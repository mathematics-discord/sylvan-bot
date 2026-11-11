using static System.StringComparison;

namespace SylvanBot.Features.Tickets;

using DotNext;
using SylvanBot.Application.Features.Tickets;
using System.Globalization;
using System.Text.Json;

public static class TicketAuditReader
{
    public static ImmutableArray<TicketObservation> Read(
        ulong guildId,
        JsonElement entry,
        IReadOnlySet<ulong> botIds,
        ulong ownBotId)
    {
        var auditId = RequiredId(entry, "id");
        var action = entry.GetProperty("action_type").GetInt32();
        var isSupportedAction = action is 20 or 22 or 23 or 24 or 25;

        if (!isSupportedAction)
            return [];

        var observation = ReadObservation(guildId, auditId, entry, botIds, ownBotId);
        var isKickOrBanChange = action is 20 or 22 or 23;

        if (isKickOrBanChange)
            return [observation with { Action = action == 20 ? TicketAction.Kick : TicketAction.Ban, Removed = action == 23 }];

        if (!entry.TryGetProperty("changes", out var changes))
            return [];

        var observations = ImmutableArray.CreateBuilder<TicketObservation>();

        foreach (var change in changes.EnumerateArray())
        {
            var key = change.GetProperty("key").GetString();

            switch (action)
            {
                case 24 when string.Equals(key, "communication_disabled_until", Ordinal):
                    AddTimeoutChange(observation, change, observations);
                    break;
                case 24 when key is "mute" or "deaf":
                    AddVoiceChange(observation, key, change, observations);
                    break;
                case 25 when key is "$add" or "$remove":
                    AddRoleChanges(observation, key, change, observations);
                    break;
            }
        }

        return observations.ToImmutable();
    }

    private static TicketObservation ReadObservation(ulong guildId, ulong auditId, JsonElement entry, IReadOnlySet<ulong> botIds, ulong ownBotId)
    {
        var subjectId = RequiredId(entry, "target_id");
        var moderatorId = RequiredId(entry, "user_id");
        var reason = entry.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() ?? string.Empty : string.Empty;
        var ownBot = moderatorId == ownBotId;
        var moderatorIsBot = ownBot || botIds.Contains(moderatorId);

        var observation = new TicketObservation
        {
            GuildId = guildId,
            AuditId = auditId,
            Action = TicketAction.Ban,
            SubjectUserId = subjectId,
            ModeratorUserId = moderatorId,
            ModeratorIsBot = moderatorIsBot,
            Reason = reason,
            CreatedAtUtc = SnowflakeUtils.FromSnowflake(auditId),
        };

        return ownBot ? ReadSylvanDetails(observation) : observation;
    }

    private static TicketObservation ReadSylvanDetails(TicketObservation observation)
    {
        var reason = observation.Reason;
        const string operationMarker = "; operation ";
        var marker = reason.LastIndexOf(operationMarker, Ordinal);
        var hasOperationMarker = marker >= 0;
        var parsedOperation = Guid.Empty;

        var hasOperationId = hasOperationMarker
            && Guid.TryParseExact(reason[(marker + operationMarker.Length)..], "D", out parsedOperation);

        Optional<Guid> operationId = Optional<Guid>.None;

        if (hasOperationId)
            operationId = parsedOperation;

        const string restoration = "Role persistence; ticket ";
        var isRoleRestoration = reason.StartsWith(restoration, Ordinal);
        long restoredNumber = 0;

        var hasRestoredNumber = isRoleRestoration && long.TryParse(reason[restoration.Length..], out restoredNumber);
        var hasValidRestoredTicket = hasRestoredNumber && restoredNumber > 0;
        var isPatternMatch = reason.StartsWith("Automatic action: message matches pattern ", Ordinal);
        var isAutomodBan = reason.StartsWith("Automod ban rule ", Ordinal);

        return observation with
        {
            OperationId = operationId,
            RestoredTicketNumber = hasValidRestoredTicket ? restoredNumber : null,
            RequiresApproval = isPatternMatch || isAutomodBan,
        };
    }

    private static void AddTimeoutChange(TicketObservation observation, JsonElement change, ImmutableArray<TicketObservation>.Builder observations)
    {
        DateTimeOffset? previous = ReadTimestamp(change, "old_value");
        DateTimeOffset? expiry = ReadTimestamp(change, "new_value");

        if (previous == expiry)
            return;

        observations.Add(
            observation with
            {
                Action = TicketAction.Timeout,
                Removed = expiry is null || expiry <= observation.CreatedAtUtc,
                Timeout = expiry is { } newExpiry ? newExpiry : Optional<DateTimeOffset>.None,
            });
    }

    private static void AddVoiceChange(
        TicketObservation observation,
        string key,
        JsonElement change,
        ImmutableArray<TicketObservation>.Builder observations)
    {
        var hasOldValue = change.TryGetProperty("old_value", out var oldValue);
        var hasNewValue = change.TryGetProperty("new_value", out var newValue);
        var wasRestricted = hasOldValue && oldValue.ValueKind == JsonValueKind.True;
        var isRestricted = hasNewValue && newValue.ValueKind == JsonValueKind.True;

        if (wasRestricted == isRestricted)
            return;

        observations.Add(
            observation with
            {
                Action = string.Equals(key, "mute", OrdinalIgnoreCase) ? TicketAction.VoiceMute : TicketAction.VoiceDeafen,
                Removed = !isRestricted,
            });
    }

    private static void AddRoleChanges(
        TicketObservation observation,
        string key,
        JsonElement change,
        ImmutableArray<TicketObservation>.Builder observations)
    {
        if (!change.TryGetProperty("new_value", out var roles))
            return;

        IEnumerable<TicketObservation> roleChanges = roles
            .EnumerateArray()
            .Select(role => observation with
            {
                Action = TicketAction.Role, RoleId = RequiredId(role, "id"), Removed = string.Equals(key, "$remove", OrdinalIgnoreCase),
            });

        observations.AddRange(roleChanges);
    }

    public static ulong RequiredId(JsonElement entry, string field)
    {
        var hasField = entry.TryGetProperty(field, out var value);
        var isString = hasField && value.ValueKind == JsonValueKind.String;
        ulong id = 0;

        var hasParsedId = isString && ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out id);
        var hasValidId = hasParsedId && id != 0;

        return !hasValidId
            ? throw new InvalidDataException($"Audit entry has a missing or invalid {field}.")
            : id;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement change, string field)
    {
        var hasField = change.TryGetProperty(field, out var value);
        var hasTimestamp = hasField && value.ValueKind != JsonValueKind.Null;

        if (!hasTimestamp)
            return null;

        return value.GetDateTimeOffset();
    }
}