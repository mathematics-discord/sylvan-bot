using Microsoft.EntityFrameworkCore;
using Npgsql;
using RatBot.Features.EmojiAnalytics;
using RatBot.Infrastructure.Data;

namespace RatBot.Infrastructure.Tests.Integration;

[TestFixture]
public sealed class MultiGuildMigrationBackfillTests
{
    private const string ExpandMigration = "20260717001600_ExpandMultiGuildOwnership";
    private const ulong LegacyGuildId = 123456789012345678;

    [Test]
    public async Task RemoveMeta_PopulatedTables_RemovesMetaAndPreservesEmojiUsage()
    {
        // Arrange
        string databaseName = await CreateDatabaseAsync()
            .ConfigureAwait(false);

        try
        {
            await using BotDbContext db = CreateDbContext(databaseName);
            await db.Database.MigrateAsync("20261011203313_RemoveAutoban")
                .ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "MetaSuggestionSettings"
                    ("GuildId", "CabinetChairRoleId", "CabinetRoleId", "CommitteeRoleId",
                     "ProposalsForumChannelId", "SuggestionsForumChannelId")
                VALUES (1, 2, 3, 4, 5, 6);

                INSERT INTO "MetaProposalStates"
                    ("Id", "GuildId", "OriginalThreadAuthorUserId", "SuggestionThreadChannelId",
                     "SuggestionsForumChannelId", "Status", "TrackedAtUtc", "FailedPollAttempts",
                     "PollFinalizationRetries", "PublicationRetryFailures")
                VALUES ('11111111-1111-1111-1111-111111111111', 1, 7, 8, 6, 'Tracked', now(), 0, 0, 0);

                INSERT INTO "EmojiUsageCounts" ("GuildId", "EmojiId", "MessageUsageCount", "ReactionUsageCount")
                VALUES (1, 500, 7, 9);
                """)
                .ConfigureAwait(false);

            // Act
            await db.Database.MigrateAsync()
                .ConfigureAwait(false);

            // Assert
            int remainingMetaTables = await db.Database.SqlQueryRaw<int>(
                    """
                    SELECT COUNT(*)::integer AS "Value" FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_name IN ('MetaProposalStates', 'MetaSuggestionSettings')
                    """)
                .SingleAsync()
                .ConfigureAwait(false);
            remainingMetaTables.ShouldBe(0);

            EmojiUsageCount persisted = await db.EmojiUsageCounts
                .AsNoTracking()
                .SingleAsync(row => row.GuildId == 1 && row.EmojiId == 500)
                .ConfigureAwait(false);
            persisted.MessageUsageCount.ShouldBe(7);
            persisted.ReactionUsageCount.ShouldBe(9);
        }
        finally
        {
            await DropDatabaseAsync(databaseName)
                .ConfigureAwait(false);
        }
    }

    [Test]
    public async Task BackfillScript_PreservesLegacyRowsAndAllowsContractMigration()
    {
        string databaseName = await CreateDatabaseAsync();

        try
        {
            await using BotDbContext db = CreateDbContext(databaseName);
            await db.Database.MigrateAsync(ExpandMigration);
            await SeedRepresentativeLegacyRowsAsync(db);

            await db.Database.ExecuteSqlRawAsync(CreateBackfillSql(LegacyGuildId));

            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"RoleColourOptions\"")
                .SingleAsync()).ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"MemberColourPreferences\"")
                .SingleAsync()).ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"ImageSpamSettings\"")
                .SingleAsync()).ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"EmojiUsageCounts\"").SingleAsync())
                .ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"AdventureForumThreadLinks\"")
                .SingleAsync()).ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM message_log_entries").SingleAsync())
                .ShouldBe(1);

            (await db.Database.SqlQueryRaw<long>("SELECT \"GuildId\" AS \"Value\" FROM \"RoleColourOptions\"")
                .SingleAsync()).ShouldBe(
                (long)LegacyGuildId
            );
            (await db.Database.SqlQueryRaw<long>("SELECT \"GuildId\" AS \"Value\" FROM \"MemberColourPreferences\"")
                .SingleAsync()).ShouldBe(
                (long)LegacyGuildId
            );
            (await db.Database.SqlQueryRaw<bool>("SELECT \"IsEnabled\" AS \"Value\" FROM \"ImageSpamSettings\"")
                .SingleAsync()).ShouldBeTrue();
            (await db.Database.SqlQueryRaw<long>("SELECT guild_id AS \"Value\" FROM message_log_entries").SingleAsync())
                .ShouldBe(
                    (long)LegacyGuildId
                );

            await db.Database.MigrateAsync("20260719054819_RemoveMessageLogEntryObservedMessageForeignKey");
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "AdventureLeaderboardMessageState" ("GuildId", "Id", "ChannelId", "MessageId", "Year", "LastRenderHash")
                VALUES ({0}, 1, 400, 600, 2026, 'legacy');
                INSERT INTO "AdventureSettings" ("GuildId", "AdventurerRoleId")
                VALUES ({0}, 700);
                """,
                (long)LegacyGuildId
            );

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO user_timezones (user_id, timezone_id, updated_at_utc) VALUES (100, 'Europe/London', now())"
            );
            await db.Database.MigrateAsync();

