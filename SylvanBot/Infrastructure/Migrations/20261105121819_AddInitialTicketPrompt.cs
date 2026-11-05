using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialTicketPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InitialPromptPending",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialPromptPending",
                table: "tickets");
        }
    }
}
