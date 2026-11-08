namespace SylvanBot.Features.Tickets;

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
    ILogger logger)
{
    internal const string ReviewButtonId = "ticket:review-start";
    private const string Heading = "## Tickets awaiting approval";
    private readonly ConcurrentDictionary<ulong, byte> _channels = new ConcurrentDictionary<ulong, byte>();

    public bool IsChannel(ulong channelId) => _channels.ContainsKey(channelId);

    // Only the ticket worker runs refreshes; gateway callbacks wake it
    public async Task RefreshAsync(CancellationToken token)
    {
        ulong after = 0;

        while (true)
        {
            await using var db = await contexts.CreateDbContextAsync(token);

            List<TicketConfiguration> configurations = await db.TicketConfigurations
               .Where(config => config.GuildId > after
                          && ((config.Enabled && config.DestinationChannelId != null
                              && (!config.OrderedPublicationPending || config.LegacyChannelId != null))
                              || config.ReviewSummaryMessageId != null))
               .OrderBy(config => config.GuildId)
               .Take(100)
               .ToListAsync(token);

            if (configurations.Count == 0)
                return;

            foreach (var config in configurations)
            {
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
        var options = TicketBoundary.Options(token);
        var guild = await client.Rest.GetGuildAsync(config.GuildId, options);

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

        _channels.TryAdd(channelId, 0);

        var channel = await guild.GetTextChannelAsync(channelId, options: options)
            ?? throw new InvalidOperationException("The tickets channel is unavailable.");

        var summary = await FindSummaryAtBottomAsync(channel, config.ReviewSummaryMessageId, token);
        var text = await DescribeAsync(db, config, channel, token);

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
        var options = TicketBoundary.Options(token);

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

    private static async Task DeleteAsync(IMessageChannel channel, ulong messageId, CancellationToken token)
    {
        try
        {
            await channel.DeleteMessageAsync(messageId, TicketBoundary.Options(token));
        }
        catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            // A moderator may already have deleted the summary.
        }
    }

    private async Task<string> DescribeAsync(BotDbContext db, TicketConfiguration config, ITextChannel channel, CancellationToken token)
    {
        var pending = TicketCoordinator.AwaitingApproval(db.Tickets.AsNoTracking(), config.GuildId);
        var count = await pending.CountAsync(token);
        var text = new StringBuilder(Heading)
           .AppendLine()
           .AppendLine(count == 0 ? "No tickets are awaiting approval." : $"**{count}** tickets awaiting approval.");

        var shown = 0;
        var canDisplay = new Dictionary<ulong, bool> { [channel.Id] = true };

        await foreach (var ticket in pending.OrderBy(ticket => ticket.Number).AsAsyncEnumerable().WithCancellation(token))
        {
            var sourceId = ticket.Publications.FirstOrDefault(copy => copy.Current && !copy.Removed)?.ChannelId
                ?? config.PublicationChannel(ticket);
            if (!canDisplay.TryGetValue(sourceId, out var visible))
            {
                // A shared post may show case details only to readers of their source channel.
                var source = client.GetGuild(config.GuildId)?.GetTextChannel(sourceId);
                visible = source is not null && HasSameViewPermissions(source, channel);
                canDisplay.Add(sourceId, visible);
            }

            if (!visible)
                continue;

            var line = $"- #{ticket.Number} - {TicketPresentation.ActionDescription(ticket)}";
            if (text.Length + line.Length + 1 > 3950)
                break;

            text.AppendLine(line);
            shown++;
        }

        if (shown < count)
            text.Append(CultureInfo.InvariantCulture, $"…and {count - shown} more.");

        return text.ToString().TrimEnd();
    }

    private static bool HasSameViewPermissions(IGuildChannel source, IGuildChannel destination)
    {
        static IEnumerable<(ulong, PermissionTarget, PermValue)> ViewOverwrites(IGuildChannel channel) =>
            channel.PermissionOverwrites
                .Where(overwrite => overwrite.Permissions.ViewChannel != PermValue.Inherit)
                .Select(overwrite => (overwrite.TargetId, overwrite.TargetType, overwrite.Permissions.ViewChannel));

        return ViewOverwrites(source).ToHashSet().SetEquals(ViewOverwrites(destination));
    }
}
