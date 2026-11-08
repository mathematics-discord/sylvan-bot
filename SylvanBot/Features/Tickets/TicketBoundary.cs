namespace SylvanBot.Features.Tickets;

using System.Globalization;
using SylvanBot.Application.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SylvanBot.Application.Features.Tickets;

public sealed class TicketBoundary(DiscordSocketClient socketClient, HttpClient httpClient,
    Microsoft.Extensions.Options.IOptions<SylvanBot.Gateway.DiscordOptions> discordOptions, ILogger logger) : IDisposable
{
    private readonly SemaphoreSlim _auditReadLock = new SemaphoreSlim(1, 1);

    public async Task<TicketAuditScan> ReadAuditAsync(
        ulong guildId, DateTimeOffset boundary, CancellationToken cancellationToken)
    {
        await _auditReadLock.WaitAsync(cancellationToken);
        try
        {
            return await ReadAuditPagesAsync(guildId, boundary, cancellationToken);
        }
        finally
        {
            _auditReadLock.Release();
        }
    }

    private async Task<TicketAuditScan> ReadAuditPagesAsync(
        ulong guildId, DateTimeOffset boundary, CancellationToken cancellationToken)
    {
        List<TicketObservation> observations = [];
        DotNext.Optional<ulong> lastAuditId = DotNext.Optional<ulong>.None;
        var cursor = string.Empty;

        while (true)
        {
            using var page = await ReadAuditPageAsync(guildId, cursor, cancellationToken);
            HashSet<ulong> botIds = ReadBotIds(page.RootElement);

            var entries = page.RootElement.GetProperty("audit_log_entries");
            if (entries.GetArrayLength() == 0)
                break;

            var oldestId = ulong.MaxValue;
            var reachedBoundary = false;
            foreach (var entry in entries.EnumerateArray())
            {
                try
                {
                    var auditId = TicketAuditReader.RequiredId(entry, "id");
                    oldestId = Math.Min(oldestId, auditId);
                    var createdAt = SnowflakeUtils.FromSnowflake(auditId);
                    if (createdAt < boundary)
                    {
                        reachedBoundary = true;
                        continue;
                    }

                    lastAuditId = lastAuditId.TryGet(out var previous) ? Math.Max(previous, auditId) : auditId;
                    observations.AddRange(TicketAuditReader.Read(guildId, entry, botIds, socketClient.CurrentUser.Id));
                }
                catch (Exception exception) when (exception is InvalidDataException or FormatException or JsonException)
                {
                    var audit = entry.TryGetProperty("id", out var id) ? id.ToString() : "missing";
                    var action = entry.TryGetProperty("action_type", out var type) ? type.ToString() : "missing";
                    logger.Error("Rejected audit entry {AuditId} in guild {GuildId}, action {Action}: {Failure}",
                        audit, guildId, action, exception.Message);
                }
            }

            if (entries.GetArrayLength() < 100 || reachedBoundary)
                break;

            if (oldestId == ulong.MaxValue)
                throw new InvalidDataException("No valid audit IDs were supplied for pagination.");

            var nextCursor = oldestId.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(nextCursor, cursor, StringComparison.Ordinal))
                throw new InvalidDataException("Discord audit pagination did not advance.");

            cursor = nextCursor;
        }

        ImmutableArray<TicketObservation> ordered = observations
            .OrderBy(observation => observation.AuditId)
            .ToImmutableArray();
        return new TicketAuditScan(lastAuditId, ordered);
    }

    private static HashSet<ulong> ReadBotIds(JsonElement root)
    {
        HashSet<ulong> botIds = [];
        if (root.TryGetProperty("users", out var users))
        {
            foreach (var user in users.EnumerateArray())
            {
                if (user.TryGetProperty("bot", out var bot) && bot.GetBoolean())
                    botIds.Add(TicketAuditReader.RequiredId(user, "id"));
            }
        }

        return botIds;
    }

    private async Task<JsonDocument> ReadAuditPageAsync(ulong guildId, string before, CancellationToken cancellationToken)
    {
        var uri = $"https://discord.com/api/v10/guilds/{guildId}/audit-logs?limit=100";
        if (before.Length > 0)
            uri += $"&before={before}";

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", discordOptions.Value.Token);
            request.Headers.UserAgent.ParseAdd("DiscordBot (https://github.com/mathematics-discord/sylvan-bot, 1.0)");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                using var rateLimit = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var seconds = rateLimit.RootElement.GetProperty("retry_after").GetDouble();
                if (!double.IsFinite(seconds) || seconds < 0 || seconds > 300)
                    throw new HttpRequestException("Discord supplied an invalid audit retry interval.");

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(seconds, 0.1)), cancellationToken);
                continue;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    public async Task<TicketActor> GetActorAsync(ulong guildId, ulong userId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var member = await guild.GetUserAsync(userId, Options(cancellationToken));

        if (member?.IsBot != false)
            throw new BotCommandException("The responsible moderator is not an available human member of this server.");

        var permissions = member.GuildPermissions;

        return new TicketActor(
            guildId,
            userId,
            permissions.Administrator,
            permissions.ModerateMembers,
            permissions.BanMembers,
            permissions.KickMembers,
            permissions.MuteMembers,
            permissions.DeafenMembers,
            permissions.ManageRoles,
            permissions.ViewAuditLog);
    }

    public async Task CheckCaptureAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var guild = await socketClient.Rest.GetGuildAsync(guildId, Options(cancellationToken));
        var bot = await guild.GetCurrentUserAsync(Options(cancellationToken));

        if (!bot.GuildPermissions.ViewAuditLog)
            throw new BotCommandException("Sylvan needs View Audit Log to capture and reconcile moderation actions.");
    }

    internal static RequestOptions Options(CancellationToken token) => new RequestOptions
    {
        CancelToken = token,
        RetryMode = RetryMode.AlwaysFail,
    };

    public void Dispose() => _auditReadLock.Dispose();
}
