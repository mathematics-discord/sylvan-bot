namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class UpdateEmojiTableAgain : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "EmojiUsageCounts"
                ALTER COLUMN "EmojiId" TYPE character varying(128)
                USING "EmojiId"::text;
                """
            );
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "EmojiUsageCounts"
                WHERE "EmojiId" !~ '^[0-9]+$'
                   OR length("EmojiId") > 19
                   OR (
                       length("EmojiId") = 19
                       AND "EmojiId" > '9223372036854775807'
                   );
                """
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE "EmojiUsageCounts"
                ALTER COLUMN "EmojiId" TYPE bigint
                USING "EmojiId"::bigint;
                """
            );
        }
    }
}
