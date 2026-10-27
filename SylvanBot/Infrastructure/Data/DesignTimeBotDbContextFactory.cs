using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SylvanBot.Infrastructure.Data;

/// <summary>
///     Creates <see cref="BotDbContext"/> instances for design-time tooling such as EF Core migrations.
/// </summary>
public sealed class DesignTimeBotDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    /// <summary>
    ///     Creates a design-time <see cref="BotDbContext"/> instance.
    /// </summary>
    /// <param name="args">Design-time arguments.</param>
    /// <returns>The constructed <see cref="BotDbContext"/> instance.</returns>
    public BotDbContext CreateDbContext(string[] args)
    {
        Env.TraversePath().Load();

        var configurationRoot = new ConfigurationBuilder().AddEnvironmentVariables().Build();

        var connectionString =
            configurationRoot.GetConnectionString("BotDb")
            ?? configurationRoot["DB:ConnectionString"]
            ?? Environment.GetEnvironmentVariable("DB__CONNECTION_STRING");

        if (connectionString is null && BuildFromDiscrete(configurationRoot).Value is Some<string>(var discreteConnectionString))
            connectionString = discreteConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Design-time connection string missing. Set DB__CONNECTION_STRING or discrete DB__* vars.");
        }

        DbContextOptions<BotDbContext> options =
            new DbContextOptionsBuilder<BotDbContext>()
               .UseNpgsql(connectionString: connectionString, postgres => postgres.MigrationsAssembly("SylvanBot"))
               .Options;

        return new BotDbContext(options);
    }

    private static Option<string> BuildFromDiscrete(IConfiguration c)
    {
        var host = c["DB:Host"] ?? c["Database:Host"] ?? Environment.GetEnvironmentVariable("DB__HOST");
        var port = c["DB:Port"] ?? c["Database:Port"] ?? Environment.GetEnvironmentVariable("DB__PORT");
        var db   = c["DB:Database"] ?? c["Database:Name"] ?? Environment.GetEnvironmentVariable("DB__DATABASE");
        var user = c["DB:User"] ?? c["Database:User"] ?? Environment.GetEnvironmentVariable("DB__USER");
        var pwd  = c["DB:Password"] ?? c["Database:Password"] ?? Environment.GetEnvironmentVariable("DB__PASSWORD");

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(db) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd))
            return new None();

        var resolvedPort = string.IsNullOrWhiteSpace(port) ? "5432" : port;

        return new Some<string>(
            $"Host={host};Port={resolvedPort};Database={db};Username={user};Password={pwd};SSL Mode=Prefer");
    }
}