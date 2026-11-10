using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetireTicketImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ticket_operations_command",
                table: "ticket_operations");

            migrationBuilder.DropColumn(
                name: "ImportedFromLegacy",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PinnedPublicationChannelId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "UseOriginalEmbedFormat",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "LegacyChannelId",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "OrderedPublicationPaused",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "OrderedPublicationPending",
                table: "ticket_configurations");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ticket_history",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ticket_history SET "Description" = CASE
                    WHEN "HistoricalText" LIKE E'Legacy current record and import provenance:\n%'
                        THEN 'Imported from the legacy ticket register.'
                    WHEN "HistoricalText" ~ '^Legacy ticket #[0-9]+, history version [0-9]+ \(raw sparse row;'
                        THEN 'Legacy revision ' || substring("HistoricalText" from 'history version ([0-9]+)')
                            || CASE WHEN "Timestamp" IS NULL THEN '' ELSE ' - <t:' || floor(extract(epoch from "Timestamp"))::bigint::text || ':f>' END
                    ELSE "HistoricalText" END
                WHERE "Action" = 'Historical';
                """);

            // Existing restrictions keep an audit cursor even where capture was never configured.
            migrationBuilder.Sql("""
                INSERT INTO ticket_configurations ("GuildId", "Enabled", "CaptureFromUtc", "TrackedRoleIds", "ReminderIntervalSeconds")
                SELECT "GuildId", false, min("CreatedAtUtc"), ARRAY[]::bigint[], 86400
                FROM tickets WHERE "Status" = 'Active'
                GROUP BY "GuildId"
                ON CONFLICT ("GuildId") DO UPDATE
                SET "CaptureFromUtc" = COALESCE(ticket_configurations."CaptureFromUtc", EXCLUDED."CaptureFromUtc");
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ticket_operations_command",
                table: "ticket_operations",
                sql: "(\"Kind\" IN ('RemoveBan', 'RemoveRole') OR (\"Kind\" = 'SetTimeoutExpiry' AND \"ExpiryUtc\" IS NOT NULL)) AND \"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restore the pre-migration backup to recover retired import configuration.");
        }
    }
}
