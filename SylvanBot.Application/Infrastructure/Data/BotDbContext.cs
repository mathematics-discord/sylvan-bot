using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Logging;
using SylvanBot.Application.Features.MessageCache;
using SylvanBot.Application.Features.Moderation;
using SylvanBot.Application.Features.RoleColours;
using SylvanBot.Application.Features.RoleReactions;
using SylvanBot.Application.Features.SylvanMail;
using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Application.Infrastructure.Data;

/// <summary>
///     Entity Framework Core database context for SylvanBot persistence.
/// </summary>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<CachedMessage> CachedMessages => Set<CachedMessage>();

    public DbSet<SylvanMailSettings> SylvanMailSettings => Set<SylvanMailSettings>();

    public DbSet<SylvanMailConversation> SylvanMailConversations => Set<SylvanMailConversation>();

    public DbSet<SylvanMailOperation> SylvanMailOperations => Set<SylvanMailOperation>();

    public DbSet<WorkingHoursPreference> WorkingHours => Set<WorkingHoursPreference>();

    public DbSet<WorkingHoursSettings> WorkingHoursSettings => Set<WorkingHoursSettings>();

    public DbSet<ImageSpamSettings> ImageSpamSettings => Set<ImageSpamSettings>();

    public DbSet<LoggingConfiguration> LoggingConfigurations => Set<LoggingConfiguration>();

    public DbSet<LoggingExcludedChannel> LoggingExcludedChannels => Set<LoggingExcludedChannel>();

    public DbSet<RoleColourOption> RoleColourOptions => Set<RoleColourOption>();

    public DbSet<RoleReaction> RoleReactions => Set<RoleReaction>();

    public DbSet<SylvanBot.Application.Features.RolePersistence.PersistentRole> PersistentRoles => Set<SylvanBot.Application.Features.RolePersistence.PersistentRole>();
    public DbSet<SylvanBot.Application.Features.RolePersistence.PendingRole> PendingRoles => Set<SylvanBot.Application.Features.RolePersistence.PendingRole>();

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