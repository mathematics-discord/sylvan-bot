namespace SylvanBot.Features.Tickets;

using System.Globalization;
using SylvanBot.Application.Features.Tickets;

internal static class TicketPresentation
{
    internal const int InlineCommentLimit = 3500;

    internal static string? Link(ulong guildId, ulong? channelId, ulong? msgId) =>
        channelId is not null && msgId is not null
            ? $"https://discord.com/channels/{guildId}/{channelId}/{msgId}"
            : null;

    internal static MessageComponent Buttons(Ticket ticket, TicketActor? actor = null)
    {
        var builder = new ComponentBuilder();

        if (ticket.Action == TicketAction.Note || UsesModerationEmbed(ticket) || actor is { CanViewAuditLog: true } or { IsAdministrator: true })
        {
            if (ticket.Action == TicketAction.Note || actor is null || ticket.CanCorrect(actor))
                builder.WithButton("Edit", $"ticket:edit:{ticket.Number}");

            var canHide = ticket.Action == TicketAction.Note
                || (UsesModerationEmbed(ticket) && (actor is null || actor.IsAdministrator || actor.CanViewAuditLog));

            if (!ticket.Hidden && canHide)
            {
                builder
                   .WithButton(
                        "Hide",
                        UsesModerationEmbed(ticket)
                            ? $"ticket:moderation-hide:{ticket.Number}:{ticket.Version}"
                            : $"ticket:hide:{ticket.Number}",
                        ButtonStyle.Danger);
            }
        }

        if (!UsesModerationEmbed(ticket) || actor is { IsAdministrator: false, CanViewAuditLog: false })
            return builder.Build();

        var label = ticket.ApprovedAtUtc is null ? "Approve" : "Approved";

        if (ticket.ReviewExempt)
            label = "Review exempt";

        builder.WithButton(
            label,
            $"ticket:approve:{ticket.Number}:{ticket.Version}",
            ButtonStyle.Success,
            disabled: ticket.ReviewExempt || ticket.ApprovedAtUtc is not null);

        return builder.Build();
    }

    internal static string ActionName(TicketAction action) => action switch
    {
        TicketAction.Note => "Note",
        TicketAction.Kick => "Kick",
        TicketAction.Ban => "Ban",
        TicketAction.VoiceMute => "Voice mute",
        TicketAction.VoiceDeafen => "Voice deafen",
        TicketAction.Role => "Role",
        TicketAction.Timeout => "Timeout",
        _ => "Unknown action",
    };

    internal static MessageComponent ChannelMessage(Ticket ticket)
    {
        var description = Describe(ticket);

        var container = new ContainerBuilder()
           .WithTextDisplay(new TextDisplayBuilder().WithContent($"## {description.Title}"))
           .WithTextDisplay(new TextDisplayBuilder().WithContent(description.Description));

        if (ticket.Action == TicketAction.Note)
            container.WithAccentColor(new Color(0x808080));

        if (ticket.Comment?.Length > InlineCommentLimit)
        {
            container.WithFile(
                new FileComponentBuilder()
                   .WithFile(new UnfurledMediaItemProperties($"attachment://{ticket.Number}-comment.txt")));
        }

        var fields = description.Fields
           .Select(field => $"**{field.Name}:** {field.Value}")
           .ToArray();

        if (fields.Length > 0)
        {
            container
               .WithSeparator(new SeparatorBuilder())
               .WithTextDisplay(new TextDisplayBuilder().WithContent(string.Join('\n', fields)));
        }

        IEnumerable<ActionRowComponent> rows = Buttons(ticket)
           .Components
           .OfType<ActionRowComponent>();

        foreach (var row in rows)
            container.WithActionRow(row.ToBuilder());

        return new ComponentBuilderV2(container).Build();
    }

    internal static Embed Describe(Ticket ticket)
    {
        if (UsesModerationEmbed(ticket))
            return ModerationEmbed(ticket);

        var subject = ticket.SubjectUserId is null ? "unknown user" : $"<@{ticket.SubjectUserId}>";
        var description = $"{ActionName(ticket.Action)} for {subject}";

        if (ticket.Comment?.Length > InlineCommentLimit)
            description += "\n\nThe full comment is attached.";
        else if (!string.IsNullOrEmpty(ticket.Comment))
            description += $"\n\n{ticket.Comment}";

        var title = $"Ticket #{ticket.Number}";

        if (ticket.Hidden)
            title += " · Hidden";

        var embed = new EmbedBuilder()
           .WithTitle(title)
           .WithDescription(description);

        if (ticket.ModeratorUserId is not null)
            embed.AddField("Moderator", $"<@{ticket.ModeratorUserId}>", inline: true);

        if (ticket.SubjectUserId is not null)
            embed.AddField("User ID", $"`{ticket.SubjectUserId}`", inline: true);

        if (ticket.CreatedAtUtc is not null)
            embed.AddField("Created", $"<t:{ticket.CreatedAtUtc.Value.ToUnixTimeSeconds()}:f>", inline: true);

        if (ticket.LastModifiedAtUtc is not null)
        {
            var edited = $"<t:{ticket.LastModifiedAtUtc.Value.ToUnixTimeSeconds()}:f>";

            if (ticket.LastModifiedByUserId is not null)
                edited += $" by <@{ticket.LastModifiedByUserId}>";

            embed.AddField("Last edited", edited);
        }

        AddModificationHistory(embed, ticket);

        if (ticket.Action == TicketAction.Note)
            return embed.Build();

        AddActionFields(embed, ticket);

        return embed.Build();
    }

