using static System.StringComparison;

namespace SylvanBot.Features.Tickets;

using System.Globalization;
using SylvanBot.Application.Features.Tickets;

internal static class TicketPresentation
{
    internal const int InlineCommentLimit = 3500;

    internal static string Link(ulong guildId, ulong channelId, ulong messageId) => $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    internal static bool UsesModerationEmbed(Ticket ticket) => ticket.Action is TicketAction.Ban or TicketAction.Timeout or TicketAction.VoiceMute;

    internal static string ActionName(TicketAction action) => action switch
    {
        TicketAction.VoiceMute => "Voice mute",
        TicketAction.VoiceDeafen => "Voice deafen",
        _ => action.ToString(),
    };

    internal static MessageComponent Buttons(Ticket ticket, TicketActor? actor = null)
    {
        var builder = new ComponentBuilder();
        var moderation = UsesModerationEmbed(ticket);
        var senior = actor is null || actor.IsAdministrator || actor.CanViewAuditLog;
        var canEditStoredType = actor is not null && (actor.IsAdministrator || actor.CanViewAuditLog);

        if (ticket.Action == TicketAction.Note || moderation || canEditStoredType)
        {
            if (actor is null || ticket.CanCorrect(actor))
                builder.WithButton("Edit", $"ticket:edit:{ticket.Number}");

            if (!ticket.Hidden && (ticket.Action == TicketAction.Note || moderation && senior))
                builder.WithButton(
                    "Hide",
                    moderation
                        ? $"ticket:moderation-hide:{ticket.Number}:{ticket.Version}"
                        : $"ticket:hide:{ticket.Number}",
                    ButtonStyle.Danger);
        }

        if (moderation && senior)
        {
            var label = ticket.ApprovedByUserId is null ? "Approve" : "Approved";

            if (ticket.ReviewExempt)
                label = "Review exempt";

            builder.WithButton(
                label,
                $"ticket:approve:{ticket.Number}:{ticket.Version}",
                ButtonStyle.Success,
                disabled: ticket.ReviewExempt || ticket.ApprovedByUserId is not null);
        }

        return builder.Build();
    }

    internal static MessageComponent ChannelMessage(Ticket ticket)
    {
        var description = Describe(ticket);

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder().WithContent($"## {description.Title}"))
            .WithTextDisplay(new TextDisplayBuilder().WithContent(description.Description));

        if (ticket.Action == TicketAction.Note)
            container.WithAccentColor(new Color(0x808080));

        if (ticket.Comment.Length > InlineCommentLimit)
            container.WithFile(
                new FileComponentBuilder()
                    .WithFile(new UnfurledMediaItemProperties($"attachment://{ticket.Number}-comment.txt")));

        var fields = description.Fields
            .Select(field => $"**{field.Name}:** {field.Value}")
            .ToArray();

        if (fields.Length > 0)
            container.WithSeparator(new SeparatorBuilder())
                .WithTextDisplay(new TextDisplayBuilder().WithContent(string.Join('\n', fields)));

        IEnumerable<ActionRowComponent> rows = Buttons(ticket)
            .Components
            .OfType<ActionRowComponent>();

        foreach (var row in rows)
            container.WithActionRow(row.ToBuilder());

