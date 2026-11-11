using System.Globalization;
using SylvanBot.Application.Features.Activity;

namespace SylvanBot.Features.Activity;

internal static class ActivityConfigurationView
{
    public static MessageComponent Build(SocketGuild guild, ActivitySettings settings, Dictionary<ulong, decimal> weights, int page, ulong ownerId)
    {
        List<string> entries = [];

        SocketGuildChannel[] channels = guild.Channels
            .Where(channel => channel is not SocketThreadChannel and not SocketCategoryChannel)
            .OrderBy(channel => channel is SocketVoiceChannel ? 1 : 0)
            .ThenBy(channel => channel.Position)
            .ThenBy(channel => channel.Id)
            .ToArray();

        foreach (var channel in channels.Where(channel => CategoryId(channel) is null))
            if (weights.TryGetValue(channel.Id, out var weight) && weight != 1)
                entries.Add(Format(channel.Id, weight));

        IOrderedEnumerable<SocketCategoryChannel> categories = guild.CategoryChannels
            .OrderBy(category => category.Position)
            .ThenBy(category => category.Id);

        foreach (var category in categories)
        {
            var weight = weights.GetValueOrDefault(category.Id, 1m);

            SocketGuildChannel[] children = channels
                .Where(channel => CategoryId(channel) == category.Id && weights.TryGetValue(channel.Id, out var childWeight) && childWeight != weight)
                .ToArray();

            var heading = Format(category.Id, weight);

            if (children.Length == 0 && weight != 1)
                entries.Add(heading);

            entries.AddRange(
                children.Chunk(20)
                    .Select(group => group.Select(child => "  " + Format(child.Id, weights[child.Id])))
                    .Select(rows => heading + "\n" + string.Join('\n', rows)));
        }

        AddThreadWeights(guild, weights, entries);
        return BuildPages(guild, settings, entries, page, ownerId);
    }

    private static void AddThreadWeights(SocketGuild guild, Dictionary<ulong, decimal> weights, List<string> entries)
    {
        IEnumerable<IGrouping<ulong, SocketThreadChannel>> configuredThreads = guild.ThreadChannels
            .Where(thread => weights.ContainsKey(thread.Id))
            .OrderBy(thread => thread.ParentChannel.Id)
            .ThenBy(thread => thread.Id)
            .GroupBy(thread => thread.ParentChannel.Id);

        entries.AddRange(
            from @group in configuredThreads
            from threads in @group.Chunk(20)
            let rows = threads.Select(thread => "  " + Format(thread.Id, weights[thread.Id]))
            select $"Threads in <#{@group.Key}>\n" + string.Join('\n', rows));

        IEnumerable<ulong> threadIds = guild.ThreadChannels
            .Select(thread => thread.Id);

        HashSet<ulong> knownIds = guild.Channels
            .Select(channel => channel.Id)
            .Concat(threadIds)
            .ToHashSet();

        IOrderedEnumerable<KeyValuePair<ulong, decimal>> uncachedWeights = weights
            .Where(pair => !knownIds.Contains(pair.Key))
            .OrderBy(pair => pair.Key);

        entries.AddRange(uncachedWeights.Select(pair => Format(pair.Key, pair.Value)));
    }

    private static MessageComponent BuildPages(SocketGuild guild, ActivitySettings settings, List<string> entries, int requestedPage, ulong ownerId)
    {
        List<string> pages = [];
        var text = string.Empty;

        foreach (var entry in entries)
        {
            if (text.Length + entry.Length > 2800)
            {
                pages.Add(text);
                text = string.Empty;
            }

            text += entry + "\n";
        }

        if (text.Length > 0)
            pages.Add(text);

        if (pages.Count == 0)
            pages.Add("All channels and threads use their inherited or default weight of 1.0x.");

        var index = Math.Clamp(requestedPage, 0, pages.Count - 1);

        var roles = settings.Role1Id == 0
            ? "Not configured."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"- Role 1 — <@&{settings.Role1Id}> — {settings.Role1Score} XP\n- Role 2 — <@&{settings.Role2Id}> — {settings.Role2Score} XP");

        var cap = settings.MaxDaily == 0 ? "Unlimited" : settings.MaxDaily.ToString(CultureInfo.InvariantCulture) + " XP";

        var buttons = new ActionRowBuilder()
            .WithButton("Prev", $"activity-page:{guild.Id}:{ownerId}:{Math.Max(0, index - 1)}", ButtonStyle.Secondary, disabled: index == 0)
            .WithButton("Next", $"activity-page:{guild.Id}:{ownerId}:{index + 1}", ButtonStyle.Secondary, disabled: index == pages.Count - 1);

        return new ComponentBuilderV2()
            .WithTextDisplay("# Activity Configuration")
            .WithSeparator()
            .WithTextDisplay("## Roles and Scores\n" + roles + "\n\nRolling 24-hour maximum: " + cap)
            .WithSeparator()
            .WithTextDisplay($"## XP Weights\n{pages[index]}\nPage {index + 1} of {pages.Count}")
            .WithActionRow(buttons)
            .Build();
    }

    private static ulong? CategoryId(SocketGuildChannel channel) => channel switch
    {
        SocketForumChannel forum => forum.CategoryId,
        SocketTextChannel text => text.CategoryId,
        _ => null,
    };

    private static string Format(ulong id, decimal weight) => string.Create(
        CultureInfo.InvariantCulture,
        $"- <#{id}> — {weight:0.0###########################}x");
}