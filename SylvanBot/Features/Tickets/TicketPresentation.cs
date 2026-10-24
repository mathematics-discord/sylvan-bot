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

    internal static MessageComponent Buttons(Ticket ticket)
    {
        var builder = new ComponentBuilder();

        if (ticket.Action == TicketAction.Note)
        {
            builder
                .WithButton("Edit", $"ticket:edit:{ticket.Number}", ButtonStyle.Primary);

            if (!ticket.Hidden)
            {
                builder
                    .WithButton("Hide", $"ticket:hide:{ticket.Number}", ButtonStyle.Danger);
            }
        }

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

        IEnumerable<ActionRowComponent> rows = Buttons(ticket).Components
            .OfType<ActionRowComponent>();

        foreach (var row in rows)
            container.WithActionRow(row.ToBuilder());

        return new ComponentBuilderV2(container).Build();
    }

    internal static Embed Describe(Ticket ticket)
    {
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

        if (ticket.Action == TicketAction.Note)
            return embed.Build();

        if (ticket.Status is not null)
        {
            var status = ticket.Status == TicketStatus.ExpirationFailed
                ? "Expiration failed"
                : ticket.Status.Value.ToString();

            embed.AddField("Status", status, inline: true);
        }

        if (ticket.DurationSeconds is not null)
            embed.AddField("Duration", $"{ticket.DurationSeconds} seconds", inline: true);

        if (ticket.RoleId is not null)
            embed.AddField("Role", ticket.RoleId.Value.ToString(CultureInfo.InvariantCulture), inline: true);

        return embed.Build();
    }

    internal static string Change(TicketChange change, bool hiding = false)
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
                   + "Check the channel before retrying. An uncertain new publication will not be reposted automatically.";

        if (change.Ticket.Hidden)
            return $"Saved {ticket}. It remains hidden.";

        var link = Link(
            change.Ticket.GuildId,
            change.ConfirmedPublication?.ChannelId,
            change.ConfirmedPublication?.MessageId);
        var publication = link is null ? string.Empty : $" [Jump to ticket message]({link})";

        if (!change.ReferenceSaved)
            return $"{ticket} was saved and its post was confirmed.{publication}\n"
                   + "Saving the message reference failed. Do not create it again.";

        return $"Saved {ticket}.{publication}";
    }
}