        return new ComponentBuilderV2(container).Build();
    }

    internal static Embed Describe(Ticket ticket)
    {
        var embed = new EmbedBuilder();
        var moderation = UsesModerationEmbed(ticket);
        var description = $"{ActionName(ticket.Action)} for <@{ticket.SubjectUserId}>";

        if (moderation)
        {
            var verb = ticket.Action switch
            {
                TicketAction.Ban => "banned",
                TicketAction.Timeout => "timed out",
                _ => "muted",
            };

            description = $"<@{ticket.SubjectUserId}> {verb} by <@{ticket.ModeratorUserId}>";
            var unapproved = ticket.ApprovedByUserId is null && !ticket.ReviewExempt;

            embed.WithAuthor($"Ticket #{ticket.Number}")
                .WithTitle(ActionName(ticket.Action) + (unapproved ? " (UNAPPROVED)" : string.Empty))
                .WithThumbnailUrl("https://media.tenor.com/PHREyAefRckAAAAM/packwatch.gif")
                .WithColor(ModerationColour(ticket))
                .WithTimestamp(ticket.CreatedAtUtc);
        }
        else
        {
            embed.WithTitle($"Ticket #{ticket.Number}" + (ticket.Hidden ? " · Hidden" : string.Empty));
            embed.AddField("Moderator", $"<@{ticket.ModeratorUserId}>", inline: true);
        }

        if (!moderation && ticket.Comment.Length > InlineCommentLimit)
            description += "\n\nThe full comment is attached.";
        else if (ticket.Comment.Length > 0)
            description += $"\n\n{ticket.Comment}";

        embed.WithDescription(description);
        embed.AddField("User ID", $"`{ticket.SubjectUserId}`", inline: true);

        if (moderation)
        {
            embed.AddField("Moderator ID", $"`{ticket.ModeratorUserId}`", inline: true);

            embed.AddField(
                "Duration",
                ticket.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds).ToString() : "**Permanent**",
                inline: true);

            if (ticket.ExpiresAtUtc is { } expiry)
                embed.AddField("Expires", TimestampTag.FormatFromDateTimeOffset(expiry, TimestampTagStyles.Relative), inline: true);
        }
        else embed.AddField("Created", $"<t:{ticket.CreatedAtUtc.ToUnixTimeSeconds()}:f>", inline: true);

        AddHistory(embed, ticket);

        if (!moderation && ticket.Action != TicketAction.Note)
            AddStoredActionFields(embed, ticket);

        return embed.Build();
    }

    private static void AddStoredActionFields(EmbedBuilder embed, Ticket ticket)
    {
        if (ticket.Status != TicketStatus.NotApplicable)
        {
            var status = ticket.Status.ToString();

            if (ticket.Status == TicketStatus.Superseded)
                status += $" · superseded by #{ticket.SupersededByNumber}";

            embed.AddField("Status", status, inline: true);
        }

        if (ticket.DurationSeconds is { } seconds)
            embed.AddField("Duration", TimeSpan.FromSeconds(seconds).ToString(), inline: true);

        if (ticket.SourceAuditId is not null)
        {
            if (ticket.DurationSeconds is null)
                embed.AddField("Duration", "Permanent", inline: true);

            if (ticket.ExpiresAtUtc is { } expiry)
                embed.AddField("Expires", $"<t:{expiry.ToUnixTimeSeconds()}:f>", inline: true);

            embed.AddField("Details", OperationalDetails(ticket));

            if (!string.IsNullOrWhiteSpace(ticket.AuditReason) && !string.Equals(ticket.AuditReason, ticket.Comment, OrdinalIgnoreCase))
                embed.AddField("Original audit reason", ticket.AuditReason);
        }

        if (ticket.Action == TicketAction.Role)
            embed.AddField("Role", ticket.RoleId!.Value.ToString(CultureInfo.InvariantCulture), inline: true);
    }

    private static void AddHistory(EmbedBuilder embed, Ticket ticket)
    {
        TicketHistoryEntry[] history = ticket.ModificationHistory
            .Where(entry => entry.IsModeratorChange)
            .OrderByDescending(entry => entry.Sequence)
            .ToArray();

        if (history.Length == 0)
            return;

        List<string> lines = [];
        var length = 0;

        foreach (var entry in history)
        {
            var line = entry.Format();

            if (length + line.Length + 1 > 950)
                break;

            lines.Insert(0, line);
            length += line.Length + 1;
        }

        if (lines.Count < history.Length)
            lines.Insert(0, "Earlier changes omitted from this view.");

        embed.AddField("Modification history", string.Join('\n', lines), inline: false);
    }

    private static Color ModerationColour(Ticket ticket)
    {
        var palette = ticket.Action switch
        {
            TicketAction.Ban => (Pending: 0xFF6666, Complete: 0xFF0000, Ended: 0x990000),
            TicketAction.Timeout => (Pending: 0xBBFF55, Complete: 0x99FF00, Ended: 0x559900),
            _ => (Pending: 0xFF55BB, Complete: 0xFF0099, Ended: 0x990055),
        };

        var colour = ticket.Complete ? palette.Complete : palette.Pending;

        if (ticket.Status is TicketStatus.Expired or TicketStatus.Reverted)
            colour = palette.Ended;

        if (ticket.Hidden)
            colour = 0x000000;

        return new Color((uint)colour);
    }

    internal static string OperationalDetails(Ticket ticket)
    {
        var text = ticket.Complete ? "Details complete." : "A nonblank comment is required.";

        if (ticket.ReminderExempt)
            text += " Automated action; no moderator reminder required.";

        if (ticket.Pending is not null)
            text += " A Discord change is awaiting reconciliation.";

        if (ticket.ExpirySuspended)
            text += " Automatic expiry is paused until the duration is confirmed.";

        if (ticket.Status == TicketStatus.Superseded)
            text += $" Superseded by ticket #{ticket.SupersededByNumber}.";

        return text;
    }
}