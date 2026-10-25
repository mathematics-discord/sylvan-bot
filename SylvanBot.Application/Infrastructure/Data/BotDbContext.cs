namespace SylvanBot.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Quorum;
using SylvanBot.Features.EmojiAnalytics;
using SylvanBot.Features.Logging;
using SylvanBot.Features.Moderation;
using SylvanBot.Features.RoleColours;

/// <summary>
///     Entity Framework Core database context for SylvanBot persistence.
/// </summary>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<EmojiUsageCount> EmojiUsageCounts => Set<EmojiUsageCount>();
    public DbSet<ImageSpamSettings> ImageSpamSettings => Set<ImageSpamSettings>();
    public DbSet<LoggingConfiguration> LoggingConfigurations => Set<LoggingConfiguration>();
    public DbSet<LoggingExcludedChannel> LoggingExcludedChannels => Set<LoggingExcludedChannel>();
    public DbSet<RoleColourOption> RoleColourOptions => Set<RoleColourOption>();

    public DbSet<QuorumConfiguration> QuorumConfigurations => Set<QuorumConfiguration>();
    public DbSet<QuorumVoterRole> QuorumVoterRoles => Set<QuorumVoterRole>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ulong>().HaveConversion<long>();
        configurationBuilder.Properties<ulong?>().HaveConversion<long?>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BotDbContext).Assembly);
    }
}