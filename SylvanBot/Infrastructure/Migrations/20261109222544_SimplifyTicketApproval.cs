using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyTicketApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_approval",
                table: "tickets");

            migrationBuilder.Sql("""
                INSERT INTO ticket_history ("TicketId", "Sequence", "Action", "HistoricalText")
                SELECT t."Id", COALESCE((SELECT MAX(h."Sequence") FROM ticket_history h WHERE h."TicketId" = t."Id"), 0) + 1,
                    'Historical', 'Prior review acceptance preserved when configurable ticket exemptions were retired.'
                FROM tickets t WHERE t."ReviewExempt" AND t."ApprovedAtUtc" IS NULL AND t."Action" <> 'Note';
                INSERT INTO ticket_history ("TicketId", "Sequence", "Action", "HistoricalText")
                SELECT t."Id", COALESCE((SELECT MAX(h."Sequence") FROM ticket_history h WHERE h."TicketId" = t."Id"), 0) + 1,
                    'Historical', 'Automatic voice removal retired. Check the live voice restriction; its saved status is unchanged.'
                FROM tickets t JOIN ticket_operations o ON o."TicketId" = t."Id"
                WHERE t."Action" IN ('VoiceMute', 'VoiceDeafen');
                UPDATE tickets SET "Version" = "Version" + 1
                WHERE "Action" IN ('VoiceMute', 'VoiceDeafen') AND "Id" IN (SELECT "TicketId" FROM ticket_operations);
                DELETE FROM ticket_operations WHERE "TicketId" IN
                    (SELECT "Id" FROM tickets WHERE "Action" IN ('VoiceMute', 'VoiceDeafen'));
                UPDATE tickets SET "ApprovedAtUtc" = CURRENT_TIMESTAMP, "Version" = "Version" + 1
                WHERE "ReviewExempt" AND "ApprovedAtUtc" IS NULL AND "Action" <> 'Note';
                """);

            migrationBuilder.DropColumn(
                name: "ReviewExempt",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "LastAttemptedVersion",
                table: "ticket_moderators");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "ticket_moderators");

            migrationBuilder.DropColumn(
                name: "ReviewExemptRoleIds",
                table: "ticket_configurations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_approval",
                table: "tickets",
                sql: "\"ApprovedByUserId\" IS NULL OR \"ApprovedAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore the pre-migration backup to recover retired exemption configuration.");
        }
    }
}
