using Microsoft.EntityFrameworkCore;
using RatBot.Features.EmojiAnalytics;
using RatBot.Features.Logging;
using RatBot.Features.Moderation;
using RatBot.Features.RoleColours;
using RatBot.Features.Tickets;

namespace RatBot.Infrastructure.Data;

/// <summary>
///     Entity Framework Core database context for RatBot persistence.
/// </summary>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<EmojiUsageCount> EmojiUsageCounts => Set<EmojiUsageCount>();
    public DbSet<ImageSpamSettings> ImageSpamSettings => Set<ImageSpamSettings>();
    public DbSet<LoggingConfiguration> LoggingConfigurations => Set<LoggingConfiguration>();
    public DbSet<LoggingExcludedChannel> LoggingExcludedChannels => Set<LoggingExcludedChannel>();
    public DbSet<RoleColourOption> RoleColourOptions => Set<RoleColourOption>();
    public DbSet<TicketConfiguration> TicketConfigurations => Set<TicketConfiguration>();
    public DbSet<TicketNote> TicketNotes => Set<TicketNote>();

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
