namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed partial class TicketCoordinator
{
    public async Task<string> RepairAsync(TicketActor actor, long number, string? messageLink, bool resend, CancellationToken token)
    {
        if (!actor.IsAdministrator || actor.GuildId == 0 || actor.UserId == 0)
            throw new BotCommandException("Administrator permission is required to repair publications.");

        if (resend && messageLink is not null)
            throw new BotCommandException("Choose either an existing message or resend, not both.");

        long ticketId;

        using (await LockTicketAsync(actor.GuildId, number, token))
        {
            await using var db = await contexts.CreateDbContextAsync(token);
            var ticket = await db.Tickets
                .SingleAsync(value => value.GuildId == actor.GuildId && value.Number == number, token);

            if (messageLink is null && !resend)
            {
                IEnumerable<string> references = ticket.Publications
                    .OrderByDescending(reference => reference.MessageId)
                    .Select(reference => $"[Message]({TicketPresentation.Link(actor.GuildId, reference.ChannelId, reference.MessageId)}): "
                        + (reference.Removed ? "removed" : reference.Current ? "current" : "superseded"));

                return ( $"Ticket #{number}: {(ticket.Hidden ? "hidden" : "visible")}; "
                    + $"publication {(ticket.SendUncertain ? "uncertain" : "not uncertain")}.\n"
                    + string.Join("\n", references.Take(10))
                    + "\nCheck the channel before using `message` to adopt a copy or `resend: true` to retry.");
            }

            if (ticket.Hidden)
                throw new BotCommandException("Hidden tickets cannot be adopted or resent. Restore the record first if publication is intended.");

            if (messageLink is not null)
            {
                var reference = TicketReference.ParseMessageLink(messageLink, actor.GuildId);
                var guild = await client.Rest.GetGuildAsync(actor.GuildId, TicketBoundary.Options(token));
                var channel = await guild.GetTextChannelAsync(reference.ChannelId, options: TicketBoundary.Options(token))
                    ?? throw new BotCommandException("The message channel is unavailable in this server.");
                var message = await channel.GetMessageAsync(reference.MessageId, options: TicketBoundary.Options(token));
                var expectedAuthor = $"Ticket #{ticket.Number}";
                if (message is null || message.Author.Id != client.CurrentUser.Id
                    || !message.Embeds.Any(embed => string.Equals(embed.Author?.Name, expectedAuthor, StringComparison.Ordinal)))
                    throw new BotCommandException("That message is not a Sylvan publication of this ticket.");

                var belongsElsewhere = await db.Tickets
                    .AnyAsync(value => value.Id != ticket.Id && value.Publications.Any(publication =>
                        publication.ChannelId == reference.ChannelId && publication.MessageId == reference.MessageId), token);
                if (belongsElsewhere)
                    throw new BotCommandException("That message is already recorded against another ticket.");

                foreach (var publication in ticket.Publications)
                    publication.Current = false;

                var adopted = ticket.Publications
                    .SingleOrDefault(publication => publication.ChannelId == reference.ChannelId && publication.MessageId == reference.MessageId);
                if (adopted is null)
                {
                    adopted = new TicketPublication { ChannelId = reference.ChannelId, MessageId = reference.MessageId };
                    ticket.Publications.Add(adopted);
                }

                adopted.Format = message.Flags?.HasFlag(MessageFlags.ComponentsV2) == true
                    ? TicketPublicationFormat.ComponentsV2 : TicketPublicationFormat.Embed;
                adopted.Current = true;
                adopted.Removed = false;
                adopted.Revision = -1;
            }

            foreach (var publication in ticket.Publications)
                if (!publication.Removed)
                    publication.Revision = -1;

            ticket.SendUncertain = false;
            await db.SaveChangesAsync(token);
            ticketId = ticket.Id;
        }

        var outcome = await publisher.SynchronizeAsync(actor.GuildId, ticketId, token, allowDisabled: true);

        if (outcome.Changed)
            reviewSummary.MarkDirty(actor.GuildId);

        return ( $"Publication repair saved for ticket #{number}." + "\n" + outcome.Message);
    }
}
