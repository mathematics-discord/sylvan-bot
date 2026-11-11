namespace SylvanBot.Features.Tickets;

using DotNext;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;

public sealed class TicketReviewSummary(
    DiscordSocketClient client,
    IDbContextFactory<BotDbContext> contexts,
    ILogger logger,
    TicketDeliveryBackoff backoff)
{
    internal const string ReviewButtonId = "ticket:review-start";
    private const string Heading = "## Tickets awaiting approval";
    private readonly ConcurrentDictionary<ulong, ulong> _channels = new ConcurrentDictionary<ulong, ulong>();

    private readonly ConcurrentDictionary<ulong, byte> _dirty = new ConcurrentDictionary<ulong, byte>();
    private DateTimeOffset _reconcileAfter;

    public void MarkDirty(ulong guildId) => _dirty[guildId] = 0;

    public bool MarkChannelDirty(ulong channelId)
    {
        if (!_channels.TryGetValue(channelId, out var guildId))
            return false;

        MarkDirty(guildId);
        return true;
    }

    // Only the publication loop runs refreshes; gateway callbacks wake it
    public async Task RefreshAsync(CancellationToken token)
    {
        var reconcile = DateTimeOffset.UtcNow >= _reconcileAfter;

        if (reconcile)
            _reconcileAfter = DateTimeOffset.UtcNow.AddMinutes(5);

        ulong after = 0;

        while (true)
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            List<TicketConfiguration> configurations = await db.TicketConfigurations
               .Where(config => config.GuildId > after
                          && ((config.Enabled && config.DestinationChannelId != null)
                              || config.ReviewSummaryMessageId != null))
               .OrderBy(config => config.GuildId)
               .Take(100)
               .ToListAsync(token);

            if (configurations.Count == 0)
                return;

            foreach (var config in configurations)
            {
                var dirty = _dirty.TryRemove(config.GuildId, out _);

                if (!reconcile && !dirty)
                    continue;

                if (backoff.IsDeferred(config.DestinationChannelId ?? config.ReviewSummaryChannelId ?? 0))
                {
                    MarkDirty(config.GuildId);
                    continue;
                }

                try
                {
                    await RefreshGuildAsync(db, config, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    MarkDirty(config.GuildId);

                    if (exception is RateLimitedException || exception is HttpException { HttpCode: System.Net.HttpStatusCode.TooManyRequests })
                        backoff.Failed(config.DestinationChannelId ?? config.ReviewSummaryChannelId ?? 0);

                    logger.Warning(exception, "Ticket review summary could not be updated in guild {GuildId}", config.GuildId);
                    db.Entry(config).State = EntityState.Detached;
                }
            }

            after = configurations[^1].GuildId;
        }
    }

    private async Task RefreshGuildAsync(BotDbContext db, TicketConfiguration config, CancellationToken token)
    {
        var destination = config.Enabled ? config.DestinationChannelId : null;
        var options = backoff.Options(config.DestinationChannelId ?? config.ReviewSummaryChannelId ?? 0, token);
        var guild = (IGuild?)client.GetGuild(config.GuildId) ?? await client.Rest.GetGuildAsync(config.GuildId, options);

        if (config.ReviewSummaryChannelId is { } previous && previous != destination)
        {
            var oldChannel = await guild.GetTextChannelAsync(previous, options: options);

            if (oldChannel is not null && config.ReviewSummaryMessageId is { } oldMessage)
                await DeleteAsync(oldChannel, oldMessage, token);

            _channels.TryRemove(previous, out _);
            config.ReviewSummaryChannelId = null;
            config.ReviewSummaryMessageId = null;
            await db.SaveChangesAsync(token);
        }

        if (destination is not { } channelId)
            return;

        _channels.TryAdd(channelId, config.GuildId);

        var channel = await guild.GetTextChannelAsync(channelId, options: options)
            ?? throw new InvalidOperationException("The tickets channel is unavailable.");

        var summary = await FindSummaryAtBottomAsync(channel, config.ReviewSummaryMessageId, token);
        var text = await DescribeAsync(db, config, token);

        var row = new ActionRowBuilder()
           .WithButton("Review tickets", ReviewButtonId);

        var container = new ContainerBuilder()
           .WithTextDisplay(new TextDisplayBuilder().WithContent(text))
           .WithActionRow(row);

        var components = new ComponentBuilderV2(container).Build();

        if (summary is null)
        {
            summary = await channel.SendMessageAsync(
                components: components,
                flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None,
                options: options);
        }
        else if (!string.Equals(SummaryText(summary), text, StringComparison.Ordinal))
        {
            await summary.ModifyAsync(
                properties =>
                {
                    properties.Components = components;
                    properties.AllowedMentions = AllowedMentions.None;
                },
                options);
        }

        config.ReviewSummaryChannelId = channelId;
        config.ReviewSummaryMessageId = summary.Id;
        await db.SaveChangesAsync(token);
    }

    private async Task<IUserMessage?> FindSummaryAtBottomAsync(IMessageChannel channel, ulong? storedId, CancellationToken token)
    {
        var options = backoff.Options(channel.Id, token);

        IEnumerable<IMessage> recent = await channel.GetMessagesAsync(options: options)
           .FlattenAsync();

        List<IMessage> messages = recent
           .OrderByDescending(message => message.Id)
           .ToList();

        // Recover a send that reached Discord before the database save completed.
        List<IUserMessage> summaries = messages
           .OfType<IUserMessage>()
           .Where(IsSummary)
           .ToList();

        var storedIsRecent = summaries
           .Any(message => message.Id == storedId);

        if (storedId is { } stored && !storedIsRecent)
        {
            var saved = await channel.GetMessageAsync(stored, options: options);

            if (saved is IUserMessage message && IsSummary(message))
                summaries.Add(message);
        }

        var latest = messages
           .FirstOrDefault();

        var summary = summaries
           .FirstOrDefault(message => message.Id == latest?.Id);

        foreach (var obsolete in summaries.Where(obsolete => obsolete.Id != summary?.Id))
            await DeleteAsync(channel, obsolete.Id, token);

        return summary;
    }

    private bool IsSummary(IUserMessage message) => message.Author.Id == client.CurrentUser.Id
        && message.Components
           .OfType<ContainerComponent>()
           .SelectMany(container => container.Components)
           .OfType<ActionRowComponent>()
           .SelectMany(row => row.Components)
           .OfType<ButtonComponent>()
           .Any(button => string.Equals(button.CustomId, ReviewButtonId, StringComparison.Ordinal));

    private static string? SummaryText(IUserMessage message) => message.Components
       .OfType<ContainerComponent>()
       .SelectMany(container => container.Components)
       .OfType<TextDisplayComponent>()
       .FirstOrDefault()
      ?.Content;

    private async Task DeleteAsync(IMessageChannel channel, ulong messageId, CancellationToken token)
    {
        try
        {
            await channel.DeleteMessageAsync(messageId, backoff.Options(channel.Id, token));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            // A moderator may already have deleted the summary.
        }
    }

    private static async Task<string> DescribeAsync(BotDbContext db, TicketConfiguration config, CancellationToken token)
    {
        IQueryable<Ticket> pending = TicketCoordinator.AwaitingApproval(db.Tickets.AsNoTracking(), config.GuildId);
        var count = await pending.CountAsync(token);
        var text = new StringBuilder(Heading)
           .AppendLine()
           .AppendLine(count == 0 ? "No tickets are awaiting approval." : $"**{count}** tickets awaiting approval.");

        var shown = 0;

        await foreach (var ticket in pending.OrderBy(ticket => ticket.Number).AsAsyncEnumerable().WithCancellation(token))
        {
            var publication = ticket.Publications.FirstOrDefault(copy => copy.Current && !copy.Removed);
            Optional<TicketMessage> published = publication is null
                ? Optional<TicketMessage>.None
                : publication.Message();
            var line = ReviewLine(ticket, published);
            if (text.Length + line.Length + 1 > 3950)
                break;

            text.AppendLine(line);
            shown++;
        }

        if (shown < count)
            text.Append(CultureInfo.InvariantCulture, $"…and {count - shown} more.");

        return text.ToString().TrimEnd();
    }

    internal static string ReviewLine(Ticket ticket, Optional<TicketMessage> published)
    {
        var label = $"#{ticket.Number}";
        if (published.TryGet(out var target))
            label = $"[{label}]({TicketPresentation.Link(ticket.GuildId, target.ChannelId, target.MessageId)})";

        return $"- {label} - {TicketPresentation.ActionDescription(ticket)}";
    }

}
