using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomodPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automod_exempt_roles",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automod_exempt_roles", x => new { x.GuildId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "automod_rules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Pattern = table.Column<string>(type: "text", nullable: false),
                    MatchType = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automod_rules", x => x.Id);
                    table.CheckConstraint("CK_automod_rules_action_duration", "(\"Action\" IN (0, 1) AND \"DurationSeconds\" IS NULL) OR (\"Action\" = 2 AND \"DurationSeconds\" IS NOT NULL AND \"DurationSeconds\" BETWEEN 1 AND 2419200) OR (\"Action\" = 3 AND (\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0))");
                    table.CheckConstraint("CK_automod_rules_match_type", "\"MatchType\" IN (0, 1)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_automod_rules_GuildId",
                table: "automod_rules",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automod_exempt_roles");

            migrationBuilder.DropTable(
                name: "automod_rules");
        }
    }
}
