namespace SylvanBot.Infrastructure.Migrations;

#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

/// <inheritdoc />
public partial class AddTemporaryPingRole : Migration
{
    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TemporaryPingRoleSettings");
    }

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TemporaryPingRoleSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                GuildId = table.Column<long>(type: "bigint", nullable: false),
                RoleId = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TemporaryPingRoleSettings", x => x.Id);
                table.CheckConstraint("CK_TemporaryPingRoleSettings_SingletonId", "\"Id\" = 1");
            }
        );
    }
}
