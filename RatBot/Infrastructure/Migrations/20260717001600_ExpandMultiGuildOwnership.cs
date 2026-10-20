namespace RatBot.Infrastructure.Migrations;

#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

public partial class ExpandMultiGuildOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "RoleColourOptions" ADD COLUMN IF NOT EXISTS "GuildId" bigint NULL;
            ALTER TABLE "MemberColourPreferences" ADD COLUMN IF NOT EXISTS "GuildId" bigint NULL;
            ALTER TABLE "ImageSpamSettings" ADD COLUMN IF NOT EXISTS "GuildId" bigint NULL;
            ALTER TABLE "ImageSpamSettings" ADD COLUMN IF NOT EXISTS "IsEnabled" boolean NULL;
            ALTER TABLE "EmojiUsageCounts" ADD COLUMN IF NOT EXISTS "GuildId" bigint NULL;
            ALTER TABLE "AdventureForumThreadLinks" ADD COLUMN IF NOT EXISTS "GuildId" bigint NULL;
            ALTER TABLE message_log_entries ADD COLUMN IF NOT EXISTS guild_id bigint NULL;
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE message_log_entries DROP COLUMN IF EXISTS guild_id;
            ALTER TABLE "AdventureForumThreadLinks" DROP COLUMN IF EXISTS "GuildId";
            ALTER TABLE "EmojiUsageCounts" DROP COLUMN IF EXISTS "GuildId";
            ALTER TABLE "ImageSpamSettings" DROP COLUMN IF EXISTS "IsEnabled";
            ALTER TABLE "ImageSpamSettings" DROP COLUMN IF EXISTS "GuildId";
            ALTER TABLE "MemberColourPreferences" DROP COLUMN IF EXISTS "GuildId";
            ALTER TABLE "RoleColourOptions" DROP COLUMN IF EXISTS "GuildId";
            """
        );
    }
}
