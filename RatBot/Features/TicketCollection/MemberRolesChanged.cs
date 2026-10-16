namespace RatBot.Features.TicketCollection;

public sealed record MemberRolesChanged(ImmutableArray<ulong> AddedRoleIds, ImmutableArray<ulong> RemovedRoleIds);