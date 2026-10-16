namespace RatBot.Features.TicketCollection;

public static class TicketAuditMapping
{
    public static CapturedAuditEntry ToEntity(TicketAuditObservation observation)
    {
        CapturedAuditEntry entry = new CapturedAuditEntry
        {
            GuildId = observation.GuildId,
            AuditId = observation.AuditId,
            OccurredAtUtc = observation.OccurredAtUtc,
            CapturedAtUtc = observation.CapturedAtUtc,
            ActorId = observation.ActorId,
            TargetId = observation.TargetId,
            Reason = observation.Reason,
            Action = observation.Action.Value switch
            {
                MemberKicked => CapturedAuditAction.MemberKicked,
                MemberBanned => CapturedAuditAction.MemberBanned,
                MemberUnbanned => CapturedAuditAction.MemberUnbanned,
                MemberUpdated => CapturedAuditAction.MemberUpdated,
                MemberRolesChanged => CapturedAuditAction.MemberRolesChanged,
                _ => throw new InvalidOperationException("Unknown ticket audit action."),
            },
        };

        switch (observation.Action.Value)
        {
            case MemberRolesChanged roles:
                entry.AddedRoleIds = roles.AddedRoleIds
                    .ToArray();

                entry.RemovedRoleIds = roles.RemovedRoleIds
                    .ToArray();

                break;

            case MemberUpdated updated:
                entry.VoiceMuteBefore = updated.VoiceMute?.Before;
                entry.VoiceMuteAfter = updated.VoiceMute?.After;
                entry.VoiceDeafenBefore = updated.VoiceDeafen?.Before;
                entry.VoiceDeafenAfter = updated.VoiceDeafen?.After;
                entry.HasTimeoutChange = updated.Timeout is not null;
                entry.TimeoutBeforeUtc = updated.Timeout?.BeforeUtc;
                entry.TimeoutAfterUtc = updated.Timeout?.AfterUtc;
                break;
        }

        return entry;
    }

    public static TicketAuditObservation ToObservation(CapturedAuditEntry entry)
    {
        TicketAuditAction action = entry.Action switch
        {
            CapturedAuditAction.MemberKicked => new MemberKicked(),
            CapturedAuditAction.MemberBanned => new MemberBanned(),
            CapturedAuditAction.MemberUnbanned => new MemberUnbanned(),
            CapturedAuditAction.MemberUpdated => ReadMemberUpdate(entry),
            CapturedAuditAction.MemberRolesChanged => new MemberRolesChanged(
                ImmutableArray.CreateRange(entry.AddedRoleIds),
                ImmutableArray.CreateRange(entry.RemovedRoleIds)),
            _ => throw new InvalidOperationException($"Unknown captured audit action: {entry.Action}."),
        };

        return new TicketAuditObservation
        {
            GuildId = entry.GuildId,
            AuditId = entry.AuditId,
            OccurredAtUtc = entry.OccurredAtUtc,
            CapturedAtUtc = entry.CapturedAtUtc,
            ActorId = entry.ActorId,
            TargetId = entry.TargetId,
            Reason = entry.Reason,
            Action = action,
        };
    }

    private static MemberUpdated ReadMemberUpdate(CapturedAuditEntry entry)
    {
        TicketBooleanChange? voiceMute = null;
        TicketBooleanChange? voiceDeafen = null;
        TicketTimeoutChange? timeout = null;

        if (entry.VoiceMuteBefore is bool muteBefore)
        {
            bool muteAfter = entry.VoiceMuteAfter
                ?? throw new InvalidOperationException("Captured voice mute change is missing its after value.");

            voiceMute = new TicketBooleanChange(muteBefore, muteAfter);
        }

        if (entry.VoiceDeafenBefore is bool deafenBefore)
        {
            bool deafenAfter = entry.VoiceDeafenAfter
                ?? throw new InvalidOperationException("Captured voice deafen change is missing its after value.");

            voiceDeafen = new TicketBooleanChange(deafenBefore, deafenAfter);
        }

        if (entry.HasTimeoutChange)
            timeout = new TicketTimeoutChange(entry.TimeoutBeforeUtc, entry.TimeoutAfterUtc);

        return new MemberUpdated(voiceMute, voiceDeafen, timeout);
    }
}