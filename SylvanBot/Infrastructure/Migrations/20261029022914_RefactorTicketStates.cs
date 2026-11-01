using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorTicketStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep all old payloads until the explicit states and owned records are populated.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE ticket_state_backup ON COMMIT DROP AS SELECT * FROM tickets;
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM tickets WHERE "GuildId" <= 0 OR "SubjectUserId" IS NULL
                        OR "SubjectUserId" <= 0 OR "ModeratorUserId" IS NULL OR "ModeratorUserId" <= 0
                        OR "CreatedAtUtc" IS NULL) THEN
                        RAISE EXCEPTION 'Ticket migration requires real guild, subject, moderator and action timestamp values';
                    END IF;
                    IF EXISTS (SELECT 1 FROM tickets WHERE ("ChannelId" IS NULL) <> ("MessageId" IS NULL)
                        OR ("PreviousChannelId" IS NULL) <> ("PreviousMessageId" IS NULL)) THEN
                        RAISE EXCEPTION 'Ticket publication references require both channel and message IDs';
                    END IF;
                    IF EXISTS (SELECT 1 FROM tickets WHERE "Action" = 'Timeout'
                        AND "DurationSeconds" IS NULL AND "ExpiresAtUtc" IS NULL) THEN
                        RAISE EXCEPTION 'Timeout ticket has neither a duration nor an observed expiry';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_ChannelId_MessageId",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_PreviousChannelId_PreviousMessageId",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_SubjectUserId_Hidden_Id",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_note_duration",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_observed_duration",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_pending_operation",
                table: "tickets");

            migrationBuilder.Sql("""
                UPDATE tickets SET
                    "Comment" = COALESCE("Comment", ''),
                    "AuditReason" = COALESCE("AuditReason", ''),
                    "DurationSeconds" = CASE WHEN "Action" = 'Timeout' AND "DurationSeconds" IS NULL
                        THEN GREATEST(1, CEIL(EXTRACT(EPOCH FROM ("ExpiresAtUtc" - "CreatedAtUtc"))))::bigint
                        ELSE COALESCE("DurationSeconds", 9223372036854775807) END,
                    "Status" = CASE WHEN "Action" = 'Note' THEN 'NotApplicable'
                        WHEN "Action" = 'Kick' THEN 'Applied'
                        WHEN "SupersededByNumber" IS NOT NULL THEN 'Superseded'
                        WHEN "Status" = 'ExpirationFailed' THEN 'Active'
                        ELSE COALESCE("Status", 'Active') END,
                    "Completion" = CASE WHEN "Completion" = 'Automated' THEN 'NotRequired'
                        WHEN length(trim(COALESCE("Comment", ''))) > 0 THEN 'Complete' ELSE 'Pending' END,
                    "LastAuditId" = COALESCE("LastAuditId", "SourceAuditId");
                UPDATE tickets SET "ExpiresAtUtc" = CASE
                    WHEN "DurationSeconds" = 9223372036854775807 THEN 'infinity'::timestamptz
                    ELSE COALESCE("ExpiresAtUtc", "CreatedAtUtc" + "DurationSeconds" * INTERVAL '1 second') END;
                """);

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "DurationKnown",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ExpirySuspended",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "Hidden",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "HidePending",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ModificationHistory",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingEditorUserId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingOperationAttempted",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PublicationPending",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ReviewExempt",
                table: "tickets");

            migrationBuilder.RenameColumn(
                name: "PreviousMessageId",
                table: "tickets",
                newName: "OperationModeratorId");

            migrationBuilder.RenameColumn(
                name: "PreviousChannelId",
                table: "tickets",
                newName: "HideModeratorId");

            migrationBuilder.RenameColumn(
                name: "PendingRequestedAtUtc",
                table: "tickets",
                newName: "OperationRequestedAtUtc");

            migrationBuilder.RenameColumn(
                name: "PendingOperationId",
                table: "tickets",
                newName: "OperationId");

            migrationBuilder.RenameColumn(
                name: "PendingExpiryUtc",
                table: "tickets",
                newName: "OperationExpiryUtc");

            migrationBuilder.AlterColumn<long>(
                name: "SubjectUserId",
                table: "tickets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ModeratorUserId",
                table: "tickets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "DurationSeconds",
                table: "tickets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Completion",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "tickets",
                type: "character varying(3000)",
                maxLength: 3000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(3000)",
                oldMaxLength: 3000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuditReason",
                table: "tickets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpirySchedule",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OperationInitiator",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationKind",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationPurpose",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationState",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PublicationState",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReviewState",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "tickets",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ticket_history",
                columns: table => new
                {
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    HistoricalText = table.Column<string>(type: "text", nullable: true),
                    InitiatorKind = table.Column<string>(type: "text", nullable: true),
                    ModeratorId = table.Column<long>(type: "bigint", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_history", x => new { x.TicketId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_ticket_history_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_publications",
                columns: table => new
                {
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_publications", x => new { x.TicketId, x.ChannelId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_ticket_publications_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                UPDATE tickets AS t SET
                    "Origin" = CASE WHEN b."SourceAuditId" IS NULL THEN 'Manual' ELSE 'Audit' END,
                    "ExpirySchedule" = CASE WHEN b."ExpirySuspended" THEN 'Suspended' ELSE 'Enabled' END,
                    "ReviewState" = CASE
                        WHEN b."Action" NOT IN ('Ban', 'Timeout', 'VoiceMute') THEN 'NotRequired'
                        WHEN b."ReviewExempt" THEN 'Exempt'
                        WHEN b."ApprovedByUserId" IS NOT NULL AND b."ApprovedAtUtc" IS NOT NULL THEN 'Approved'
                        ELSE 'Pending' END,
                    "Visibility" = CASE WHEN b."Hidden" THEN 'Hidden' WHEN b."HidePending" THEN 'Hiding' ELSE 'Visible' END,
                    "HideModeratorId" = CASE WHEN b."HidePending" AND NOT b."Hidden" THEN b."LastModifiedByUserId" END,
                    "OperationState" = CASE WHEN b."PendingOperationId" IS NULL THEN 'None'
                        WHEN b."PendingOperationAttempted" THEN 'AwaitingConfirmation' ELSE 'Prepared' END,
                    "OperationKind" = CASE WHEN b."PendingOperationId" IS NOT NULL THEN
                        CASE WHEN b."Action" = 'Ban' THEN 'RemoveBan'
                            WHEN b."Action" = 'VoiceMute' THEN 'RemoveVoiceMute'
                            WHEN b."PendingExpiryUtc" IS NULL THEN 'RemoveTimeout' ELSE 'SetTimeoutExpiry' END END,
                    "OperationPurpose" = CASE WHEN b."PendingOperationId" IS NOT NULL THEN
                        CASE WHEN b."HidePending" THEN 'Hide' WHEN b."PendingEditorUserId" IS NULL THEN 'Expire' ELSE 'Edit' END END,
                    "OperationInitiator" = CASE WHEN b."PendingOperationId" IS NOT NULL THEN
                        CASE WHEN b."PendingEditorUserId" IS NULL THEN 'System' ELSE 'Moderator' END END,
                    "OperationModeratorId" = b."PendingEditorUserId",
                    "PublicationState" = CASE WHEN b."PublicationPending" THEN 'Uncertain'
                        WHEN b."Hidden" AND (b."MessageId" IS NOT NULL OR b."PreviousMessageId" IS NOT NULL) THEN 'PendingRemoval'
                        WHEN b."Hidden" THEN 'Unpublished'
                        WHEN b."MessageId" IS NULL OR b."ChannelId" IS NULL THEN 'Unpublished'
                        WHEN b."PublishedVersion" = b."Version" THEN 'Published' ELSE 'PendingUpdate' END
                FROM ticket_state_backup AS b WHERE b."Id" = t."Id";

                INSERT INTO ticket_history ("TicketId", "Sequence", "Action", "HistoricalText")
                SELECT b."Id", entry.ordinality::integer, 'Historical', entry.text
                FROM ticket_state_backup AS b, unnest(b."ModificationHistory") WITH ORDINALITY AS entry(text, ordinality);

                INSERT INTO ticket_publications ("TicketId", "ChannelId", "MessageId", "Revision", "State")
                SELECT "Id", "ChannelId", "MessageId", COALESCE("PublishedVersion", -1), 'Current'
                FROM ticket_state_backup WHERE "ChannelId" IS NOT NULL AND "MessageId" IS NOT NULL;
                INSERT INTO ticket_publications ("TicketId", "ChannelId", "MessageId", "Revision", "State")
                SELECT "Id", "PreviousChannelId", "PreviousMessageId", -1, 'Retained'
                FROM ticket_state_backup WHERE "PreviousChannelId" IS NOT NULL AND "PreviousMessageId" IS NOT NULL
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SubjectUserId_Visibility_Id",
                table: "tickets",
                columns: new[] { "GuildId", "SubjectUserId", "Visibility", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_approval",
                table: "tickets",
                sql: "\"ReviewState\" <> 'Approved' OR (\"ApprovedByUserId\" IS NOT NULL AND \"ApprovedByUserId\" > 0 AND \"ApprovedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_audit",
                table: "tickets",
                sql: "\"Origin\" <> 'Audit' OR (\"SourceAuditId\" IS NOT NULL AND \"SourceAuditId\" > 0 AND \"LastAuditId\" IS NOT NULL AND \"LastAuditId\" >= \"SourceAuditId\")");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_completion",
                table: "tickets",
                sql: "\"Completion\" <> 'Complete' OR length(trim(\"Comment\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_duration",
                table: "tickets",
                sql: "\"DurationSeconds\" > 0 AND (\"Action\" <> 'Timeout' OR \"DurationSeconds\" < 9223372036854775807)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_hidden_action",
                table: "tickets",
                sql: "\"Visibility\" <> 'Hidden' OR \"Status\" <> 'Active'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_hiding",
                table: "tickets",
                sql: "\"Visibility\" <> 'Hiding' OR (\"HideModeratorId\" IS NOT NULL AND \"HideModeratorId\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_identity",
                table: "tickets",
                sql: "\"GuildId\" > 0 AND \"SubjectUserId\" > 0 AND \"ModeratorUserId\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_note_duration",
                table: "tickets",
                sql: "\"Action\" <> 'Note' OR \"DurationSeconds\" = 9223372036854775807");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_observed_duration",
                table: "tickets",
                sql: "\"SourceAuditId\" IS NULL OR \"DurationSeconds\" = 9223372036854775807 OR \"DurationSeconds\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_operation_payload",
                table: "tickets",
                sql: "\"OperationState\" = 'None' OR (\"OperationId\" IS NOT NULL AND \"OperationKind\" IS NOT NULL AND \"OperationPurpose\" IS NOT NULL AND \"OperationRequestedAtUtc\" IS NOT NULL AND (\"OperationInitiator\" = 'System' OR (\"OperationInitiator\" = 'Moderator' AND \"OperationModeratorId\" > 0)) AND (\"OperationKind\" <> 'SetTimeoutExpiry' OR \"OperationExpiryUtc\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_role",
                table: "tickets",
                sql: "\"Action\" <> 'Role' OR (\"RoleId\" IS NOT NULL AND \"RoleId\" > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_supersession",
                table: "tickets",
                sql: "\"Status\" <> 'Superseded' OR (\"SupersededByNumber\" IS NOT NULL AND \"SupersededByNumber\" > 0 AND \"SupersededByNumber\" <> \"Number\")");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_publications_ChannelId_MessageId",
                table: "ticket_publications",
                columns: new[] { "ChannelId", "MessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Structured ticket states and history cannot be safely downgraded. Restore a pre-migration backup instead.");
        }
    }
}
