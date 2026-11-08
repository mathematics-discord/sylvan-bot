using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketReviewSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ReviewSummaryChannelId",
                table: "ticket_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReviewSummaryMessageId",
                table: "ticket_configurations",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewSummaryChannelId",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "ReviewSummaryMessageId",
                table: "ticket_configurations");
        }
    }
}
