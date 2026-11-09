using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.EmojiYoink;
using SylvanBot.Features.Automod;
using SylvanBot.Features.Logging;
using SylvanBot.Features.MessageCache;
using SylvanBot.Features.Moderation;
using SylvanBot.Features.RoleColours;
using SylvanBot.Features.RolePersistence;
using SylvanBot.Features.RoleReactions;
using SylvanBot.Features.SylvanMail;
using SylvanBot.Features.Tickets;
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
            services.AddHostedService<SylvanMailHostedService>();
        }

        private void AddFeatures(IConfiguration configuration)
        {
            services.AddSingleton<AutomodBanEnforcement>();
            services.AddSingleton<AutomodManagement>();
            services.AddSingleton<AutomodRuntime>();
            services.AddHostedService(sp => sp.GetRequiredService<AutomodRuntime>());
            services.AddSingleton<AutomodGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<AutomodGatewayHandler>());
            services
               .AddOptions<SylvanMailOptions>()
               .Bind(configuration.GetSection(SylvanMailOptions.SectionName))
               .Validate(options => !string.IsNullOrWhiteSpace(options.Token), "SylvanMail token is required.")
               .Validate(options => options.BotId != 0, "SylvanMail bot ID is required.")
               .ValidateOnStart();

            services.AddSingleton<SylvanMailClient>();

            services.AddSingleton<TicketBoundary>();
            services.AddSingleton<NoteTicketStrategy>();
            services.AddSingleton<BanTicketStrategy>();
            services.AddSingleton<TimeoutTicketStrategy>();
            services.AddSingleton<KickTicketStrategy>();
            services.AddSingleton<VoiceMuteTicketStrategy>();
            services.AddSingleton<VoiceDeafenTicketStrategy>();
            services.AddSingleton<RoleTicketStrategy>();
            services.AddSingleton<TicketLocks>();
            services.AddSingleton<TicketPublisher>();
            services.AddSingleton<TicketCoordinator>();
            services.AddSingleton<TicketReviewSummary>();
            services.AddSingleton<TicketWorker>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<TicketWorker>());
            services.AddHostedService(sp => sp.GetRequiredService<TicketWorker>());
            services.AddSingleton<TicketReactionGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<TicketReactionGatewayHandler>());
            services.AddHostedService(sp => sp.GetRequiredService<TicketReactionGatewayHandler>());

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

            services.AddSingleton<RolePersistenceWorker>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RolePersistenceWorker>());
            services.AddHostedService(sp => sp.GetRequiredService<RolePersistenceWorker>());

            services.AddSingleton<RoleReactionRuntime>();
            services.AddSingleton<RoleReactionConfiguration>();
            services.AddSingleton<RoleReactionCoordinator>();
            services.AddSingleton<RoleReactionGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RoleReactionGatewayHandler>());
            services.AddHostedService(sp => sp.GetRequiredService<RoleReactionGatewayHandler>());

            services.AddSingleton<ImageBurstSpamDetector>();
            services.AddSingleton<ImageSpamSettingsService>();

            services.AddSingleton<DiscordGuildEmojiImporter>();
            services.AddModerationLogging(configuration);
            services.AddSingleton<ImageBurstSpamGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<ImageBurstSpamGatewayHandler>());

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
                                                           options.UseNpgsql(
                                                               connectionString: connectionString,
                                                               postgres => postgres.MigrationsAssembly("SylvanBot")));
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
                            | GatewayIntents.GuildVoiceStates
                            | GatewayIntents.GuildBans
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
