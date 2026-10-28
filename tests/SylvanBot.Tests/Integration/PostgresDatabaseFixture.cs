using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SylvanBot.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace SylvanBot.Tests.Integration;

[SetUpFixture]
[SuppressMessage("Structure", "NUnit1028:The non-test method is public")]
public sealed class PostgresDatabaseFixture
{
    private static PostgreSqlContainer _container = null!;

    private static string ConnectionString => _container.GetConnectionString();

    public static BotDbContext CreateDbContext()
    {
        DbContextOptions<BotDbContext> options = new DbContextOptionsBuilder<BotDbContext>()
            .UseNpgsql(connectionString: ConnectionString, postgres => postgres.MigrationsAssembly("SylvanBot"))
            .EnableSensitiveDataLogging()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new BotDbContext(options);
    }

    public static async Task ResetAsync()
    {
        await using var db = CreateDbContext();

        await db.Database.ExecuteSqlRawAsync("DELETE FROM logging_excluded_channels").ConfigureAwait(false);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM logging_configurations").ConfigureAwait(false);

        await db.RoleColourOptions
            .ExecuteDeleteAsync().ConfigureAwait(false);

        await db.ImageSpamSettings
            .ExecuteDeleteAsync().ConfigureAwait(false);

        await db.EmojiUsageCounts
            .ExecuteDeleteAsync().ConfigureAwait(false);
    }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _container.DisposeAsync();
    }

    internal static IDbContextFactory<BotDbContext> CreateContextFactory()
    {
        return new PooledDbContextFactory<BotDbContext>(
            new DbContextOptionsBuilder<BotDbContext>()
                .UseNpgsql(connectionString: ConnectionString, postgres => postgres.MigrationsAssembly("SylvanBot"))
                .Options);
    }
}