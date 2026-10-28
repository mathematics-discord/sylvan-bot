namespace SylvanBot.Application.Features.Tickets;

using System.Collections.Immutable;

public interface IDiscordTicket
{
    Task CheckChannelAsync(TicketActor actor, ulong channelId, bool publishing, CancellationToken cancellationToken);

    Task<Optional<TicketMessage>> FindMessageAsync(TicketActor actor, Ticket ticket, CancellationToken cancellationToken);

    Task<TicketMessage> PublishAsync(Ticket ticket, ulong channelId, CancellationToken cancellationToken);

    // False means this link has no editable message owned by the current bot.
    Task<bool> UpdateAsync(Ticket ticket, TicketMessage publication, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(ulong guildId, TicketMessage publication, CancellationToken cancellationToken);

    Task<(ulong? LastAuditId, ImmutableArray<TicketObservation> Observations)> ReadAuditAsync(
        ulong guildId, DateTimeOffset boundary, CancellationToken cancellationToken);

    Task CheckCaptureAsync(ulong guildId, CancellationToken cancellationToken);

    Task CheckPublicationAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken);

    Task<TicketActor> GetActorAsync(ulong guildId, ulong userId, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetTimeoutAsync(ulong guildId, ulong userId, CancellationToken cancellationToken);

    Task SetTimeoutAsync(Ticket ticket, DateTimeOffset? expiry, Guid operationId, CancellationToken cancellationToken);

    Task<bool> IsBannedAsync(ulong guildId, ulong userId, CancellationToken cancellationToken);

    Task RevertVoiceMuteAsync(Ticket ticket, CancellationToken cancellationToken);

    Task UnbanAsync(Ticket ticket, Guid operationId, CancellationToken cancellationToken);
}
