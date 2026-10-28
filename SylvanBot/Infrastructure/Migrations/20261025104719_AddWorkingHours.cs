using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "working_hours",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    MemberId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: true),
                    EndTime = table.Column<string>(type: "text", nullable: true),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    ZoneId = table.Column<string>(type: "text", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_working_hours", x => new { x.GuildId, x.MemberId });
                    table.CheckConstraint("CK_working_hours_identity", "\"GuildId\" <> 0 AND \"MemberId\" <> 0");
                    table.CheckConstraint(
                        "CK_working_hours_window",
                        "(\"StartTime\" IS NULL AND \"EndTime\" IS NULL AND \"OffsetMinutes\" IS NULL AND \"ZoneId\" IS NULL AND NOT \"Enabled\") OR (\"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND ((\"OffsetMinutes\" IS NOT NULL AND \"OffsetMinutes\" BETWEEN -1439 AND 1439 AND \"ZoneId\" IS NULL) OR (\"OffsetMinutes\" IS NULL AND \"ZoneId\" IS NOT NULL AND length(\"ZoneId\") > 0)))"
                    );
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "working_hours");
        }
    }
}
