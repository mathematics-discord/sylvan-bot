using Discord.Rest;

namespace RatBot.Features.TicketCollection;

public static class DiscordTicketAuditMapper
{
    public static ErrorOr<Option<TicketAuditObservation>> Map(
        ulong guildId,
        IAuditLogEntry entry,
        DateTimeOffset capturedAtUtc)
    {
        ErrorOr<Option<TicketAuditAction>> actionResult = MapAction(entry);

        if (actionResult.IsError)
            return actionResult.Errors;

        if (actionResult.Value.Value is not Some<TicketAuditAction> capturedAction)
            return (Option<TicketAuditObservation>)new None();

        ulong? actorId = entry.User?.Id;
        ulong? targetId = entry.Data switch
        {
            KickAuditLogData kick => kick.Target?.Id,
            BanAuditLogData ban => ban.Target?.Id,
            UnbanAuditLogData unban => unban.Target?.Id,
            MemberUpdateAuditLogData update => update.Target?.Id,
            MemberRoleAuditLogData roles => roles.Target?.Id,
            _ => null,
        };

        bool hasInvalidId = guildId == 0
            || entry.Id == 0
            || actorId == 0
            || targetId == 0;

        if (hasInvalidId)
            return Error.Failure("TicketAudit.InvalidId", "A relevant audit entry contains a zero ID.");

        TicketAuditObservation observation = new TicketAuditObservation
        {
            GuildId = guildId,
            AuditId = entry.Id,
            OccurredAtUtc = entry.CreatedAt,
            CapturedAtUtc = capturedAtUtc,
            ActorId = actorId,
            TargetId = targetId,
            Reason = entry.Reason,
            Action = capturedAction.Value,
        };

        return (Option<TicketAuditObservation>)new Some<TicketAuditObservation>(observation);
    }

    private static ErrorOr<Option<TicketAuditAction>> MapAction(IAuditLogEntry entry)
    {
        switch (entry.Action, entry.Data)
        {
            case (ActionType.Kick, KickAuditLogData):
                return (Option<TicketAuditAction>)new Some<TicketAuditAction>(new MemberKicked());

            case (ActionType.Ban, BanAuditLogData):
                return (Option<TicketAuditAction>)new Some<TicketAuditAction>(new MemberBanned());

            case (ActionType.Unban, UnbanAuditLogData):
                return (Option<TicketAuditAction>)new Some<TicketAuditAction>(new MemberUnbanned());

            case (ActionType.MemberUpdated, MemberUpdateAuditLogData update):
                return MapMemberUpdate(update);

            case (ActionType.MemberRoleUpdated, MemberRoleAuditLogData roles):
                return MapRoles(roles);

            default:
                bool isRelevantAction = entry.Action is ActionType.Kick
                    or ActionType.Ban
                    or ActionType.Unban
                    or ActionType.MemberUpdated
                    or ActionType.MemberRoleUpdated;

                if (isRelevantAction)
                {
                    return Error.Failure(
                        "TicketAudit.InvalidData",
                        "The relevant audit action is missing its expected Discord.NET data.");
                }

                return (Option<TicketAuditAction>)new None();
        }
    }

    private static ErrorOr<Option<TicketAuditAction>> MapMemberUpdate(MemberUpdateAuditLogData update)
    {
        MemberInfo before = update.Before;
        MemberInfo after = update.After;
        bool hasIncompleteMute = before.Mute.HasValue != after.Mute.HasValue;
        bool hasIncompleteDeafen = before.Deaf.HasValue != after.Deaf.HasValue;
        bool hasIncompleteVoiceChange = hasIncompleteMute || hasIncompleteDeafen;

        if (hasIncompleteVoiceChange)
        {
            return Error.Failure(
                "TicketAudit.IncompleteVoiceChange",
                "A voice mute or deafen change is missing its before or after value.");
        }

        TicketBooleanChange? mute = (before.Mute, after.Mute) switch
        {
            (bool muteBefore, bool muteAfter) => new TicketBooleanChange(muteBefore, muteAfter),
            _ => null,
        };

        TicketBooleanChange? deafen = (before.Deaf, after.Deaf) switch
        {
            (bool deafenBefore, bool deafenAfter) => new TicketBooleanChange(deafenBefore, deafenAfter),
            _ => null,
        };

        TicketTimeoutChange? timeout = null;
        // Discord.NET 3.20.1 exposes nullable timestamps, without a change-presence flag.
        // Either timestamp identifies a timeout change, including clearing an existing timeout.
        // Null on both sides is indistinguishable from an absent change in this API.
        bool hasTimeoutChange = before.TimedOutUntil.HasValue || after.TimedOutUntil.HasValue;

        if (hasTimeoutChange)
            timeout = new TicketTimeoutChange(before.TimedOutUntil, after.TimedOutUntil);

        bool hasRelevantChange = mute is not null || deafen is not null || timeout is not null;

        if (!hasRelevantChange)
            return (Option<TicketAuditAction>)new None();

        MemberUpdated action = new MemberUpdated(mute, deafen, timeout);
        return (Option<TicketAuditAction>)new Some<TicketAuditAction>(action);
    }

    private static ErrorOr<Option<TicketAuditAction>> MapRoles(MemberRoleAuditLogData data)
    {
        if (data.Roles is null)
            return Error.Failure("TicketAudit.MissingRoles", "The member role update is missing its role changes.");

        ImmutableArray<ulong>.Builder added = ImmutableArray.CreateBuilder<ulong>();
        ImmutableArray<ulong>.Builder removed = ImmutableArray.CreateBuilder<ulong>();

        foreach (MemberRoleEditInfo role in data.Roles)
        {
            bool hasInvalidDirection = role.Added == role.Removed;
            bool hasInvalidRoleChange = role.RoleId == 0 || hasInvalidDirection;

            if (hasInvalidRoleChange)
            {
                return Error.Failure(
                    "TicketAudit.InvalidRoleChange",
                    "A member role change requires a nonzero role ID and exactly one change direction.");
            }

            if (role.Added)
                added.Add(role.RoleId);
            else
                removed.Add(role.RoleId);
        }

        bool hasRoleChanges = added.Count > 0 || removed.Count > 0;

        if (!hasRoleChanges)
            return (Option<TicketAuditAction>)new None();

        MemberRolesChanged action = new MemberRolesChanged(added.ToImmutable(), removed.ToImmutable());
        return (Option<TicketAuditAction>)new Some<TicketAuditAction>(action);
    }
}