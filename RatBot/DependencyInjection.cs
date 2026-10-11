using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RatBot.Application.Common.Forums;
using RatBot.Application.Features.Logging;
using RatBot.Application.Features.Quorum;
using RatBot.Application.Moderation;
using RatBot.Configuration;
using RatBot.Features.EmojiAnalytics;
using RatBot.Features.EmojiYoink;
using RatBot.Features.Logging;
using RatBot.Features.Logging.BackgroundWorkers;
using RatBot.Features.Logging.Gateway;
using RatBot.Features.Meta;
using RatBot.Features.Meta.BackgroundWorkers;
using RatBot.Features.Meta.Gateway;
using RatBot.Features.Quorum;
using RatBot.Features.RoleColours;
using RatBot.Forum;
using RatBot.Gateway;
using RatBot.Handlers;
using RatBot.Hosting;
using RatBot.Infrastructure;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Logging;
using RatBot.Infrastructure.Features.Meta;
using RatBot.Infrastructure.Features.Quorum.Persistence;
using RatBot.Infrastructure.Persistence.Repositories;
using RatBot.Infrastructure.Stores;

namespace RatBot;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        public void AddRatBot(IConfiguration configuration)
        {
            services.AddHostedService<DatabaseMigrationHostedService>();

            services.AddApplication();
            services.AddInfrastructure(configuration);
            services.AddDiscordAdapter(configuration);
        }

        private void AddApplication()
        {
            services.AddSingleton<ImageBurstSpamDetectorSettings>();
            services.AddSingleton(sp =>
                new ImageBurstSpamDetector(TimeProvider.System,
                    sp.GetRequiredService<ImageBurstSpamDetectorSettings>()));

            services.AddScoped<ImageSpamSettingsService>();
            services.AddScoped<IModerationService, ModerationService>();
            services.AddScoped<QuorumOperations>();
        }

        private void AddInfrastructure(IConfiguration configuration)
        {
            string connectionString = PostgresConnectionStringBuilder.Build(configuration);

            services.AddDbContextFactory<BotDbContext>(options => options.UseNpgsql(connectionString));

            services.AddScoped<IAutobannedUserRepository, AutobannedUserRepository>();
            services.AddScoped<IImageSpamSettingsStore, ImageSpamSettingsStore>();
            services.AddScoped<IQuorumConfigurationStore>(_ => new QuorumConfigurationStore(connectionString));
            services.AddSingleton<ModerationLoggingStore>();
            services.AddScoped<MetaProposalService>();
            services.AddScoped<MetaSuggestionSettingsService>();
        }

        private void AddDiscordAdapter(IConfiguration configuration)
        {
            services
                .AddOptions<DiscordOptions>()
                .Bind(configuration.GetSection(DiscordOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Token), "Discord token is required.")
                .Validate(options => options.MessageCacheSize >= 1000,
                    "Discord message cache size must be at least 1000.")
                .ValidateOnStart();

            services
                .AddOptions<EmojiAnalyticsOptions>()
                .Bind(configuration.GetSection(EmojiAnalyticsOptions.SectionName))
                .Validate(options => options.EnabledGuildIds.All(guildId => guildId != 0),
                    "Emoji analytics guild ids must be non-zero.")
                .ValidateOnStart();

            services
                .AddOptions<LoggingOptions>()
                .Bind(configuration.GetSection(LoggingOptions.SectionName))
                .Validate(options => options.MaxCachedMessageCount > 0,
                    "Logging max cached message count must be positive.")
                .Validate(options => options.MaxAttachmentCountPerMessage >= 0,
                    "Logging max attachment count per message must not be negative.")
                .Validate(
                    options => options.MaxAttachmentBytesPerAttachment >= 0,
                    "Logging max attachment bytes per attachment must not be negative."
                )
                .Validate(options => options.MaxTotalCachedAttachmentBytes >= 0,
                    "Logging max total cached attachment bytes must not be negative.")
                .Validate(options => options.MetadataRetentionPeriod > TimeSpan.Zero,
                    "Logging metadata retention period must be positive.")
                .Validate(options => options.MetadataCleanupInterval > TimeSpan.Zero,
                    "Logging metadata cleanup interval must be positive.")
                .ValidateOnStart();

            services.AddSingleton(sp =>
            {
                DiscordOptions options = sp.GetRequiredService<IOptions<DiscordOptions>>().Value;

                return new DiscordSocketClient(
                    new DiscordSocketConfig
                    {
                        AlwaysDownloadUsers = true,
                        MessageCacheSize = options.MessageCacheSize,
                        GatewayIntents =
                            GatewayIntents.Guilds
                            | GatewayIntents.GuildMembers
                            | GatewayIntents.GuildMessages
                            | GatewayIntents.GuildMessageReactions
                            | GatewayIntents.GuildMessagePolls
                            | GatewayIntents.MessageContent
                    }
                );
            });

            services.AddSingleton(sp => new InteractionService(
                sp.GetRequiredService<DiscordSocketClient>(),
                new InteractionServiceConfig { AutoServiceScopes = true }
            ));

            services.AddSingleton<DiscordInteractionHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<DiscordInteractionHandler>());
            services.AddSingleton<AutobanGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<AutobanGatewayHandler>());
            services.AddSingleton(sp =>
                new MessageEvidenceCache(sp.GetRequiredService<IOptions<LoggingOptions>>().Value
                    .ToEvidenceCacheSettings()));
            services.AddSingleton<HttpClient>();
            services.AddSingleton<DiscordGuildEmojiImporter>();
            services.AddSingleton<ModerationLoggingGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp =>
                sp.GetRequiredService<ModerationLoggingGatewayHandler>());
            services.AddSingleton<ImageBurstSpamGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<ImageBurstSpamGatewayHandler>());
            services.AddSingleton<MetaProposalPollResolver>();
            services.AddSingleton<MetaProposalGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<MetaProposalGatewayHandler>());

            services.AddHostedService<DiscordBotHostedService>();
            services.AddSingleton(
                Channel.CreateUnbounded<EmojiUsageIncrement>(
                    new UnboundedChannelOptions
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        AllowSynchronousContinuations = false
                    }
                )
            );
            services.AddSingleton<IDiscordClient>(sp => sp.GetRequiredService<DiscordSocketClient>());
            services.AddSingleton<EmojiUsageStore>();
            services.AddSingleton<EmojiAnalyticsGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<EmojiAnalyticsGatewayHandler>());
            services.AddHostedService<EmojiAnalyticsWorker>();

            services.AddScoped<RoleColourOperations>();
            services.AddSingleton<RoleColourReconciler>();
            services.AddSingleton<RoleColourGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RoleColourGatewayHandler>());

            services.AddHostedService<MetaProposalPollBackgroundWorker>();
            services.AddHostedService<LoggingMetadataCleanupBackgroundWorker>();

            services.AddSingleton<DiscordQuorumMemberIndex>();
            services.AddSingleton<IQuorumMemberSource, DiscordQuorumMemberSource>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<DiscordQuorumMemberIndex>());

            services.AddSingleton<IForumThreadClient, ForumThreadClient>();
            services.AddSingleton<MetaProposalDiscordWorkflow>();
        }
    }
}