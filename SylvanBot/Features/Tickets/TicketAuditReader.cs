namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

using System.Globalization;
using System.Text.Json;

public static class TicketAuditReader
{
    public static ImmutableArray<TicketObservation> Read(
        ulong guildId, JsonElement entry, IReadOnlySet<ulong> botIds, ulong ownBotId)
    {
        var auditId = RequiredId(entry, "id");
        var action = entry.GetProperty("action_type").GetInt32();

        if (action is not (20 or 22 or 23 or 24 or 25))
            return [];

        var subjectId = RequiredId(entry, "target_id");
        var moderatorId = RequiredId(entry, "user_id");
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(1420070400000L + checked((long)(auditId >> 22)));
        var reason = entry.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() ?? string.Empty : string.Empty;
        DotNext.Optional<Guid> operationId = DotNext.Optional<Guid>.None;
        long? restoredTicket = null;
        var ownBot = moderatorId == ownBotId;

        if (ownBot)
        {
            var marker = reason.LastIndexOf("; operation ", StringComparison.Ordinal);
            if (marker >= 0 && Guid.TryParseExact(reason[(marker + 12)..], "D", out var parsed))
                operationId = parsed;

            const string restoration = "Role persistence; ticket ";
            if (reason.StartsWith(restoration, StringComparison.Ordinal)
                && long.TryParse(reason[restoration.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number > 0)
                restoredTicket = number;
        }

        var observation = new TicketObservation(guildId, auditId, TicketAction.Ban, subjectId, moderatorId,
            ownBot || botIds.Contains(moderatorId), reason, createdAt)
        {
            OperationId = operationId,
            RestoredTicketNumber = restoredTicket,
        };

        if (action is 20 or 22 or 23)
            return [observation with { Action = action == 20 ? TicketAction.Kick : TicketAction.Ban, Removed = action == 23 }];

        if (!entry.TryGetProperty("changes", out var changes))
            return [];

        List<TicketObservation> observations = [];

        foreach (var change in changes.EnumerateArray())
        {
            var key = change.GetProperty("key").GetString();
            if (action == 24 && key == "communication_disabled_until")
            {
                DateTimeOffset? previous = ReadTimestamp(change, "old_value");
                DateTimeOffset? expiry = ReadTimestamp(change, "new_value");
                if (previous != expiry)
                    observations.Add(observation with
                    {
                        Action = TicketAction.Timeout,
                        Removed = expiry is null || expiry <= createdAt,
                        Timeout = expiry is { } newExpiry ? new TicketTimeout.Until(newExpiry) : new TicketTimeout.None(),
                    });
            }
            else if (action == 24 && key is "mute" or "deaf")
            {
                var before = change.TryGetProperty("old_value", out var oldValue) && oldValue.ValueKind == JsonValueKind.True;
                var after = change.TryGetProperty("new_value", out var newValue) && newValue.ValueKind == JsonValueKind.True;
                if (before != after)
                    observations.Add(observation with { Action = key == "mute" ? TicketAction.VoiceMute : TicketAction.VoiceDeafen, Removed = !after });
            }
            else if (action == 25 && key is "$add" or "$remove")
            {
                if (change.TryGetProperty("new_value", out var roles))
                    foreach (var role in roles.EnumerateArray())
                        observations.Add(observation with { Action = TicketAction.Role, RoleId = RequiredId(role, "id"), Removed = key == "$remove" });
            }
        }

        return observations.ToImmutableArray();
    }

    public static ulong RequiredId(JsonElement entry, string field)
    {
        if (!entry.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String
            || !ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new InvalidDataException($"Audit entry has a missing or invalid {field}.");

        return id;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement change, string field)
    {
        if (!change.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        return value.GetDateTimeOffset();
    }
}
