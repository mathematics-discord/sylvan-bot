using Discord.Net;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Features.MessageCache;
using SylvanBot.Gateway;
using static SylvanBot.Application.Features.Automod.AutomodAction;

namespace SylvanBot.Features.Automod;

public sealed class AutomodGatewayHandler(
    DiscordSocketClient client,
    AutomodRuntime runtime,
    AutomodBanEnforcement bans,
    MessageUpdateGatewayAdapter updatePayload,
    IHostApplicationLifetime lifetime,
    ILogger logger) : IDiscordGatewayHandler, IDisposable
{
    private readonly ILogger _logger = logger.ForContext<AutomodGatewayHandler>();
    private readonly SemaphoreSlim _muteGate = new SemaphoreSlim(1, 1);

    private bool _subscribed;

    public void Dispose() => _muteGate.Dispose();

    public Task InitializeAsync(CancellationToken ct)
    {
        if (_subscribed)
            return Task.CompletedTask;

        client.MessageReceived += ReceivedAsync;
        client.MessageUpdated += UpdatedAsync;
        _subscribed = true;

        return Task.CompletedTask;
    }

    public void Unsubscribe()
    {
        client.MessageReceived -= ReceivedAsync;
        client.MessageUpdated -= UpdatedAsync;
        _subscribed = false;
    }

    private Task ReceivedAsync(SocketMessage message) => ProcessAsync(message, message.Content);

    private Task UpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        var content = after.Content;

        if (updatePayload.Current is { HasValue: true, Value: var payload })
        {
            var matchesEvent = payload.MessageId == after.Id && payload.ChannelId == channel.Id;

            if (matchesEvent)
            {
                if (!payload.Content.HasValue)
                    return Task.CompletedTask;

                content = payload.Content.Value;
            }
        }

        if (before.HasValue && string.Equals(before.Value.Content, content, StringComparison.Ordinal))
            return Task.CompletedTask;

        return ProcessAsync(after, content);
    }

    private async Task<DotNext.Optional<bool>> IsExemptAsync(
        ulong guildId,
        SocketMessage message,
        AutomodConfiguration configuration,
        CancellationToken ct)
    {
        if (message.Author.Id == 0)
        {
            _logger.Warning("Automod exemption resolution failed for guild {GuildId}, message {MessageId}: author unavailable", guildId, message.Id);

            return default;
        }

        if (configuration.ExemptRoleIds.Contains(guildId))
            return new DotNext.Optional<bool>(true);

        if (configuration.ExemptRoleIds.IsEmpty)
            return new DotNext.Optional<bool>(false);

        try
        {
            var member = client.GetGuild(guildId)?.GetUser(message.Author.Id)
                         ?? (IGuildUser?)await client.Rest.GetGuildUserAsync(guildId, message.Author.Id, new RequestOptions { CancelToken = ct });

            if (member?.RoleIds != null)
                return new DotNext.Optional<bool>(configuration.ExemptRoleIds.Overlaps(member.RoleIds));

            _logger.Warning(
                "Automod exemption resolution failed for guild {GuildId}, message {MessageId}, user {UserId}: member roles unavailable",
                guildId,
                message.Id,
                message.Author.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpException ex)
        {
            _logger.Warning(
                "Automod exemption resolution failed for guild {GuildId}, message {MessageId}, user {UserId}: HTTP {HttpCode}, Discord {DiscordCode}",
                guildId,
                message.Id,
                message.Author.Id,
                ex.HttpCode,
                ex.DiscordCode);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                "Automod exemption resolution failed for guild {GuildId}, message {MessageId}, user {UserId}: {ExceptionType}",
                guildId,
                message.Id,
                message.Author.Id,
                ex.GetType().Name);
        }

        return default;
    }

    private async Task ProcessAsync(SocketMessage message, string content)
    {
        var eligible = message is SocketUserMessage && message.Source == MessageSource.User && !message.Author.IsBot && !message.Author.IsWebhook;

        if (!eligible || message.Channel is not IGuildChannel channel || string.IsNullOrEmpty(content))
            return;

        var guildId = channel.GuildId;
        var ct = lifetime.ApplicationStopping;

        try
        {
            if (runtime.Current(guildId) is not { HasValue: true, Value: var configuration })
            {
                _logger.Warning("Automod configuration unavailable for guild {GuildId}, message {MessageId}; skipped", guildId, message.Id);

                return;
            }

            DotNext.Optional<bool> exemption = await IsExemptAsync(guildId, message, configuration, ct);

            if (!exemption.HasValue || exemption.Value)
                return;

            var result = configuration.Matcher.Match(content);

            foreach (var ruleId in result.TimedOutRuleIds)
                _logger.Warning("Automod regex timed out for guild {GuildId}, message {MessageId}, rule {RuleId}", guildId, message.Id, ruleId);

            if (result.Rule is not { HasValue: true, Value: var rule } || result.Reason is not { HasValue: true, Value: var reason })
                return;

            if (rule.Action is not (Delete or Mute or Ban))
            {
                _logger.Warning(
                    "Automod action {Action} is unsupported for guild {GuildId}, message {MessageId}, rule {RuleId}; no action taken",
                    rule.Action,
                    guildId,
                    message.Id,
                    rule.Id);

                return;
            }

            await DeleteAsync(message, guildId, rule.Id, ct);

            switch (rule.Action)
            {
                case Ban:
                    await bans.BanAsync(guildId, message.Author.Id, message.Id, rule, reason, ct);

                    break;
                case Mute:
                    await MuteAsync(message, guildId, rule, reason, ct);

                    break;
                case Delete:
                case Note:
                default:
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Gateway shutdown cancels outstanding requests; events are not replayed.
        }
        catch (Exception ex)
        {
            _logger.Error(
                "Automod handling failed for guild {GuildId}, message {MessageId}: {ExceptionType}",
                guildId,
                message.Id,
                ex.GetType().Name);
        }
    }

    private async Task DeleteAsync(SocketMessage message, ulong guildId, long ruleId, CancellationToken ct)
    {
        try
        {
            await message.DeleteAsync(new RequestOptions { CancelToken = ct, AuditLogReason = $"Automod rule {ruleId}" });
            _logger.Information("Automod deleted message {MessageId} in guild {GuildId} for rule {RuleId}", message.Id, guildId, ruleId);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.UnknownMessage)
        {
            _logger.Debug("Automod message {MessageId} in guild {GuildId} for rule {RuleId} was already deleted", message.Id, guildId, ruleId);
        }
        catch (HttpException ex)
        {
            _logger.Warning(
                "Automod deletion failed for guild {GuildId}, message {MessageId}, rule {RuleId}: HTTP {HttpCode}, Discord {DiscordCode}",
                guildId,
                message.Id,
                ruleId,
                ex.HttpCode,
                ex.DiscordCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                "Automod deletion failed for guild {GuildId}, message {MessageId}, rule {RuleId}: {ExceptionType}",
                guildId,
                message.Id,
                ruleId,
                ex.GetType().Name);
        }
    }

    private async Task MuteAsync(SocketMessage message, ulong guildId, AutomodRule rule, string reason, CancellationToken ct)
    {
        await _muteGate.WaitAsync(ct);

        try
        {
            DotNext.Optional<long> duration = rule.DurationSeconds is { } seconds ? new DotNext.Optional<long>(seconds) : default;
            var requestedExpiry = AutomodDuration.MuteExpiry(duration, DateTimeOffset.UtcNow);
            var request = new RequestOptions { CancelToken = ct, RetryMode = RetryMode.AlwaysFail, AuditLogReason = reason };
            var member = await client.Rest.GetGuildUserAsync(guildId, message.Author.Id, request);

            if (member is null)
            {
                _logger.Warning(
                    "Automod timeout refused for guild {GuildId}, message {MessageId}, rule {RuleId}: member unavailable",
                    guildId,
                    message.Id,
                    rule.Id);

                return;
            }

            DotNext.Optional<DateTimeOffset> existingExpiry = member.TimedOutUntil is { } until
                ? new DotNext.Optional<DateTimeOffset>(until)
                : default;

            if (AutomodDuration.CoversExpiry(existingExpiry, requestedExpiry))
            {
                _logger.Information(
                    "Automod timeout unchanged for guild {GuildId}, message {MessageId}, rule {RuleId}: existing expiry covers requested expiry",
                    guildId,
                    message.Id,
                    rule.Id);

                return;
            }

            await member.ModifyAsync(properties => properties.TimedOutUntil = requestedExpiry, request);

            _logger.Information(
                "Automod timed out user {UserId} in guild {GuildId} until {Expiry} for rule {RuleId}, message {MessageId}",
                message.Author.Id,
                guildId,
                requestedExpiry,
                rule.Id,
                message.Id);
        }
        catch (ArgumentOutOfRangeException)
        {
            _logger.Warning(
                "Automod timeout refused for guild {GuildId}, message {MessageId}, rule {RuleId}: invalid stored duration {DurationSeconds}",
                guildId,
                message.Id,
                rule.Id,
                rule.DurationSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpException ex)
        {
            _logger.Warning(
                "Automod timeout failed for guild {GuildId}, message {MessageId}, rule {RuleId}: HTTP {HttpCode}, Discord {DiscordCode}",
                guildId,
                message.Id,
                rule.Id,
                ex.HttpCode,
                ex.DiscordCode);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                "Automod timeout failed for guild {GuildId}, message {MessageId}, rule {RuleId}: {ExceptionType}",
                guildId,
                message.Id,
                rule.Id,
                ex.GetType().Name);
        }
        finally
        {
            _muteGate.Release();
        }
    }
}
