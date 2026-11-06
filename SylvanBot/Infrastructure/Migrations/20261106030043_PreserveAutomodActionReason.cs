using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveAutomodActionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionReason",
                table: "automod_bans",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionReason",
                table: "automod_bans");
        }
    }
}
