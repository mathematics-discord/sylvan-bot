using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Features.EmojiAnalytics;
using SylvanBot.Features.EmojiAnalytics.Persistence;
using SylvanBot.Features.EmojiYoink;
using SylvanBot.Features.Logging;
using SylvanBot.Features.MessageCache;
using SylvanBot.Features.Moderation;
using SylvanBot.Features.RoleColours;
using SylvanBot.Features.RoleReactions;
using SylvanBot.Features.SylvanMail;
using SylvanBot.Features.WorkingHours;
using SylvanBot.Gateway;
using SylvanBot.Infrastructure.Data;
using SylvanBot.Interactions;

namespace SylvanBot;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public void AddSylvanBot(IConfiguration configuration)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();

            services.AddSingleton(TimeProvider.System);
            services.AddPersistence(configuration);
            services.AddDiscord(configuration);
            services.AddFeatures(configuration);
            // Stop gateway admission before stopping feature consumers (host shutdown is reverse registration order).
            services.AddHostedService<DiscordBotHostedService>();
        }

        private void AddFeatures(IConfiguration configuration)
        {
            services.AddSingleton<ISylvanMailDiscord, SylvanMailDiscord>();
            services.AddSingleton<ISylvanMailDrafts, SylvanMailDrafts>();
            services.AddSingleton<ISylvanMailForum, SylvanMailForum>();
            services.AddSingleton<SylvanMailReporting>();
            services.AddSingleton<SylvanMailConfiguration>();
            services.AddSingleton<SylvanMailDraftWorkflow>();
            services.AddSingleton<SylvanMailConversations>();
            services.AddSingleton<SylvanMailWorkflow>();
            services.AddSingleton<SylvanMailGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<SylvanMailGatewayHandler>());
            services.AddHostedService(sp => sp.GetRequiredService<SylvanMailGatewayHandler>());

            services.AddWorkingHours();

            services.AddSingleton<RoleReactionRuntime>();
            services.AddSingleton<RoleReactionConfiguration>();
            services.AddSingleton<RoleReactionCoordinator>();
            services.AddSingleton<RoleReactionGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RoleReactionGatewayHandler>());
            services.AddHostedService(sp => sp.GetRequiredService<RoleReactionGatewayHandler>());

            services.AddSingleton<ImageBurstSpamDetector>();
            services.AddSingleton<ImageSpamSettingsService>();

            services
                .AddOptions<EmojiAnalyticsOptions>()
                .Bind(configuration.GetSection(EmojiAnalyticsOptions.SectionName))
                .Validate(
                    options => options.EnabledGuildIds
                        .All(guildId => guildId != 0),
                    "Emoji analytics guild ids must be non-zero.")
                .ValidateOnStart();

            services.AddSingleton<DiscordGuildEmojiImporter>();
            services.AddModerationLogging(configuration);
            services.AddSingleton<ImageBurstSpamGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<ImageBurstSpamGatewayHandler>());

            services.AddSingleton<EmojiUsageStore>();
            services.AddSingleton<EmojiAnalyticsGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<EmojiAnalyticsGatewayHandler>());
            services.AddSingleton<EmojiAnalyticsWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<EmojiAnalyticsWorker>());

            services.AddScoped<RoleColourOperations>();
            services.AddSingleton<RoleColourReconciler>();
            services.AddSingleton<RoleColourGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RoleColourGatewayHandler>());
        }

        private void AddModerationLogging(IConfiguration configuration)
        {
            services
                .AddOptions<LoggingOptions>()
                .Bind(configuration.GetSection(LoggingOptions.SectionName))
                .Validate(options => options.MaxAttachmentCountPerMessage >= 0, "Logging max attachment count per message must not be negative.")
                .Validate(
                    options => options.MaxAttachmentBytesPerAttachment >= 0,
                    "Logging max attachment bytes per attachment must not be negative.")
                .ValidateOnStart();

            services.AddSingleton<IMessageCachePersistence, PostgresMessageCachePersistence>();
            services.AddSingleton<MessageSnapshotCache>();
            services.AddSingleton<MessageCacheMaintenanceWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<MessageCacheMaintenanceWorker>());
            services.AddSingleton<LoggingScopeResolver>();
            services.AddSingleton<ModerationAuditLookup>();
            services.AddSingleton<ModerationLogPublisher>();
            services.AddSingleton<ModerationLoggingGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<ModerationLoggingGatewayHandler>());
            services.AddHostedService(sp => sp.GetRequiredService<ModerationLoggingGatewayHandler>());
        }

        private void AddWorkingHours()
        {
            services.AddSingleton<WorkingHoursRuntime>();
            services.AddSingleton<WorkingHoursGuildRules>();
            services.AddSingleton<WorkingHoursReconciler>();
            services.AddSingleton<WorkingHoursConfigurationView>();
            services.AddSingleton<WorkingHoursConfiguration>();
            services.AddSingleton<WorkingHoursSchedules>();
            services.AddHostedService<WorkingHoursWorker>();
            services.AddSingleton<IDiscordGatewayHandler, WorkingHoursGatewayHandler>();
        }

        private void AddPersistence(IConfiguration configuration)
        {
            var connectionString = PostgresConnectionStringBuilder.Build(configuration);

            services.AddDbContextFactory<BotDbContext>(options =>
                options.UseNpgsql(connectionString: connectionString, postgres => postgres.MigrationsAssembly("SylvanBot")));
        }

        private void AddDiscord(IConfiguration configuration)
        {
            services
                .AddOptions<DiscordOptions>()
                .Bind(configuration.GetSection(DiscordOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Token), "Discord token is required.")
                .Validate(options => options.MessageCacheSize is >= 0 and <= 50000, "Discord message cache size must be between 0 and 50000.")
                .ValidateOnStart();

            services.AddSingleton<MessageUpdateGatewayAdapter>();

            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<DiscordOptions>>().Value;

                return new DiscordSocketClient(
                    new DiscordSocketConfig
                    {
                        AlwaysDownloadUsers = true,
                        MessageCacheSize = options.MessageCacheSize,
                        WebSocketProvider = sp.GetRequiredService<MessageUpdateGatewayAdapter>().CreateWebSocket,
                        GatewayIntents =
                            GatewayIntents.Guilds
                            | GatewayIntents.DirectMessages
                            | GatewayIntents.GuildMembers
                            | GatewayIntents.GuildMessages
                            | GatewayIntents.GuildMessageReactions
                            | GatewayIntents.MessageContent,
                    });
            });

            services.AddSingleton(sp => new InteractionService(
                sp.GetRequiredService<DiscordSocketClient>(),
                new InteractionServiceConfig
                {
                    AutoServiceScopes = true,
                    ThrowOnError = false,
                    EnableAutocompleteHandlers = true,
                }
            ));

            services.AddSingleton<InteractionDiagnostics>();
            services.AddSingleton<InteractionCommandRegistrar>();
            services.AddSingleton<DiscordInteractionHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<DiscordInteractionHandler>());
            services.AddSingleton<HttpClient>();
            services.AddSingleton<IDiscordClient>(sp => sp.GetRequiredService<DiscordSocketClient>());
            // Subscribe to readiness before the gateway starts.
            services.AddHostedService<MessageCacheSamplingWorker>();
        }
    }
}