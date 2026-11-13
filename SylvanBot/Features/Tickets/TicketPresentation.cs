namespace SylvanBot.Features.Tickets;

using System.Globalization;
using SylvanBot.Application.Features.Tickets;

internal static class TicketPresentation
{
    internal const int InlineCommentLimit = 3500;

    internal static string Link(ulong guildId, ulong channelId, ulong messageId) => $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";

    internal static string ActionName(TicketAction action) => action switch
    {
        TicketAction.VoiceMute => "Voice mute",
        TicketAction.VoiceDeafen => "Voice deafen",
        _ => action.ToString(),
    };

    internal static MessageComponent Buttons(Ticket ticket, TicketActor? actor = null)
    {
        var builder = new ComponentBuilder();
        var isNote = ticket.Action == TicketAction.Note;
        var senior = actor?.IsAdministrator != false || actor.CanViewAuditLog;

        if (actor is null || ticket.CanCorrect(actor))
            builder.WithButton("Edit", $"ticket:edit:{ticket.Number}");

        if (!ticket.Hidden && (isNote || senior))
            builder.WithButton(
                "Hide",
                isNote ? $"ticket:hide:{ticket.Number}" : $"ticket:moderation-hide:{ticket.Number}:{ticket.Version}",
                ButtonStyle.Danger);

        if (!isNote && senior)
        {
            var label = ticket.IsApproved ? "Approved" : "Approve";

            builder.WithButton(
                label,
                $"ticket:approve:{ticket.Number}:{ticket.Version}",
                ButtonStyle.Success,
                disabled: ticket.IsApproved);
        }

        return builder.Build();
    }

    internal static string ActionDescription(Ticket ticket)
    {
        var subject = $"<@{ticket.SubjectUserId}>";
        var moderator = $"<@{ticket.ModeratorUserId}>";
        return ticket.Action switch
        {
            TicketAction.Note => $"**Note** for {subject} by {moderator}",
            TicketAction.Ban => $"{subject} banned by {moderator}",
            TicketAction.Timeout => $"{subject} timed out by {moderator}",
            TicketAction.VoiceMute => $"{subject} muted by {moderator}",
            TicketAction.VoiceDeafen => $"{subject} deafened by {moderator}",
            TicketAction.Kick => $"{subject} kicked by {moderator}",
            _ => $"{subject} given <@&{ticket.RoleId}> by {moderator}",
        };
    }

    internal static Embed Describe(Ticket ticket)
    {
        var isNote = ticket.Action == TicketAction.Note;
        var description = ActionDescription(ticket);
        var status = string.Empty;

        if (ticket.Status == TicketStatus.Reverted)
            status = " (REVERTED)";
        else if (!isNote && !ticket.IsApproved)
            status = " (UNAPPROVED)";

        var embed = new EmbedBuilder()
            .WithAuthor($"Ticket #{ticket.Number}")
            .WithTitle(ActionName(ticket.Action) + status + (isNote && ticket.Hidden ? " · Hidden" : string.Empty))
            .WithColor(ModerationColour(ticket))
            .WithTimestamp(ticket.CreatedAtUtc);

        if (ticket.Action == TicketAction.Ban)
            embed.WithThumbnailUrl("https://media.tenor.com/PHREyAefRckAAAAM/packwatch.gif");

        if (ticket.Comment.Length > InlineCommentLimit)
            description += "\n\nThe full comment is attached.";
        else if (ticket.Comment.Length > 0)
            description += $"\n\n{ticket.Comment}";

        string FormatId(ulong id) => id.ToString(CultureInfo.InvariantCulture);

        embed.WithDescription(description);
        embed.AddField("User ID", FormatId(ticket.SubjectUserId), inline: true);
        embed.AddField("Moderator ID", FormatId(ticket.ModeratorUserId), inline: true);

        if (ticket.Action != TicketAction.Kick)
            embed.AddField("Duration", ticket.DurationSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds).ToString()
                : "**Permanent**", inline: true);

        if (ticket.ExpiresAtUtc is { } expiry)
            embed.AddField(ticket.Action is TicketAction.VoiceMute or TicketAction.VoiceDeafen ? "Descriptive end" : "Expires", FormatExpiry(expiry), inline: true);

        if (ticket.Action is TicketAction.VoiceMute or TicketAction.VoiceDeafen)
            embed.AddField("Voice restriction", "Lift this restriction manually in Discord. Its duration is descriptive.");

        if (ticket.Action == TicketAction.Role)
            embed.AddField("Role", $"<@&{ticket.RoleId}> (`{ticket.RoleId}`)", inline: true);

        if (ticket.Pending is not null)
            embed.AddField("Pending", "Expiry or a Discord change is awaiting confirmation.");
        if (ticket.ExpirySuspended && ticket.Status == TicketStatus.Active
            && ticket.Action is not (TicketAction.VoiceMute or TicketAction.VoiceDeafen))
            embed.AddField(ticket.Action == TicketAction.Timeout ? "Enforcement paused" : "Expiry paused",
                "Automatic changes are paused because the saved restriction could not be verified. Use Edit to set a new duration.");

        AddHistory(embed, ticket);

        return embed.Build();
    }

    private static string FormatExpiry(DateTimeOffset expiry)
    {
        var timestamp = expiry.ToUnixTimeSeconds();
        return $"<t:{timestamp}:F>, <t:{timestamp}:R>";
    }

    internal static string OperationalDetails(Ticket ticket)
    {
        var text = ticket.Complete ? "Details complete." : "A nonblank comment is required.";

        if (ticket.ReminderExempt)
            text += " Automated action; no moderator reminder required.";

        if (ticket.Pending is not null)
            text += " A Discord change is awaiting reconciliation.";

        if (ticket.ExpirySuspended && ticket.Action is not (TicketAction.VoiceMute or TicketAction.VoiceDeafen))
            text += ticket.Action == TicketAction.Timeout
                ? " Automatic timeout changes are paused. Use Edit to set a new duration."
                : " Automatic expiry is paused. Use Edit to set a new duration.";

        if (ticket.Status == TicketStatus.Superseded)
            text += $" Superseded by ticket #{ticket.SupersededByNumber}.";

        return text;
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
            TicketAction.Note => (Pending: 0xFFFFFF, Complete: 0xFFFFFF, Ended: 0x666666),
            TicketAction.Ban => (Pending: 0xFF6666, Complete: 0xFF0000, Ended: 0x990000),
            TicketAction.Timeout => (Pending: 0xBBFF55, Complete: 0x99FF00, Ended: 0x559900),
            TicketAction.Kick => (Pending: 0xFFBB55, Complete: 0xFF9900, Ended: 0x995500),
            TicketAction.VoiceDeafen => (Pending: 0xCC66FF, Complete: 0x9900FF, Ended: 0x550099),
            TicketAction.Role => (Pending: 0xFFFF99, Complete: 0xFFFF00, Ended: 0x999900),
            _ => (Pending: 0xFF55BB, Complete: 0xFF0099, Ended: 0x990055),
        };

        var colour = ticket.Complete ? palette.Complete : palette.Pending;

        if (ticket.Status is TicketStatus.Expired or TicketStatus.Reverted)
            colour = palette.Ended;

        if (ticket.Hidden)
            colour = 0x000000;

        return new Color((uint)colour);
    }
}
