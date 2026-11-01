using System.ComponentModel.DataAnnotations.Schema;
using static SylvanBot.Application.Features.Tickets.TicketOperationCommand;

namespace SylvanBot.Application.Features.Tickets;

public sealed class TicketOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset? ExpiryUtc { get; set; }

    public ulong? RequestedBy { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool Attempted { get; set; }

    [NotMapped]
    public TicketOperationCommand Command => Kind switch
    {
        "SetTimeoutExpiry" when ExpiryUtc is { } expiry => new ChangeTimeout(expiry),
        "RemoveBan" => new ExpireBan(),
        _ => throw new InvalidOperationException("Invalid pending moderation command."),
    };
}