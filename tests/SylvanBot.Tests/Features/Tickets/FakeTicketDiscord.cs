namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Features.Tickets;

internal sealed class FakeTicketDiscord : ITicketDiscord
{
    private readonly HashSet<TicketMessage> _ownedMessages = [];
    internal bool ChannelMissing { get; set; }
    internal bool DenyRead { get; set; }
    internal bool DenyPublication { get; set; }
    internal bool PublicationFails { get; set; }
    internal bool RemovalFails { get; set; }
    internal int Publications { get; private set; }
    internal int Updates { get; private set; }
    internal List<string?> PublishedComments { get; } = [];
    internal Func<Ticket, Task>? BeforePublish { get; set; }
    internal Func<Ticket, Task>? BeforeUpdate { get; set; }

    public Task CheckChannelAsync(
        TicketActor actor, ulong channelId, bool publishing, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ChannelMissing)
            throw new InvalidOperationException();

        if (DenyRead || (publishing && DenyPublication))
            throw new UnauthorizedAccessException();

        return Task.CompletedTask;
    }

    public Task<Option<TicketMessage>> FindMessageAsync(
        TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
    {
        var publication = ticket.ChannelId is not null && ticket.MessageId is not null
            ? new TicketMessage(ticket.ChannelId.Value, ticket.MessageId.Value)
            : null;

        Option<TicketMessage> result = publication is not null && _ownedMessages.Contains(publication) && !DenyRead
            ? new Some<TicketMessage>(publication)
            : new None();

        return Task.FromResult(result);
    }

    public async Task<TicketMessage> PublishAsync(Ticket ticket, ulong channelId, CancellationToken cancellationToken)
    {
        Publications++;

        if (BeforePublish is not null)
            await BeforePublish(ticket);

        if (PublicationFails)
            throw new InvalidOperationException();

        cancellationToken.ThrowIfCancellationRequested();
        var publication = new TicketMessage(channelId, (ulong)(400 + Publications));
        _ownedMessages.Add(publication);
        PublishedComments.Add(ticket.Comment);
        return publication;
    }

    public async Task<bool> UpdateAsync(Ticket ticket, TicketMessage publication, CancellationToken cancellationToken)
    {
        if (!_ownedMessages.Contains(publication))
            return false;

        Updates++;

        if (BeforeUpdate is not null)
            await BeforeUpdate(ticket);

        cancellationToken.ThrowIfCancellationRequested();

        if (PublicationFails)
            throw new InvalidOperationException();

        PublishedComments.Add(ticket.Comment);
        return true;
    }

    public Task<bool> RemoveAsync(ulong guildId, TicketMessage publication, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!RemovalFails && _ownedMessages.Remove(publication));
    }
}