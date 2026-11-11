using static System.Net.HttpStatusCode;

namespace SylvanBot.Features.Tickets;

using System.Text;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketPublisher(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    TicketLocks locks,
    TicketDeliveryBackoff backoff,
    ILogger logger)
{
    internal async Task<TicketPublicationResult> SynchronizeAsync(ulong guildId, long id, CancellationToken token, bool allowDisabled = false)
    {
        using var timing = new OperationTiming(logger.ForContext("GuildId", guildId).ForContext("TicketId", id), "ticket.publish", "synchronise");
        var changed = false;
        var failed = false;
        var deferred = false;

        try
        {
            List<TicketMessage> copies;

            await using (var db = await contexts.CreateDbContextAsync(token))
            {
                copies = await db.Tickets
                    .AsNoTracking()
                    .Where(ticket => ticket.GuildId == guildId && ticket.Id == id)
                    .SelectMany(ticket => ticket.Publications)
                    .Where(copy => !copy.Removed)
                    .Select(copy => new TicketMessage(copy.ChannelId, copy.MessageId))
                    .ToListAsync(token);
            }

            foreach (var copy in copies)
            {
                // Cleanup and editing use separate leases. A busy ticket cannot extend this pass indefinitely.
                for (var step = 0; step < 2; step++)
                {
                    var result = await SynchronizeCopyAsync(guildId, id, copy, token, allowDisabled);
                    changed |= result.Changed;
                    failed |= result.Outcome == TicketPublicationOutcome.Failed;
                    deferred |= result.Outcome == TicketPublicationOutcome.Deferred;

                    if (result.Outcome != TicketPublicationOutcome.Pending)
                        break;
                }
            }

            var final = await PublishNewAsync(guildId, id, token, allowDisabled);
            changed |= final.Changed;

            if (final.Outcome == TicketPublicationOutcome.Uncertain)
                return final with { Changed = changed };

            if (failed)
                return new TicketPublicationResult(TicketPublicationOutcome.Failed,
                    "Some recorded copies could not be synchronised. Their changes will retry.", changed);

            if (deferred && final.Outcome != TicketPublicationOutcome.Failed)
                return new TicketPublicationResult(TicketPublicationOutcome.Deferred,
                    "Some channel updates are waiting for Discord's rate limit.", changed);

            return final with { Changed = changed };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Publication pass failed for ticket {TicketId} in guild {GuildId}", id, guildId);
            return new TicketPublicationResult(TicketPublicationOutcome.Failed, "Publication could not be confirmed. Saved changes will retry.", changed);
        }
    }

    private async Task<(TicketPublicationOutcome Outcome, bool Changed)> SynchronizeCopyAsync(
        ulong guildId, long id, TicketMessage copy, CancellationToken token, bool allowDisabled)
    {
        using var gate = await locks.TicketAsync(id, token);
        var changed = false;

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            if (!allowDisabled)
                await TicketCoordinator.EnabledAsync(db, guildId, token);

            var ticket = await db.Tickets
                .SingleAsync(value => value.GuildId == guildId && value.Id == id, token);
            var reference = ticket.Publications
                .SingleOrDefault(value => value.ChannelId == copy.ChannelId && value.MessageId == copy.MessageId);

            if (reference is null || !NeedsSynchronization(ticket, reference))
                return (TicketPublicationOutcome.Complete, false);

            if (backoff.IsDeferred(copy.ChannelId))
                return (TicketPublicationOutcome.Deferred, false);

            var channel = await GetChannelAsync(guildId, copy.ChannelId, token);

            try
            {
                await InspectCopyAsync(channel, reference, token);
                var keepMessage = !ticket.Hidden && reference.Current && reference.Format == TicketPublicationFormat.Embed;

                if (!reference.Removed && reference.ApprovalReactionCleanupPending && keepMessage)
                {
                    // Claim before the single attempt, including uncertain failures.
                    reference.ApprovalReactionCleanupPending = false;
                    await db.SaveChangesAsync(token);
                    changed = true;
                    await client.Rest.RemoveAllReactionsAsync(copy.ChannelId, copy.MessageId, backoff.Options(copy.ChannelId, token));
                }
                else if (!reference.Removed)
                {
                    if (!keepMessage)
                    {
                        await channel.DeleteMessageAsync(copy.MessageId, backoff.Options(copy.ChannelId, token));
                        MarkRemoved(reference);
                    }
                    else
                    {
                        await UpdateAsync(channel, copy.MessageId, ticket, token);
                        reference.Revision = ticket.Version;
                    }
                }
            }
            catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMessage)
            {
                MarkRemoved(reference);
            }

            await db.SaveChangesAsync(token);
            backoff.Succeeded(copy.ChannelId);
            var outcome = NeedsSynchronization(ticket, reference) ? TicketPublicationOutcome.Pending : TicketPublicationOutcome.Complete;
            return (outcome, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is RateLimitedException || exception is HttpException { HttpCode: TooManyRequests })
        {
            backoff.Failed(copy.ChannelId);
            return (TicketPublicationOutcome.Deferred, changed);
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Ticket {TicketId}: publication {MessageId} in channel {ChannelId} could not be synchronised",
                id, copy.MessageId, copy.ChannelId);
            return (TicketPublicationOutcome.Failed, changed);
        }
    }

    private async Task InspectCopyAsync(ITextChannel channel, TicketPublication reference, CancellationToken token)
    {
        if (reference.Format != TicketPublicationFormat.Unknown)
            return;

        var message = await channel.GetMessageAsync(reference.MessageId, CacheMode.AllowDownload, backoff.Options(channel.Id, token));

        if (message is null)
        {
            MarkRemoved(reference);
            return;
        }

        if (message.Author.Id != client.CurrentUser.Id)
            throw new BotCommandException("Sylvan can only change messages it authored.");

        reference.Format = message.Flags?.HasFlag(MessageFlags.ComponentsV2) == true
            ? TicketPublicationFormat.ComponentsV2 : TicketPublicationFormat.Embed;
    }

    private static bool NeedsSynchronization(Ticket ticket, TicketPublication copy) => !copy.Removed
        && (copy.ApprovalReactionCleanupPending || ticket.Hidden || !copy.Current || copy.Revision != ticket.Version);

    private static void MarkRemoved(TicketPublication reference)
    {
        reference.Removed = true;
        reference.Current = false;
        reference.ApprovalReactionCleanupPending = false;
    }

    private async Task<ITextChannel> GetChannelAsync(ulong guildId, ulong channelId, CancellationToken token)
    {
        var guild = (IGuild?)client.GetGuild(guildId) ?? await client.Rest.GetGuildAsync(guildId, backoff.Options(channelId, token));
        return await guild.GetTextChannelAsync(channelId, options: backoff.Options(channelId, token))
            ?? throw new BotCommandException("The tickets channel is unavailable.");
    }

    private async Task<TicketPublicationResult> PublishNewAsync(ulong guildId, long id, CancellationToken token, bool allowDisabled)
    {
        using var gate = await locks.TicketAsync(id, token);
        ulong destinationId = 0;
        var sending = false;

        try
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var config = allowDisabled
                ? await db.TicketConfigurations.SingleAsync(value => value.GuildId == guildId, token)
                : await TicketCoordinator.EnabledAsync(db, guildId, token);
            var ticket = await db.Tickets
                .SingleAsync(value => value.GuildId == guildId && value.Id == id, token);
            var unresolved = ticket.Publications
                .Any(copy => NeedsSynchronization(ticket, copy));

            if (unresolved)
                return new TicketPublicationResult(TicketPublicationOutcome.Pending, "Recorded channel changes remain queued.");

            if (ticket.SendUncertain)
                return new TicketPublicationResult(TicketPublicationOutcome.Uncertain, "A new publication remains unconfirmed. Inspect the channel before reposting.");

            var hasCurrent = ticket.Publications
                .Any(copy => copy.Current && !copy.Removed);

            if (ticket.Hidden || hasCurrent)
                return new TicketPublicationResult(TicketPublicationOutcome.Complete, string.Empty);

            destinationId = config.PublicationChannel(ticket);

            if (backoff.IsDeferred(destinationId))
                return new TicketPublicationResult(TicketPublicationOutcome.Deferred, "Publication is waiting for Discord's rate limit.");

            var channel = await GetChannelAsync(guildId, destinationId, token);
            ticket.SendUncertain = true;
            await db.SaveChangesAsync(token);
            sending = true;
            IUserMessage sent;

            try
            {
                sent = await SendAsync(channel, ticket, token);
            }
            catch (Exception exception) when (exception is RateLimitedException
                || exception is HttpException { HttpCode: BadRequest or Forbidden or NotFound or TooManyRequests })
            {
                ticket.SendUncertain = false;
                await db.SaveChangesAsync(token);
                sending = false;
                throw;
            }

            ticket.Publications.Add(new TicketPublication
            {
                ChannelId = channel.Id,
                MessageId = sent.Id,
                Current = true,
                Revision = ticket.Version,
                Format = TicketPublicationFormat.Embed,
            });
            ticket.SendUncertain = false;
            await db.SaveChangesAsync(token);
            sending = false;
            backoff.Succeeded(destinationId);
            return new TicketPublicationResult(TicketPublicationOutcome.Complete,
                $"[Jump to ticket message]({TicketPresentation.Link(guildId, channel.Id, sent.Id)})", Changed: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is RateLimitedException || exception is HttpException { HttpCode: TooManyRequests })
        {
            backoff.Failed(destinationId);
            return new TicketPublicationResult(TicketPublicationOutcome.Deferred, "Publication is waiting for Discord's rate limit.");
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Publication failed for ticket {TicketId} in guild {GuildId}", id, guildId);
            return sending
                ? new TicketPublicationResult(TicketPublicationOutcome.Uncertain, "A new publication remains unconfirmed. Inspect the channel before reposting.")
                : new TicketPublicationResult(TicketPublicationOutcome.Failed, "Publication could not be confirmed. Saved changes will retry.");
        }
    }

    private async Task<IUserMessage> SendAsync(IMessageChannel channel, Ticket ticket, CancellationToken token)
    {
        var options = backoff.Options(channel.Id, token);
        var embed = TicketPresentation.Describe(ticket);

        if (ticket.Comment.Length <= TicketPresentation.InlineCommentLimit)
            return await channel.SendMessageAsync(embed: embed, options: options, allowedMentions: AllowedMentions.None);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));

        return await channel.SendFileAsync(
            stream,
            $"{ticket.Number}-comment.txt",
            embed: embed,
            allowedMentions: AllowedMentions.None,
            options: options);
    }

    private async Task UpdateAsync(IMessageChannel channel, ulong messageId, Ticket ticket, CancellationToken token)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));

        await channel.ModifyMessageAsync(
            messageId,
            properties =>
            {
                properties.Content = string.Empty;
                properties.Embeds = new[] { TicketPresentation.Describe(ticket) };
                properties.Components = new ComponentBuilder().Build();

                properties.Attachments = ticket.Comment.Length > TicketPresentation.InlineCommentLimit
                    ? new[] { new FileAttachment(stream, $"{ticket.Number}-comment.txt") }
                    : Array.Empty<FileAttachment>();

                properties.AllowedMentions = AllowedMentions.None;
            },
            backoff.Options(channel.Id, token));
    }

    public static TicketMessage? FindMessage(TicketActor actor, Ticket ticket)
    {
        if (actor.GuildId != ticket.GuildId)
            throw new BotCommandException("This ticket belongs to another server.");

        // A recorded jump link is navigation, not authorisation to read the ticket.
        // Publication reconciliation owns checking whether the message still exists.
        var reference = ticket.Publications
           .Where(reference => !reference.Removed)
           .OrderByDescending(reference => reference.Current)
           .FirstOrDefault();

        return reference?.Message();
    }

}


public enum TicketPublicationOutcome
{
    Complete,
    Pending,
    Deferred,
    Uncertain,
    Failed,
}


public sealed record TicketPublicationResult(TicketPublicationOutcome Outcome, string Message, bool Changed = false);