            (
                await db
                    .Database.SqlQueryRaw<int>(
                        """
                        SELECT COUNT(*)::integer AS "Value" FROM information_schema.tables
                        WHERE table_schema = 'public'
                          AND table_name IN ('AdventureForumThreadLinks', 'AdventureLeaderboardMessageState', 'AdventureSettings', 'user_timezones')
                        """
                    )
                    .SingleAsync()
            ).ShouldBe(0);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"RoleColourOptions\"")
                .SingleAsync()).ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"EmojiUsageCounts\"").SingleAsync())
                .ShouldBe(1);
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM message_log_entries").SingleAsync())
                .ShouldBe(1);

            (
                await db
                    .Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*)::integer AS \"Value\" FROM information_schema.tables WHERE table_name = 'MemberColourPreferences'"
                    )
                    .SingleAsync()
            ).ShouldBe(0);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Test]
    public async Task ContractMigration_FailsLoudlyWhenBackfillWasSkipped()
    {
        string databaseName = await CreateDatabaseAsync();

        try
        {
            await using BotDbContext db = CreateDbContext(databaseName);
            await db.Database.MigrateAsync(ExpandMigration);
            await SeedRepresentativeLegacyRowsAsync(db);

            Exception exception = await Should.ThrowAsync<Exception>(() => db.Database.MigrateAsync());
            exception.ToString().ShouldContain("Run the multi-guild backfill first");
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private static async Task SeedRepresentativeLegacyRowsAsync(BotDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "RoleColourOptions" ("OptionId", "Key", "NormalisedKey", "Label", "SourceRoleId", "DisplayRoleId", "IsEnabled", "CreatedAtUtc", "UpdatedAtUtc", "GuildId")
            VALUES ('11111111-1111-1111-1111-111111111111', 'red', 'red', 'Red', 10, 20, true, now(), now(), NULL);

            INSERT INTO "MemberColourPreferences" ("PreferenceId", "UserId", "Kind", "SelectedOptionId", "GuildId")
            VALUES ('22222222-2222-2222-2222-222222222222', 100, 1, '11111111-1111-1111-1111-111111111111', NULL);

            INSERT INTO "ImageSpamSettings" ("Id", "RequiredChannelCount", "RequiredAttachmentCount", "BurstDurationSeconds", "GuildId", "IsEnabled")
            VALUES (1, 4, 2, 45, NULL, NULL);

            INSERT INTO "EmojiUsageCounts" ("EmojiId", "MessageUsageCount", "ReactionUsageCount", "GuildId")
            VALUES (500, 7, 9, NULL);

            INSERT INTO "AdventureForumThreadLinks" ("ScorePartIndex", "ThreadId", "GuildId")
            VALUES (1, 900, NULL);

            INSERT INTO observed_messages (guild_id, original_message_id, channel_id, author_id, observed_at_utc)
            VALUES ({0}, 300, 400, 500, now());

            INSERT INTO message_log_entries (original_message_id, log_message_id, captured_at_utc, guild_id)
            VALUES (300, 301, now(), NULL);
            """,
            (long)LegacyGuildId
        );
    }

    private static string CreateBackfillSql(ulong legacyGuildId)
    {
        string script = File.ReadAllText(FindRepositoryFile("ops/backfill-multi-guild.sql"));
        return script
            .Replace("\\set ON_ERROR_STOP on", string.Empty, StringComparison.Ordinal)
            .Replace(":'legacy_guild_id'::bigint", $"{legacyGuildId}::bigint", StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file {relativePath}.");
    }

    private static BotDbContext CreateDbContext(string databaseName)
    {
        DbContextOptions<BotDbContext> options = new DbContextOptionsBuilder<BotDbContext>()
            .UseNpgsql(CreateConnectionString(databaseName)).Options;

        return new BotDbContext(options);
    }

    private static async Task<string> CreateDatabaseAsync()
    {
        string databaseName = "ratbot_migration_test_" + Guid.NewGuid().ToString("N");
        await using NpgsqlConnection connection = new NpgsqlConnection(CreateConnectionString("postgres"));
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
        await command.ExecuteNonQueryAsync();
        return databaseName;
    }

    private static async Task DropDatabaseAsync(string databaseName)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(CreateConnectionString("postgres"));
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateConnectionString(string databaseName)
    {
        NpgsqlConnectionStringBuilder builder =
            new NpgsqlConnectionStringBuilder(PostgresDatabaseFixture.ConnectionString)
            {
                Database = databaseName
            };

        return builder.ConnectionString;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}