    private static void AddActionFields(EmbedBuilder embed, Ticket ticket)
    {
        if (ticket.Status is not null)
        {
            var status = ticket.Status switch
            {
                TicketStatus.Active => "Active",
                TicketStatus.Expired => "Expired",
                TicketStatus.Reverted => "Reverted",
                TicketStatus.ExpirationFailed => "Expiration failed",
                _ => "Unknown",
            };

            if (ticket.SupersededByNumber is not null)
                status += $" · superseded by #{ticket.SupersededByNumber}";

            embed.AddField("Status", status, inline: true);
        }

        if (ticket.DurationSeconds is not null)
            embed.AddField("Duration", $"{ticket.DurationSeconds} seconds", inline: true);

        if (ticket.SourceAuditId is not null)
        {
            if (ticket.ModeratorUserId is null)
                embed.AddField("Moderator", "Unknown; Discord did not supply resolved user data.");

            if (ticket.DurationSeconds is null)
                embed.AddField("Duration", ticket.DurationKnown ? "Permanent" : "Unknown", inline: true);

            if (ticket.ExpiresAtUtc is not null)
                embed.AddField("Expires", $"<t:{ticket.ExpiresAtUtc.Value.ToUnixTimeSeconds()}:f>", inline: true);

            embed.AddField("Details", CompletionText(ticket));

            if (ticket.PendingOperationId is not null)
                embed.AddField(
                    "Discord change",
                    ticket.PendingOperationAttempted
                        ? "The outcome is awaiting audit reconciliation."
                        : "The request is prepared; reconciliation is required before an attempt.");

            if (ticket.ExpirySuspended)
                embed.AddField("Automatic expiry paused", "Audit history was unavailable. Set the duration again to authorize a new schedule.");

            if (!string.IsNullOrWhiteSpace(ticket.AuditReason) && !string.Equals(ticket.AuditReason, ticket.Comment, StringComparison.Ordinal))
                embed.AddField("Original audit reason", ticket.AuditReason);
        }

        if (ticket.RoleId is not null)
            embed.AddField("Role", ticket.RoleId.Value.ToString(CultureInfo.InvariantCulture), inline: true);
    }

    internal static bool UsesModerationEmbed(Ticket ticket) =>
        ticket.Action is TicketAction.Ban or TicketAction.Timeout or TicketAction.VoiceMute;

    private static Embed ModerationEmbed(Ticket ticket)
    {
        var complete = ticket.Completion is TicketCompletion.Complete or TicketCompletion.Automated
            || (ticket.Completion is null && !string.IsNullOrWhiteSpace(ticket.Comment));

        var action = ticket.Action switch
        {
            TicketAction.Ban => "banned",
            TicketAction.Timeout => "timed out",
            _ => "muted",
        };

        var subject = ticket.SubjectUserId is null ? "Unknown" : $"<@{ticket.SubjectUserId}>";

        var embed = new EmbedBuilder()
           .WithAuthor($"Ticket #{ticket.Number}")
           .WithTitle(
                $"{subject} {action} by <@{ticket.ModeratorUserId}>"
                + (ticket is { ReviewExempt: false, ApprovedAtUtc: null } ? " (UNAPPROVED)" : string.Empty))
           .WithDescription(ticket.Comment)
           .WithThumbnailUrl("https://media1.tenor.com/m/PHREyAefRckAAAAd/packwatch.gif")
           .WithColor(ModerationColour(ticket, complete));

        if (ticket.CreatedAtUtc is not null)
            embed.WithTimestamp(ticket.CreatedAtUtc.Value);

        embed.AddField("User ID", $"`{ticket.SubjectUserId}`", inline: true);
        embed.AddField("Moderator ID", $"`{ticket.ModeratorUserId}`", inline: true);

        AddDurationFields(embed, ticket);
        AddModificationHistory(embed, ticket);

        return embed.Build();
    }

    private static void AddModificationHistory(EmbedBuilder embed, Ticket ticket)
    {
        if (ticket.ModificationHistory.Count == 0)
            return;

        List<string> lines = [];
        var length = 0;

        for (var index = ticket.ModificationHistory.Count - 1; index >= 0; index--)
        {
            var line = ticket.ModificationHistory[index];

            if (length + line.Length + 1 > 950)
                break;

            lines.Insert(0, line);
            length += line.Length + 1;
        }

        if (lines.Count < ticket.ModificationHistory.Count)
            lines.Insert(0, "Earlier changes omitted from this view.");

        embed.AddField("Modification history", string.Join('\n', lines), inline: false);
    }

