using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReduceTicketDeliveryWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApprovalReactionCleanupPending",
                table: "ticket_publications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Format",
                table: "ticket_publications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PromptRevision",
                table: "ticket_moderators",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalReactionCleanupPending",
                table: "ticket_publications");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "ticket_publications");

            migrationBuilder.DropColumn(
                name: "PromptRevision",
                table: "ticket_moderators");
        }
    }
}
