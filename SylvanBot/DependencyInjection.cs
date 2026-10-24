namespace SylvanBot;

using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Quorum;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.EmojiAnalytics;
using SylvanBot.Features.EmojiAnalytics.Persistence;
using SylvanBot.Features.EmojiYoink;
using SylvanBot.Features.Logging;
using SylvanBot.Features.Moderation;
using SylvanBot.Features.Quorum;
using SylvanBot.Features.RoleColours;
using SylvanBot.Features.Tickets;
using SylvanBot.Gateway;
using SylvanBot.Infrastructure.Data;
using SylvanBot.Interactions;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public void AddSylvanBot(IConfiguration configuration)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();

            services.AddPersistence(configuration);
            services.AddDiscord(configuration);
            services.AddFeatures(configuration);
        }

        private void AddFeatures(IConfiguration configuration)
        {
            services.AddSingleton<IDiscordTicket, DiscordTicket>();
            services.AddSingleton<TicketWorkflow>();

            services.AddSingleton<IQuorumMembers, DiscordQuorumMembers>();
            services.AddScoped<QuorumOperations>();

            services.AddSingleton<ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>>();
            services.AddSingleton(sp =>
                new ImageBurstSpamDetector(TimeProvider.System,
                    sp.GetRequiredService<ConcurrentDictionary<ulong, ImageBurstSpamDetectorOptions>>()));

            services.AddScoped<ImageSpamSettingsService>();

            services
                .AddOptions<EmojiAnalyticsOptions>()
                .Bind(configuration.GetSection(EmojiAnalyticsOptions.SectionName))
                .Validate(options => options.EnabledGuildIds.All(guildId => guildId != 0),
                    "Emoji analytics guild ids must be non-zero.")
                .ValidateOnStart();

            services
                .AddOptions<LoggingOptions>()
                .Bind(configuration.GetSection(LoggingOptions.SectionName))
                .Validate(options => options.MaxAttachmentCountPerMessage >= 0,
                    "Logging max attachment count per message must not be negative.")
                .Validate(
                    options => options.MaxAttachmentBytesPerAttachment >= 0,
                    "Logging max attachment bytes per attachment must not be negative."
                )
                .ValidateOnStart();

            services.AddSingleton<DiscordGuildEmojiImporter>();
            services.AddSingleton<IDiscordGatewayHandler, ModerationLoggingGatewayHandler>();
            services.AddSingleton<ImageBurstSpamGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<ImageBurstSpamGatewayHandler>());

            services.AddSingleton(
                Channel.CreateUnbounded<EmojiUsageIncrement>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        AllowSynchronousContinuations = false,
                    }
                )
            );
            services.AddSingleton<EmojiUsageStore>();
            services.AddSingleton<EmojiAnalyticsGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<EmojiAnalyticsGatewayHandler>());
            services.AddHostedService<EmojiAnalyticsWorker>();

            services.AddScoped<RoleColourOperations>();
            services.AddSingleton<RoleColourReconciler>();
            services.AddSingleton<RoleColourGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RoleColourGatewayHandler>());
        }

        private void AddPersistence(IConfiguration configuration)
        {
            var connectionString = PostgresConnectionStringBuilder.Build(configuration);

            services.AddDbContextFactory<BotDbContext>(options =>
                options.UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly("SylvanBot")));
        }

        private void AddDiscord(IConfiguration configuration)
        {
            services
                .AddOptions<DiscordOptions>()
                .Bind(configuration.GetSection(DiscordOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Token), "Discord token is required.")
                .Validate(options => options.MessageCacheSize >= 1000,
                    "Discord message cache size must be at least 1000.")
                .ValidateOnStart();

            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<DiscordOptions>>().Value;

                return new DiscordSocketClient(
                    new DiscordSocketConfig
                    {
                        AlwaysDownloadUsers = true,
                        MessageCacheSize = options.MessageCacheSize,
                        GatewayIntents = GatewayIntents.Guilds
                                         | GatewayIntents.GuildMembers
                                         | GatewayIntents.GuildMessages
                                         | GatewayIntents.GuildMessageReactions
                                         | GatewayIntents.MessageContent,
                    }
                );
            });

            services.AddSingleton(sp => new InteractionService(
                discordProvider: sp.GetRequiredService<DiscordSocketClient>(),
                config: new InteractionServiceConfig { AutoServiceScopes = true, ThrowOnError = false }));

            services.AddSingleton<DiscordInteractionHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<DiscordInteractionHandler>());
            services.AddSingleton<HttpClient>();
            services.AddSingleton<IDiscordClient>(sp => sp.GetRequiredService<DiscordSocketClient>());
            services.AddHostedService<DiscordBotHostedService>();
        }
    }
}
