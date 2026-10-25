using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyTicketEnabledState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enabled is authoritative. Keep existing tickets and audit progress,
            // and start newly enabled capture at this migration rather than importing history.
            migrationBuilder.Sql("""
                UPDATE ticket_configurations
                SET "Enabled" = FALSE
                WHERE "Enabled" AND "DestinationChannelId" IS NULL;

                UPDATE ticket_configurations
                SET "CaptureFromUtc" = CURRENT_TIMESTAMP
                WHERE "Enabled" AND (NOT "AutomaticCaptureEnabled" OR "CaptureFromUtc" IS NULL);
                """);

            migrationBuilder.DropColumn(
                name: "AutomaticCaptureEnabled",
                table: "ticket_configurations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ticket_configurations_enabled_channel",
                table: "ticket_configurations",
                sql: "NOT \"Enabled\" OR \"DestinationChannelId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ticket_configurations_enabled_channel",
                table: "ticket_configurations");

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticCaptureEnabled",
                table: "ticket_configurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE ticket_configurations
                SET "AutomaticCaptureEnabled" = "Enabled";
                """);
        }
    }
}
