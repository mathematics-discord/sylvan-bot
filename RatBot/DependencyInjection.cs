using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using RatBot.Application.Features.Quorum;
using RatBot.Application.Moderation;
using RatBot.Configuration;
using RatBot.Features.EmojiAnalytics;
using RatBot.Features.EmojiYoink;
using RatBot.Features.Quorum;
using RatBot.Features.RoleColours;
using RatBot.Gateway;
using RatBot.Handlers;
using RatBot.Hosting;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Quorum.Persistence;

namespace RatBot;

[SuppressMessage("Design", "MA0051:Method is too long")]
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
            services.AddScoped<QuorumOperations>();
        }

        private void AddInfrastructure(IConfiguration configuration)
        {
            string connectionString = PostgresConnectionStringBuilder.Build(configuration);

            services.AddDbContextFactory<BotDbContext>(options => options.UseNpgsql(connectionString));

            services.AddScoped<IQuorumConfigurationStore>(_ => new QuorumConfigurationStore(connectionString));
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

            services.AddSingleton(sp =>
            {
                DiscordOptions options = sp.GetRequiredService<IOptions<DiscordOptions>>().Value;

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
                config: new InteractionServiceConfig { AutoServiceScopes = true }));

            services.AddSingleton<DiscordInteractionHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<DiscordInteractionHandler>());
            services.AddSingleton<HttpClient>();
            services.AddSingleton<DiscordGuildEmojiImporter>();
            services.AddSingleton<ImageBurstSpamGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<ImageBurstSpamGatewayHandler>());

            services.AddHostedService<DiscordBotHostedService>();
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
            services.AddSingleton<IDiscordClient>(sp => sp.GetRequiredService<DiscordSocketClient>());
            services.AddSingleton<EmojiUsageStore>();
            services.AddSingleton<EmojiAnalyticsGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<EmojiAnalyticsGatewayHandler>());
            services.AddHostedService<EmojiAnalyticsWorker>();

            services.AddScoped<RoleColourOperations>();
            services.AddSingleton<RoleColourReconciler>();
            services.AddSingleton<RoleColourGatewayHandler>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<RoleColourGatewayHandler>());

            services.AddSingleton<DiscordQuorumMemberIndex>();
            services.AddSingleton<IQuorumMemberSource, DiscordQuorumMemberSource>();
            services.AddSingleton<IDiscordGatewayHandler>(sp => sp.GetRequiredService<DiscordQuorumMemberIndex>());
        }
    }
}