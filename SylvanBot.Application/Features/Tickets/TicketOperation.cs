
namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public TicketOperationKind Kind { get; set; }

    public DateTimeOffset? ExpiryUtc { get; set; }

    public DateTimeOffset? NativeExpiryUtc { get; set; }

    public ulong? RequestedBy { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool Attempted { get; set; }

}

public enum TicketOperationKind
{
    SetTimeoutExpiry,
    RemoveBan,
    RemoveRole,
}
