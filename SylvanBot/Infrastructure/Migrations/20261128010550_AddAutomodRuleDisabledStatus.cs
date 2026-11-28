using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261128010550_AddAutomodRuleDisabledStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "Disabled",
            table: "automod_rules",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Disabled",
            table: "automod_rules");
    }
}
