namespace SylvanBot.Infrastructure.Data;

/// <summary>
///     Builds PostgreSQL connection strings from environment-backed configuration.
/// </summary>
public static class PostgresConnectionStringBuilder
{
    /// <summary>
    ///     Builds a PostgreSQL connection string from configured values.
    /// </summary>
    /// <param name="config">The application configuration root.</param>
    /// <returns>The PostgreSQL connection string.</returns>
    public static string Build(IConfiguration config)
    {
        var connectionString = config["DB:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        var host = config["DB:Host"] ?? throw new InvalidOperationException("DB:Host not configured");
        var port = config["DB:Port"] ?? "5432";
        var database = config["DB:Database"] ?? throw new InvalidOperationException("DB:Database not configured");
        var user = config["DB:User"] ?? throw new InvalidOperationException("DB:User not configured");
        var password = config["DB:Password"] ?? throw new InvalidOperationException("DB:Password not configured");

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Disable";
    }
}