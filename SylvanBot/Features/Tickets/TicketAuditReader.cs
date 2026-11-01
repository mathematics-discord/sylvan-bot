namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

using System.Globalization;
using System.Text.Json;

public static class TicketAuditReader
{
    public static DotNext.Optional<TicketObservation> Read(
        ulong guildId, JsonElement entry, IReadOnlySet<ulong> botIds, ulong ownBotId)
    {
        var auditId = RequiredId(entry, "id");
        var action = entry.GetProperty("action_type").GetInt32();
        if (action is not (22 or 23 or 24))
            return DotNext.Optional<TicketObservation>.None;

        DateTimeOffset? previous = null;
        DateTimeOffset? expiry = null;
        if (action == 24)
        {
            var found = false;
            if (entry.TryGetProperty("changes", out var changes))
            {
                foreach (var change in changes.EnumerateArray())
                {
                    if (!string.Equals(change.GetProperty("key").GetString(), "communication_disabled_until", StringComparison.Ordinal))
                        continue;

                    found = true;
                    previous = ReadTimestamp(change, "old_value");
                    expiry = ReadTimestamp(change, "new_value");
                    break;
                }
            }

            if (!found || previous == expiry)
                return DotNext.Optional<TicketObservation>.None;
        }

        var subjectId = RequiredId(entry, "target_id");
        var moderatorId = RequiredId(entry, "user_id");
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(1420070400000L + checked((long)(auditId >> 22)));
        var reason = entry.TryGetProperty("reason", out var reasonValue) ? reasonValue.GetString() ?? string.Empty : string.Empty;
        DotNext.Optional<Guid> operationId = DotNext.Optional<Guid>.None;
        var ownBot = moderatorId == ownBotId;
        if (ownBot)
        {
            var marker = reason.LastIndexOf("; operation ", StringComparison.Ordinal);
            if (marker >= 0 && Guid.TryParseExact(reason[(marker + 12)..], "D", out var parsed))
                operationId = parsed;
        }

        return new TicketObservation(
            guildId, auditId, action == 24 ? TicketAction.Timeout : TicketAction.Ban, subjectId, moderatorId, ownBot || botIds.Contains(moderatorId), reason, createdAt)
        {
            Removed = action == 23 || (action == 24 && (expiry is null || expiry <= createdAt)),
            PreviousTimeout = previous is { } oldExpiry ? new TicketTimeout.Until(oldExpiry) : new TicketTimeout.None(),
            Timeout = expiry is { } newExpiry ? new TicketTimeout.Until(newExpiry) : new TicketTimeout.None(),
            OperationId = operationId,
        };
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
