using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DirectTicketStrategies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM tickets WHERE "OperationState" IN ('Prepared', 'AwaitingConfirmation')
                        AND "OperationPurpose" = 'Hide') THEN
                        RAISE EXCEPTION 'Resolve pending hide reversals with the previous bot before migrating tickets';
                    END IF;
                    IF EXISTS (SELECT 1 FROM tickets WHERE "OperationState" IN ('Prepared', 'AwaitingConfirmation')
                        AND "OperationKind" NOT IN ('SetTimeoutExpiry', 'RemoveBan')) THEN
                        RAISE EXCEPTION 'Resolve unsupported pending moderation operations before migrating tickets';
                    END IF;
                END $$;
                CREATE TEMP TABLE ticket_direct_backup ON COMMIT DROP AS SELECT * FROM tickets;
                CREATE TEMP TABLE ticket_direct_publications ON COMMIT DROP AS SELECT * FROM ticket_publications;
                """);

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_Status_ExpiresAtUtc",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_SubjectUserId_Visibility_Id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_ModeratorUserId_CreatedAtUtc_Id",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_approval",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_audit",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_completion",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_duration",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_hidden_action",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_hiding",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_note_duration",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_observed_duration",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_operation_payload",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_role",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_supersession",
                table: "tickets");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<long>(
                name: "DurationSeconds",
                table: "tickets",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<bool>(
                name: "ExpirySuspended",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Hidden",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReminderExempt",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewExempt",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SendUncertain",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Current",
                table: "ticket_publications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Removed",
                table: "ticket_publications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ticket_operations",
                columns: table => new
                {
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ExpiryUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedBy = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_operations", x => x.TicketId);
                    table.CheckConstraint("CK_ticket_operations_command", "(\"Kind\" = 'RemoveBan' OR (\"Kind\" = 'SetTimeoutExpiry' AND \"ExpiryUtc\" IS NOT NULL)) AND \"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_ticket_operations_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Derive policy and copy unfinished work before obsolete payloads disappear.
            migrationBuilder.Sql("""
                UPDATE tickets AS t SET
                    "Hidden" = old."Visibility" = 'Hidden',
                    "ReviewExempt" = old."ReviewState" IN ('Exempt', 'NotRequired'),
                    "ReminderExempt" = old."Completion" = 'NotRequired',
                    "ExpirySuspended" = old."ExpirySchedule" = 'Suspended',
                    "SendUncertain" = old."PublicationState" IN ('PendingSend', 'Uncertain'),
                    "DurationSeconds" = NULLIF(old."DurationSeconds", 9223372036854775807),
                    "ExpiresAtUtc" = CASE WHEN old."DurationSeconds" = 9223372036854775807 THEN NULL ELSE old."ExpiresAtUtc" END
                FROM ticket_direct_backup AS old WHERE old."Id" = t."Id";
                INSERT INTO ticket_operations ("TicketId", "Id", "Kind", "ExpiryUtc", "RequestedBy", "RequestedAtUtc", "Attempted")
                    SELECT "Id", "OperationId", "OperationKind", "OperationExpiryUtc", "OperationModeratorId",
                        "OperationRequestedAtUtc", "OperationState" = 'AwaitingConfirmation'
                    FROM ticket_direct_backup WHERE "OperationState" IN ('Prepared', 'AwaitingConfirmation');
                UPDATE ticket_publications AS p SET
                    "Current" = old."State" = 'Current', "Removed" = old."State" = 'Removed',
                    "Revision" = CASE WHEN old."State" = 'Current' THEN -1 ELSE p."Revision" END
                FROM ticket_direct_publications AS old WHERE old."TicketId" = p."TicketId"
                    AND old."ChannelId" = p."ChannelId" AND old."MessageId" = p."MessageId";
                """);

            migrationBuilder.DropColumn(
                name: "Completion",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ExpirySchedule",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "HideModeratorId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationExpiryUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationInitiator",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationKind",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationModeratorId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationPurpose",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationRequestedAtUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "OperationState",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PublicationState",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ReviewState",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "State",
                table: "ticket_publications");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_Status_ExpiresAtUtc",
                table: "tickets",
                columns: new[] { "GuildId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SubjectUserId_Hidden_Id",
                table: "tickets",
                columns: new[] { "GuildId", "SubjectUserId", "Hidden", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ModeratorUserId_CreatedAtUtc_Id",
                table: "tickets",
                columns: new[] { "ModeratorUserId", "CreatedAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_approval",
                table: "tickets",
                sql: "(\"ApprovedByUserId\" IS NULL) = (\"ApprovedAtUtc\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_audit",
                table: "tickets",
                sql: "\"SourceAuditId\" IS NULL OR (\"SourceAuditId\" > 0 AND \"LastAuditId\" IS NOT NULL AND \"LastAuditId\" >= \"SourceAuditId\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_duration",
                table: "tickets",
                sql: "(\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0) AND (\"Action\" <> 'Timeout' OR (\"DurationSeconds\" IS NOT NULL AND \"ExpiresAtUtc\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_note_duration",
                table: "tickets",
                sql: "\"Action\" <> 'Note' OR \"DurationSeconds\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_role",
                table: "tickets",
                sql: "\"Action\" <> 'Role' OR (\"RoleId\" IS NOT NULL AND \"RoleId\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_supersession",
                table: "tickets",
                sql: "\"Status\" <> 'Superseded' OR (\"SupersededByNumber\" IS NOT NULL AND \"SupersededByNumber\" > 0 AND \"SupersededByNumber\" <> \"Number\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("The visibility-only ticket model requires a forward migration; restore a backup to run the old model.");
    }
}
