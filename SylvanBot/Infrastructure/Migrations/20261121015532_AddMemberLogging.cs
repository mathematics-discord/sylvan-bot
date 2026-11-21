using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261121015532_AddMemberLogging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "member_log_channel_id",
            table: "logging_configurations",
            type: "bigint",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "member_log_channel_id",
            table: "logging_configurations");
    }
}
