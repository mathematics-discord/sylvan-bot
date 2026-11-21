using Microsoft.EntityFrameworkCore.Migrations;

namespace SylvanBot.Infrastructure.Migrations;

public sealed partial class AddSealClaimWindow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "expires_at", table: "seal_hunts", type: "timestamp with time zone", nullable: true);

        migrationBuilder.AddColumn<long[]>(
            name: "abandoned", table: "seal_hunts", type: "bigint[]", nullable: false, defaultValue: Array.Empty<long>());
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "expires_at", table: "seal_hunts");
        migrationBuilder.DropColumn(name: "abandoned", table: "seal_hunts");
    }
}