    private static Color ModerationColour(Ticket ticket, bool complete)
    {
        var ended = ticket.Status is TicketStatus.Expired or TicketStatus.Reverted;

        var palette = ticket.Action switch
        {
            TicketAction.Ban => (Pending: 0xFF6666, Complete: 0xFF0000, Ended: 0x990000),
            TicketAction.Timeout => (Pending: 0xBBFF55, Complete: 0x99FF00, Ended: 0x559900),
            _ => (Pending: 0xFF55BB, Complete: 0xFF0099, Ended: 0x990055),
        };

        var colour = complete ? palette.Complete : palette.Pending;

        if (ended)
            colour = palette.Ended;

        if (ticket.Status == TicketStatus.ExpirationFailed)
            colour = 0x0000FF;

        if (ticket.Hidden)
            colour = 0x000000;

        return new Color((uint)colour);
    }

    private static void AddDurationFields(EmbedBuilder embed, Ticket ticket)
    {
        if (ticket.DurationSeconds is not null)
        {
            var seconds = ticket.DurationSeconds.Value;
            var days = seconds / 86400;
            var duration = $"{seconds % 86400 / 3600:00}:{seconds % 3600 / 60:00}:{seconds % 60:00}";

            if (days > 0)
                duration = $"{days} day{(days == 1 ? "" : "s")}, " + duration;

            embed.AddField("Duration", duration, inline: true);
        }
        else if (ticket.DurationKnown)
            embed.AddField("Duration", "**Permanent**", inline: true);
        else
            embed.AddField("Duration", "Unknown", inline: true);
    }

    internal static string OperationalDetails(Ticket ticket)
    {
        var text = CompletionText(ticket);

        if (ticket.ExpirySuspended)
            text += "\nAutomatic expiry is paused. Set the duration again to authorize a new schedule.";

        if (ticket.HidePending)
            text += "\nA reversal and hide request is awaiting confirmation.";

        if (ticket.PendingOperationId is not null)
            text += "\nA Discord change is awaiting reconciliation.";

        if (ticket.SupersededByNumber is not null)
            text += $"\nSuperseded by ticket #{ticket.SupersededByNumber}.";

        return text;
    }

    internal static string Change(TicketChange change, bool hiding = false)
    {
        var text = PublicationChange(change, hiding);

        if (change.ModerationOutcome is not null)
            text += "\n" + change.ModerationOutcome;

        if (change.Ticket.SourceAuditId is not null)
            text += "\n" + CompletionText(change.Ticket);

        return text;
    }

    private static string CompletionText(Ticket ticket)
    {
        // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
        switch (ticket.Completion)
        {
            case TicketCompletion.Automated:
                return "Automated action; no moderator reply required.";
            case TicketCompletion.Complete:
                return "Details complete. No further reminders are scheduled.";
        }

        if (ticket.PendingOperationId is not null)
            return ticket.PendingOperationAttempted
                ? "A Discord change is awaiting confirmation. Read the ticket before supplying a duration again."
                : "A Discord change is prepared and awaiting reconciliation.";

        List<string> missing = [];

        if (!ticket.DurationKnown)
            missing.Add(ticket.Action == TicketAction.Ban ? "a duration or explicit permanent duration" : "a finite duration");

        if (string.IsNullOrWhiteSpace(ticket.Comment))
            missing.Add("a comment");

        return missing.Count == 0
            ? "Details awaiting confirmation."
            : "Still needed: " + string.Join(" and ", missing) + ".";
    }

    private static string PublicationChange(TicketChange change, bool hiding)
    {
        var ticket = $"Ticket #{change.Ticket.Number}";

        if (change.NoChange)
            return $"{ticket}: no fields supplied; nothing changed.";

        if (hiding)
            return $"{ticket} is hidden; its record is retained. "
                + (change.LinkedCopyRemains
                    ? "A linked or unconfirmed post may remain. Sylvan can only remove messages it authored."
                    : "No recorded channel copy remains.");

        if (change.PublicationUncertain)
            return $"{ticket} was saved, but publication/update could not be confirmed. "
                + "Do not repeat the action. Check the saved ticket and the tickets channel. "
                + "An uncertain new publication will not be reposted automatically.";

        if (change.Ticket.Hidden)
            return $"Saved {ticket}. It remains hidden.";

        var link = Link(
            change.Ticket.GuildId,
            change.ConfirmedPublication?.ChannelId,
            change.ConfirmedPublication?.MessageId);

        var publication = link is null ? string.Empty : $" [Jump to ticket message]({link})";

        if (!change.ReferenceSaved)
            return $"{ticket} was saved and its post was confirmed.{publication}\n"
                + "Saving the message reference could not be confirmed. Do not create it again.";

        return $"Saved {ticket}.{publication}";
    }
}