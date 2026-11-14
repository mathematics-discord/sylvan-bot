using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Discord.Net;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Announcements;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Gateway;

namespace SylvanBot.Features.Announcements;

public sealed class AnnouncementRuntime(DiscordSocketClient client, IDbContextFactory<BotDbContext> contexts, ILogger logger)
    : BackgroundService, IDiscordGatewayHandler
{
    public const string SubscribeId = "announcements:subscribe";
    public const string UnsubscribeId = "announcements:unsubscribe";

    private readonly ConcurrentDictionary<ulong, ulong> _channels = new ConcurrentDictionary<ulong, ulong>();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _gates = new ConcurrentDictionary<ulong, SemaphoreSlim>();
    private readonly ConcurrentDictionary<ulong, byte> _pending = new ConcurrentDictionary<ulong, byte>();
    private readonly Channel<ulong> _work = Channel.CreateUnbounded<ulong>(new UnboundedChannelOptions { SingleReader = true });

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        AnnouncementSettings[] settings = await db.AnnouncementSettings
            .AsNoTracking()
            .ToArrayAsync(ct);

        foreach (var row in settings)
            _channels[row.ChannelId] = row.GuildId;

        client.Ready += ReadyAsync;
        client.GuildAvailable += AvailableAsync;
        client.MessageReceived += ReceivedAsync;
        client.MessageDeleted += DeletedAsync;
    }

    public void Unsubscribe()
    {
        client.Ready -= ReadyAsync;
        client.GuildAvailable -= AvailableAsync;
        client.MessageReceived -= ReceivedAsync;
        client.MessageDeleted -= DeletedAsync;
        _work.Writer.TryComplete();
    }

    public async Task<ulong> ConfigureAsync(SocketGuild guild, ITextChannel channel, CancellationToken ct)
    {
        var gate = Gate(guild.Id);
        await gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await db.AnnouncementSettings.FindAsync([guild.Id], ct);

            if (settings is null)
            {
                settings = new AnnouncementSettings { GuildId = guild.Id };
                db.AnnouncementSettings.Add(settings);
            }

            var options = new RequestOptions { CancelToken = ct, AuditLogReason = "Configure announcements" };
            IRole? role = guild.GetRole(settings.RoleId);

            if (role is null)
            {
                role = await guild.CreateRoleAsync(
                    name: "📯",
                    permissions: GuildPermissions.None,
                    isHoisted: false,
                    isMentionable: false,
                    options: options);

                settings.RoleId = role.Id;
            }
            else
            {
                await role.ModifyAsync(
                    properties =>
                    {
                        properties.Name = "📯";
                        properties.Hoist = false;
                        properties.Mentionable = false;
                    },
                    options);
            }

            var previousChannelId = settings.ChannelId;
            var previousMessageId = settings.MessageId;
            var moved = previousChannelId != channel.Id;
            settings.ChannelId = channel.Id;

            if (moved)
                settings.MessageId = 0;

            await db.SaveChangesAsync(ct);

            _channels.TryRemove(previousChannelId, out _);
            _channels[channel.Id] = guild.Id;

            await MaintainMessageAsync(db, settings, channel, ct);

            if (!moved || previousMessageId == 0)
                return settings.RoleId;

            var previousChannel = await client.Rest.GetChannelAsync(previousChannelId, options) as ITextChannel;

            if (previousChannel is not null)
                await DeleteSubscriptionAsync(previousChannel, previousMessageId, ct);

            return settings.RoleId;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> PublishAsync(SocketGuild guild, string title, string body, CancellationToken ct)
    {
        if (title.Length > 100 || body.Length > 3850 || string.IsNullOrWhiteSpace(body))
            throw new BotCommandException("Enter a title of up to 100 characters and a body of up to 3,850 characters.");

        title = title.TrimStart().TrimStart('#').TrimStart();

        if (string.IsNullOrWhiteSpace(title) || title.Contains('\n') || title.Contains('\r'))
            throw new BotCommandException("Enter a non-empty, single-line title after removing its heading marks.");

        var gate = Gate(guild.Id);
        await gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await SettingsAsync(db, guild.Id, ct);
            var options = new RequestOptions { CancelToken = ct };

            var channel = await client.Rest.GetChannelAsync(settings.ChannelId, options) as ITextChannel
                          ?? throw new BotCommandException("The announcements channel is missing. Ask an administrator to configure it again.");

            var components = new ComponentBuilderV2()
                .WithTextDisplay($"## <@&{settings.RoleId}> — {title}\n{body}")
                .Build();

            var message = await channel.SendMessageAsync(
                options: options,
                allowedMentions: new AllowedMentions { RoleIds = new List<ulong> { settings.RoleId } },
                components: components,
                flags: MessageFlags.ComponentsV2);

            var confirmation = $"Announcement sent: https://discord.com/channels/{guild.Id}/{channel.Id}/{message.Id}";

            try
            {
                await MaintainMessageAsync(db, settings, channel, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.Error(
                    exception,
                    "Announcement sent but subscription message maintenance failed in guild {GuildId}, channel {ChannelId}",
                    guild.Id,
                    channel.Id);

                Queue(guild.Id);
                return confirmation + "\nThe subscription message could not be moved; the bot will check it again.";
            }

            return confirmation;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SetSubscriptionAsync(SocketMessageComponent component, bool subscribe, CancellationToken ct)
    {
        if (component.User is not SocketGuildUser member)
            throw new BotCommandException("Use the subscription buttons in the server's announcements channel.");

        var guild = member.Guild;
        var gate = Gate(guild.Id);
        await gate.WaitAsync(ct);

        try
        {
            await using var db = await contexts.CreateDbContextAsync(ct);
            var settings = await SettingsAsync(db, guild.Id, ct);

            var currentMessage = component.Channel.Id == settings.ChannelId
                                 && component.Message.Id == settings.MessageId
                                 && IsSubscriptionMessage(component.Message);

            if (!currentMessage)
                throw new BotCommandException("Use the current subscription buttons at the bottom of the announcements channel.");

            var options = new RequestOptions { CancelToken = ct, AuditLogReason = "Member changed announcement subscription" };

            if (subscribe)
                await member.AddRoleAsync(settings.RoleId, options);
            else
                await member.RemoveRoleAsync(settings.RoleId, options);
        }
        finally
        {
            gate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var guildId in _work.Reader.ReadAllAsync(stoppingToken))
        {
            _pending.TryRemove(guildId, out _);
            var gate = Gate(guildId);
            await gate.WaitAsync(stoppingToken);

            try
            {
                var guild = client.GetGuild(guildId);

                if (((IGuild)guild)?.Available != true)
                    continue;

                await using var db = await contexts.CreateDbContextAsync(stoppingToken);
                var settings = await SettingsAsync(db, guildId, stoppingToken);
                var options = new RequestOptions { CancelToken = stoppingToken };

                if (await client.Rest.GetChannelAsync(settings.ChannelId, options) is not ITextChannel channel)
                    throw new BotCommandException("The configured announcements channel is missing.");

                await MaintainMessageAsync(db, settings, channel, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Announcement subscription message maintenance failed in guild {GuildId}", guildId);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        foreach (var gate in _gates.Values)
            gate.Dispose();
    }

    private async Task MaintainMessageAsync(BotDbContext db, AnnouncementSettings settings, ITextChannel channel, CancellationToken ct)
    {
        var options = new RequestOptions { CancelToken = ct };

        if (channel is SocketTextChannel)
            channel = await client.Rest.GetChannelAsync(channel.Id, options) as ITextChannel
                      ?? throw new BotCommandException("The announcements channel is unavailable.");

        IEnumerable<IMessage> latest = await channel.GetMessagesAsync(limit: 1, options: options)
            .FlattenAsync();

        var alreadyLast = latest.Any(message => message.Id == settings.MessageId && IsSubscriptionMessage(message));

        if (alreadyLast)
            return;

        var buttons = new ActionRowBuilder()
            .WithButton("Subscribe", SubscribeId)
            .WithButton("Unsubscribe", UnsubscribeId, ButtonStyle.Secondary);

        var components = new ComponentBuilderV2()
            .WithTextDisplay("Subscribe to receive announcement notifications.")
            .WithActionRow(buttons)
            .Build();

        var message = await channel.SendMessageAsync(
            components: components,
            flags: MessageFlags.ComponentsV2,
            allowedMentions: AllowedMentions.None,
            options: options);

        var previousMessageId = settings.MessageId;
        settings.MessageId = message.Id;
        await db.SaveChangesAsync(ct);

        if (previousMessageId != 0)
            await DeleteSubscriptionAsync(channel, previousMessageId, ct);
    }

    private async Task DeleteSubscriptionAsync(ITextChannel channel, ulong messageId, CancellationToken ct)
    {
        var options = new RequestOptions { CancelToken = ct };

        try
        {
            await channel.DeleteMessageAsync(messageId, options);
        }
        catch (HttpException exception) when (exception.HttpCode == HttpStatusCode.NotFound)
        {
            // A moderator may already have removed the previous subscription message.
        }
    }

    private static async Task<AnnouncementSettings> SettingsAsync(BotDbContext db, ulong guildId, CancellationToken ct) =>
        await db.AnnouncementSettings.FindAsync([guildId], ct)
        ?? throw new BotCommandException("Ask an administrator to run /config announcements first.");

    private bool IsSubscriptionMessage(IMessage message)
    {
        if (message.Author.Id != client.CurrentUser.Id || message is not IUserMessage userMessage)
            return false;

        IEnumerable<ButtonComponent> buttons = userMessage.Components
            .OfType<ActionRowComponent>()
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>();

        return buttons.Any(button => button.CustomId is SubscribeId or UnsubscribeId);
    }

    private SemaphoreSlim Gate(ulong guildId) => _gates.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));

    private void Queue(ulong guildId)
    {
        if (_pending.TryAdd(guildId, 0))
            _work.Writer.TryWrite(guildId);
    }

    private Task ReadyAsync()
    {
        foreach (var guildId in _channels.Values)
            Queue(guildId);

        return Task.CompletedTask;
    }

    private Task AvailableAsync(SocketGuild guild)
    {
        if (_channels.Values.Contains(guild.Id))
            Queue(guild.Id);

        return Task.CompletedTask;
    }

    private Task ReceivedAsync(SocketMessage message)
    {
        if (_channels.TryGetValue(message.Channel.Id, out var guildId) && !IsSubscriptionMessage(message))
            Queue(guildId);

        return Task.CompletedTask;
    }

    private Task DeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (_channels.TryGetValue(channel.Id, out var guildId))
            Queue(guildId);

        return Task.CompletedTask;
    }
}
