namespace RatBot.Infrastructure.Data;

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
        string? connectionString = config["DB:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        string host = config["DB:Host"] ?? throw new InvalidOperationException("DB:Host not configured");
        string port = config["DB:Port"] ?? "5432";
        string database = config["DB:Database"] ?? throw new InvalidOperationException("DB:Database not configured");
        string user = config["DB:User"] ?? throw new InvalidOperationException("DB:User not configured");
        string password = config["DB:Password"] ?? throw new InvalidOperationException("DB:Password not configured");

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Disable";
    }
}