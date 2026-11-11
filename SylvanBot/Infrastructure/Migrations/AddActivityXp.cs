using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityXp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_awards",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Xp = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_awards", x => new { x.GuildId, x.MessageId });
                    table.CheckConstraint("CK_activity_award", "\"Xp\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "activity_members",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Xp = table.Column<decimal>(type: "numeric", nullable: false),
                    OptedOut = table.Column<bool>(type: "boolean", nullable: false),
                    ResetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignedRoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_members", x => new { x.GuildId, x.UserId });
                    table.CheckConstraint("CK_activity_xp", "\"Xp\" >= 0 AND (NOT \"OptedOut\" OR \"Xp\" = 0)");
                });

            CreateSettings(migrationBuilder);
        }

        private static void CreateSettings(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_settings",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Role1Id = table.Column<long>(type: "bigint", nullable: false),
                    Role2Id = table.Column<long>(type: "bigint", nullable: false),
                    Role1Score = table.Column<long>(type: "bigint", nullable: false),
                    Role2Score = table.Column<long>(type: "bigint", nullable: false),
                    MaxDaily = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_settings", x => x.GuildId);
                    table.CheckConstraint("CK_activity_cap", "\"MaxDaily\" >= 0");
                    table.CheckConstraint("CK_activity_scores", "(\"Role1Id\" = 0 AND \"Role2Id\" = 0 AND \"Role1Score\" = 0 AND \"Role2Score\" = 0) OR (\"Role1Id\" <> 0 AND \"Role2Id\" <> 0 AND \"Role1Id\" <> \"Role2Id\" AND \"Role1Score\" > \"Role2Score\" AND \"Role2Score\" > 1000)");
                });

            migrationBuilder.CreateTable(
                name: "activity_weights",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_weights", x => new { x.GuildId, x.ChannelId });
                    table.CheckConstraint("CK_activity_weight", "\"Weight\" >= 0 AND \"Weight\" <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_awards_AwardedAt",
                table: "activity_awards",
                column: "AwardedAt");

            migrationBuilder.CreateIndex(
                name: "IX_activity_awards_GuildId_UserId_AwardedAt",
                table: "activity_awards",
                columns: new[] { "GuildId", "UserId", "AwardedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_awards");

            migrationBuilder.DropTable(
                name: "activity_members");

            migrationBuilder.DropTable(
                name: "activity_settings");

            migrationBuilder.DropTable(
                name: "activity_weights");
        }
    }
}
