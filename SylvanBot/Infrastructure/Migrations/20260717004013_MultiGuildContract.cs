namespace SylvanBot.Infrastructure.Migrations;

#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

public partial class MultiGuildContract : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM "RoleColourOptions" WHERE "GuildId" IS NULL) THEN
                    RAISE EXCEPTION 'RoleColourOptions contains rows without GuildId. Run the multi-guild backfill first.';
                END IF;
                IF EXISTS (SELECT 1 FROM "MemberColourPreferences" WHERE "GuildId" IS NULL) THEN
                    RAISE EXCEPTION 'MemberColourPreferences contains rows without GuildId. Run the multi-guild backfill first.';
                END IF;
                IF EXISTS (SELECT 1 FROM "ImageSpamSettings" WHERE "GuildId" IS NULL OR "IsEnabled" IS NULL) THEN
                    RAISE EXCEPTION 'ImageSpamSettings contains rows without GuildId/IsEnabled. Run the multi-guild backfill first.';
                END IF;
                IF EXISTS (SELECT 1 FROM "EmojiUsageCounts" WHERE "GuildId" IS NULL) THEN
                    RAISE EXCEPTION 'EmojiUsageCounts contains rows without GuildId. Run the multi-guild backfill first.';
                END IF;
                IF EXISTS (SELECT 1 FROM "AdventureForumThreadLinks" WHERE "GuildId" IS NULL) THEN
                    RAISE EXCEPTION 'AdventureForumThreadLinks contains rows without GuildId. Run the multi-guild backfill first.';
                END IF;
                IF EXISTS (SELECT 1 FROM message_log_entries WHERE guild_id IS NULL) THEN
                    RAISE EXCEPTION 'message_log_entries contains rows without guild_id. Run the multi-guild backfill first.';
                END IF;
                IF EXISTS (SELECT 1 FROM "TemporaryPingRoleSettings" WHERE "GuildId" IS NULL) THEN
                    RAISE EXCEPTION 'TemporaryPingRoleSettings contains rows without GuildId.';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM "MemberColourPreferences" p
                    JOIN "RoleColourOptions" o ON o."OptionId" = p."SelectedOptionId"
                    WHERE p."SelectedOptionId" IS NOT NULL AND p."GuildId" <> o."GuildId"
                ) THEN
                    RAISE EXCEPTION 'MemberColourPreferences contains a selected option from another guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM "RoleColourOptions" GROUP BY "GuildId", "NormalisedKey" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate role-colour keys exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM "MemberColourPreferences" GROUP BY "GuildId", "UserId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate member colour preferences exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM "ImageSpamSettings" GROUP BY "GuildId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate image spam settings exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM "EmojiUsageCounts" GROUP BY "GuildId", "EmojiId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate emoji usage counts exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM "AdventureForumThreadLinks" GROUP BY "GuildId", "ScorePartIndex" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate adventure thread positions exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM "AdventureLeaderboardMessageState" GROUP BY "GuildId", "Id" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate adventure leaderboard positions exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM observed_messages GROUP BY guild_id, original_message_id HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate observed messages exist within a guild.';
                END IF;
                IF EXISTS (SELECT 1 FROM message_log_entries GROUP BY guild_id, original_message_id, log_message_id HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate message log entries exist within a guild.';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM message_log_entries entry
                    LEFT JOIN observed_messages observed
                        ON observed.guild_id = entry.guild_id
                        AND observed.original_message_id = entry.original_message_id
                    WHERE observed.original_message_id IS NULL
                ) THEN
                    RAISE EXCEPTION 'message_log_entries contains rows without matching observed_messages ownership.';
                END IF;
            END $$;

            ALTER TABLE "MemberColourPreferences" DROP CONSTRAINT IF EXISTS "FK_MemberColourPreferences_RoleColourOptions_SelectedOptionId";
            ALTER TABLE "TemporaryPingRoleSettings" DROP CONSTRAINT IF EXISTS "PK_TemporaryPingRoleSettings";
            ALTER TABLE "TemporaryPingRoleSettings" DROP CONSTRAINT IF EXISTS "CK_TemporaryPingRoleSettings_SingletonId";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_DisplayRoleId";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_NormalisedKey";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_SourceRoleId_DisplayRoleId";
            DROP INDEX IF EXISTS "IX_MemberColourPreferences_SelectedOptionId";
            DROP INDEX IF EXISTS "IX_MemberColourPreferences_UserId";
            ALTER TABLE "ImageSpamSettings" DROP CONSTRAINT IF EXISTS "PK_ImageSpamSettings";
            ALTER TABLE "EmojiUsageCounts" DROP CONSTRAINT IF EXISTS "PK_EmojiUsageCounts";
            ALTER TABLE "AdventureLeaderboardMessageState" DROP CONSTRAINT IF EXISTS "PK_AdventureLeaderboardMessageState";
            ALTER TABLE "AdventureForumThreadLinks" DROP CONSTRAINT IF EXISTS "PK_AdventureForumThreadLinks";
            DROP INDEX IF EXISTS "IX_AdventureForumThreadLinks_ScorePartIndex";
            DROP INDEX IF EXISTS "IX_AdventureForumThreadLinks_ThreadId";
            ALTER TABLE message_log_entries DROP CONSTRAINT IF EXISTS "PK_message_log_entries";
            ALTER TABLE observed_messages DROP CONSTRAINT IF EXISTS "PK_observed_messages";
            DROP INDEX IF EXISTS "IX_message_log_entries_log_message_id";

            ALTER TABLE "RoleColourOptions" ALTER COLUMN "GuildId" SET NOT NULL;
            ALTER TABLE "MemberColourPreferences" ALTER COLUMN "GuildId" SET NOT NULL;
            ALTER TABLE "ImageSpamSettings" ALTER COLUMN "GuildId" SET NOT NULL;
            ALTER TABLE "ImageSpamSettings" ALTER COLUMN "IsEnabled" SET NOT NULL;
            ALTER TABLE "EmojiUsageCounts" ALTER COLUMN "GuildId" SET NOT NULL;
            ALTER TABLE "AdventureForumThreadLinks" ALTER COLUMN "GuildId" SET NOT NULL;
            ALTER TABLE message_log_entries ALTER COLUMN guild_id SET NOT NULL;

            ALTER TABLE "TemporaryPingRoleSettings" DROP COLUMN IF EXISTS "Id";
            ALTER TABLE "ImageSpamSettings" DROP COLUMN IF EXISTS "Id";

            ALTER TABLE "TemporaryPingRoleSettings" ADD CONSTRAINT "PK_TemporaryPingRoleSettings" PRIMARY KEY ("GuildId");
            ALTER TABLE "RoleColourOptions" ADD CONSTRAINT "AK_RoleColourOptions_GuildId_OptionId" UNIQUE ("GuildId", "OptionId");
            ALTER TABLE "ImageSpamSettings" ADD CONSTRAINT "PK_ImageSpamSettings" PRIMARY KEY ("GuildId");
            ALTER TABLE "EmojiUsageCounts" ADD CONSTRAINT "PK_EmojiUsageCounts" PRIMARY KEY ("GuildId", "EmojiId");
            ALTER TABLE "AdventureLeaderboardMessageState" ADD CONSTRAINT "PK_AdventureLeaderboardMessageState" PRIMARY KEY ("GuildId", "Id");
            ALTER TABLE "AdventureForumThreadLinks" ADD CONSTRAINT "PK_AdventureForumThreadLinks" PRIMARY KEY ("GuildId", "ScorePartIndex");
            ALTER TABLE observed_messages ADD CONSTRAINT "PK_observed_messages" PRIMARY KEY (guild_id, original_message_id);
            ALTER TABLE message_log_entries ADD CONSTRAINT "PK_message_log_entries" PRIMARY KEY (guild_id, original_message_id, log_message_id);

            CREATE UNIQUE INDEX "IX_RoleColourOptions_GuildId_DisplayRoleId" ON "RoleColourOptions" ("GuildId", "DisplayRoleId");
            CREATE UNIQUE INDEX "IX_RoleColourOptions_GuildId_NormalisedKey" ON "RoleColourOptions" ("GuildId", "NormalisedKey");
            CREATE UNIQUE INDEX "IX_RoleColourOptions_GuildId_SourceRoleId" ON "RoleColourOptions" ("GuildId", "SourceRoleId");
            CREATE UNIQUE INDEX "IX_RoleColourOptions_GuildId_SourceRoleId_DisplayRoleId" ON "RoleColourOptions" ("GuildId", "SourceRoleId", "DisplayRoleId");
            CREATE INDEX "IX_MemberColourPreferences_GuildId_SelectedOptionId" ON "MemberColourPreferences" ("GuildId", "SelectedOptionId");
            CREATE UNIQUE INDEX "IX_MemberColourPreferences_GuildId_UserId" ON "MemberColourPreferences" ("GuildId", "UserId");
            CREATE UNIQUE INDEX "IX_AdventureLeaderboardMessageState_GuildId_Id" ON "AdventureLeaderboardMessageState" ("GuildId", "Id");
            CREATE UNIQUE INDEX "IX_AdventureForumThreadLinks_GuildId_ScorePartIndex" ON "AdventureForumThreadLinks" ("GuildId", "ScorePartIndex");
            CREATE UNIQUE INDEX "IX_AdventureForumThreadLinks_GuildId_ThreadId" ON "AdventureForumThreadLinks" ("GuildId", "ThreadId");
            CREATE INDEX "IX_message_log_entries_guild_id_log_message_id" ON message_log_entries (guild_id, log_message_id);

            ALTER TABLE message_log_entries
                ADD CONSTRAINT "FK_message_log_entries_observed_messages_guild_id_original_message_id"
                FOREIGN KEY (guild_id, original_message_id)
                REFERENCES observed_messages (guild_id, original_message_id)
                ON DELETE CASCADE;

            ALTER TABLE "MemberColourPreferences"
                ADD CONSTRAINT "FK_MemberColourPreferences_RoleColourOptions_GuildId_SelectedOptionId"
                FOREIGN KEY ("GuildId", "SelectedOptionId")
                REFERENCES "RoleColourOptions" ("GuildId", "OptionId")
                ON DELETE RESTRICT;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "MemberColourPreferences" DROP CONSTRAINT IF EXISTS "FK_MemberColourPreferences_RoleColourOptions_GuildId_SelectedOptionId";
            ALTER TABLE message_log_entries DROP CONSTRAINT IF EXISTS "FK_message_log_entries_observed_messages_guild_id_original_message_id";
            ALTER TABLE "TemporaryPingRoleSettings" DROP CONSTRAINT IF EXISTS "PK_TemporaryPingRoleSettings";
            ALTER TABLE "RoleColourOptions" DROP CONSTRAINT IF EXISTS "AK_RoleColourOptions_GuildId_OptionId";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_GuildId_DisplayRoleId";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_GuildId_NormalisedKey";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_GuildId_SourceRoleId";
            DROP INDEX IF EXISTS "IX_RoleColourOptions_GuildId_SourceRoleId_DisplayRoleId";
            DROP INDEX IF EXISTS "IX_MemberColourPreferences_GuildId_SelectedOptionId";
            DROP INDEX IF EXISTS "IX_MemberColourPreferences_GuildId_UserId";
            ALTER TABLE "ImageSpamSettings" DROP CONSTRAINT IF EXISTS "PK_ImageSpamSettings";
            ALTER TABLE "EmojiUsageCounts" DROP CONSTRAINT IF EXISTS "PK_EmojiUsageCounts";
            ALTER TABLE "AdventureLeaderboardMessageState" DROP CONSTRAINT IF EXISTS "PK_AdventureLeaderboardMessageState";
            DROP INDEX IF EXISTS "IX_AdventureLeaderboardMessageState_GuildId_Id";
            ALTER TABLE "AdventureForumThreadLinks" DROP CONSTRAINT IF EXISTS "PK_AdventureForumThreadLinks";
            DROP INDEX IF EXISTS "IX_AdventureForumThreadLinks_GuildId_ScorePartIndex";
            DROP INDEX IF EXISTS "IX_AdventureForumThreadLinks_GuildId_ThreadId";
            ALTER TABLE message_log_entries DROP CONSTRAINT IF EXISTS "PK_message_log_entries";
            ALTER TABLE observed_messages DROP CONSTRAINT IF EXISTS "PK_observed_messages";
            DROP INDEX IF EXISTS "IX_message_log_entries_guild_id_log_message_id";

            ALTER TABLE "TemporaryPingRoleSettings" ADD COLUMN IF NOT EXISTS "Id" integer NOT NULL DEFAULT 1;
            ALTER TABLE "ImageSpamSettings" ADD COLUMN IF NOT EXISTS "Id" integer NOT NULL DEFAULT 1;
            ALTER TABLE "TemporaryPingRoleSettings" ADD CONSTRAINT "PK_TemporaryPingRoleSettings" PRIMARY KEY ("Id");
            ALTER TABLE "ImageSpamSettings" ADD CONSTRAINT "PK_ImageSpamSettings" PRIMARY KEY ("Id");
            ALTER TABLE "EmojiUsageCounts" ADD CONSTRAINT "PK_EmojiUsageCounts" PRIMARY KEY ("EmojiId");
            ALTER TABLE "AdventureLeaderboardMessageState" ADD CONSTRAINT "PK_AdventureLeaderboardMessageState" PRIMARY KEY ("Id");
            ALTER TABLE "AdventureForumThreadLinks" ADD CONSTRAINT "PK_AdventureForumThreadLinks" PRIMARY KEY ("ScorePartIndex");
            ALTER TABLE observed_messages ADD CONSTRAINT "PK_observed_messages" PRIMARY KEY (original_message_id);
            ALTER TABLE message_log_entries ADD CONSTRAINT "PK_message_log_entries" PRIMARY KEY (original_message_id, log_message_id);
            ALTER TABLE "TemporaryPingRoleSettings" ADD CONSTRAINT "CK_TemporaryPingRoleSettings_SingletonId" CHECK ("Id" = 1);
            CREATE UNIQUE INDEX "IX_RoleColourOptions_DisplayRoleId" ON "RoleColourOptions" ("DisplayRoleId");
            CREATE UNIQUE INDEX "IX_RoleColourOptions_NormalisedKey" ON "RoleColourOptions" ("NormalisedKey");
            CREATE UNIQUE INDEX "IX_RoleColourOptions_SourceRoleId_DisplayRoleId" ON "RoleColourOptions" ("SourceRoleId", "DisplayRoleId");
            CREATE UNIQUE INDEX "IX_MemberColourPreferences_UserId" ON "MemberColourPreferences" ("UserId");
            CREATE UNIQUE INDEX "IX_AdventureForumThreadLinks_ScorePartIndex" ON "AdventureForumThreadLinks" ("ScorePartIndex");
            CREATE UNIQUE INDEX "IX_AdventureForumThreadLinks_ThreadId" ON "AdventureForumThreadLinks" ("ThreadId");
            CREATE INDEX "IX_message_log_entries_log_message_id" ON message_log_entries (log_message_id);
            ALTER TABLE "MemberColourPreferences"
                ADD CONSTRAINT "FK_MemberColourPreferences_RoleColourOptions_SelectedOptionId"
                FOREIGN KEY ("SelectedOptionId") REFERENCES "RoleColourOptions" ("OptionId") ON DELETE RESTRICT;
            """
        );
    }
}
