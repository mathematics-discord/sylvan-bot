using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingHoursSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "working_hours_settings",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PingRoleId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_working_hours_settings", x => x.GuildId);
                    table.CheckConstraint("CK_working_hours_settings_enabled", "NOT \"Enabled\" OR \"PingRoleId\" IS NOT NULL");
                    table.CheckConstraint("CK_working_hours_settings_identity", "\"GuildId\" <> 0");
                    table.CheckConstraint("CK_working_hours_settings_target", "\"PingRoleId\" IS NULL OR (\"PingRoleId\" <> 0 AND \"PingRoleId\" <> \"GuildId\")");
                });

            migrationBuilder.CreateTable(
                name: "working_hours_eligible_roles",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    IsExempt = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_working_hours_eligible_roles", x => new { x.GuildId, x.RoleId });
                    table.CheckConstraint("CK_working_hours_eligible_roles_identity", "\"RoleId\" <> 0 AND \"RoleId\" <> \"GuildId\"");
                    table.ForeignKey(
                        name: "FK_working_hours_eligible_roles_working_hours_settings_GuildId",
                        column: x => x.GuildId,
                        principalTable: "working_hours_settings",
                        principalColumn: "GuildId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "working_hours_eligible_roles");

            migrationBuilder.DropTable(
                name: "working_hours_settings");
        }
    }
}
