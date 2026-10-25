using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticModerationTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuditReason",
                table: "tickets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Completion",
                table: "tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DurationKnown",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExpirySuspended",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "LastAuditId",
                table: "tickets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PendingEditorUserId",
                table: "tickets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingExpiryUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PendingOperationAttempted",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingOperationId",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingRequestedAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedVersion",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceAuditId",
                table: "tickets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SupersededByNumber",
                table: "tickets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticCaptureEnabled",
                table: "ticket_configurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CaptureFromUtc",
                table: "ticket_configurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastAuditId",
                table: "ticket_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderIntervalSeconds",
                table: "ticket_configurations",
                type: "integer",
                nullable: false,
                defaultValue: 86400);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAuditScanAtUtc",
                table: "ticket_configurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ticket_moderators",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ActiveTicketId = table.Column<long>(type: "bigint", nullable: true),
                    ChannelId = table.Column<long>(type: "bigint", nullable: true),
                    PromptMessageId = table.Column<long>(type: "bigint", nullable: true),
                    PromptVersion = table.Column<int>(type: "integer", nullable: true),
                    LastAttemptedVersion = table.Column<int>(type: "integer", nullable: true),
                    NextDeliveryUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastReadMessageId = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_moderators", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SourceAuditId",
                table: "tickets",
                columns: new[] { "GuildId", "SourceAuditId" },
                unique: true,
                filter: "\"SourceAuditId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_Status_ExpiresAtUtc",
                table: "tickets",
                columns: new[] { "GuildId", "Status", "ExpiresAtUtc" },
                filter: "\"SourceAuditId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ModeratorUserId_CreatedAtUtc_Id",
                table: "tickets",
                columns: new[] { "ModeratorUserId", "CreatedAtUtc", "Id" },
                filter: "\"Completion\" = 'Pending'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_observed_duration",
                table: "tickets",
                sql: "\"SourceAuditId\" IS NULL OR ((\"DurationKnown\" OR \"DurationSeconds\" IS NULL) AND (\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tickets_pending_operation",
                table: "tickets",
                sql: "(\"PendingOperationId\" IS NULL AND \"PendingRequestedAtUtc\" IS NULL) OR (\"SourceAuditId\" IS NOT NULL AND \"PendingOperationId\" IS NOT NULL AND \"PendingRequestedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ticket_configurations_reminder",
                table: "ticket_configurations",
                sql: "\"ReminderIntervalSeconds\" >= 60");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastAuditScanAtUtc",
                table: "ticket_configurations");

            migrationBuilder.DropTable(
                name: "ticket_moderators");

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_SourceAuditId",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_Status_ExpiresAtUtc",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_ModeratorUserId_CreatedAtUtc_Id",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_observed_duration",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_tickets_pending_operation",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ticket_configurations_reminder",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "AuditReason",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "Completion",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "DurationKnown",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ExpirySuspended",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "LastAuditId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingEditorUserId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingExpiryUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingOperationAttempted",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingOperationId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PendingRequestedAtUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "PublishedVersion",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "SourceAuditId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "SupersededByNumber",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "AutomaticCaptureEnabled",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "CaptureFromUtc",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "LastAuditId",
                table: "ticket_configurations");

            migrationBuilder.DropColumn(
                name: "ReminderIntervalSeconds",
                table: "ticket_configurations");
        }
    }
}
