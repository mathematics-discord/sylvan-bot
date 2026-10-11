using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Quorum.Persistence;
using Testcontainers.PostgreSql;

namespace RatBot.Infrastructure.Tests.Integration;

[SetUpFixture]
[SuppressMessage("Structure", "NUnit1028:The non-test method is public")]
public sealed class PostgresDatabaseFixture
{
    private static PostgreSqlContainer _container = null!;

    internal static string ConnectionString => _container.GetConnectionString();

    public static BotDbContext CreateDbContext()
    {
        DbContextOptions<BotDbContext> options = new DbContextOptionsBuilder<BotDbContext>()
            .UseNpgsql(ConnectionString)
            .EnableSensitiveDataLogging()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new BotDbContext(options);
    }

    public static QuorumConfigurationStore CreateQuorumConfigurationStore() => new QuorumConfigurationStore(ConnectionString);

    public static async Task ResetAsync()
    {
        BotDbContext db = CreateDbContext();

        await using (db.ConfigureAwait(false))
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM message_log_entries").ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM observed_messages").ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM logging_excluded_channels").ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM logging_configurations").ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM quorum_configurations").ConfigureAwait(false);
            await db.MetaProposalStates.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.MetaSuggestionSettings.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.RoleColourOptions.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.ImageSpamSettings.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.AutobannedUsers.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.EmojiUsageCounts.ExecuteDeleteAsync().ConfigureAwait(false);
        }
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        await _container.StartAsync();

        await using BotDbContext db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }
}
