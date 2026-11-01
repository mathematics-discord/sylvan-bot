namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketActor(
    ulong GuildId,
    ulong UserId,
    bool IsAdministrator,
    bool CanTimeoutMembers,
    bool CanBanMembers = false,
    bool CanKickMembers = false,
    bool CanMuteMembers = false,
    bool CanDeafenMembers = false,
    bool CanManageRoles = false,
    bool CanViewAuditLog = false);