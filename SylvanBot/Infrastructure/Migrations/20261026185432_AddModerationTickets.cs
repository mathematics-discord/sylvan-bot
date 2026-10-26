using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ticket_numbers");

            migrationBuilder.CreateTable(
                name: "ticket_configurations",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DestinationChannelId = table.Column<long>(type: "bigint", nullable: true),
                    CaptureFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAuditId = table.Column<long>(type: "bigint", nullable: true),
                    LastAuditScanAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReminderIntervalSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 86400)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_configurations", x => x.GuildId);
                    table.CheckConstraint("CK_ticket_configurations_enabled_channel", "NOT \"Enabled\" OR \"DestinationChannelId\" IS NOT NULL");
                    table.CheckConstraint("CK_ticket_configurations_reminder", "\"ReminderIntervalSeconds\" >= 60");
                });

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

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SubjectUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModeratorUserId = table.Column<long>(type: "bigint", nullable: true),
                    Comment = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    RoleId = table.Column<long>(type: "bigint", nullable: true),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: true),
                    MessageId = table.Column<long>(type: "bigint", nullable: true),
                    PreviousChannelId = table.Column<long>(type: "bigint", nullable: true),
                    PreviousMessageId = table.Column<long>(type: "bigint", nullable: true),
                    PublicationPending = table.Column<bool>(type: "boolean", nullable: false),
                    LastModifiedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    LastModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SourceAuditId = table.Column<long>(type: "bigint", nullable: true),
                    LastAuditId = table.Column<long>(type: "bigint", nullable: true),
                    AuditReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DurationKnown = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Completion = table.Column<string>(type: "text", nullable: true),
                    SupersededByNumber = table.Column<long>(type: "bigint", nullable: true),
                    PublishedVersion = table.Column<int>(type: "integer", nullable: true),
                    PendingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingExpiryUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingEditorUserId = table.Column<long>(type: "bigint", nullable: true),
                    PendingRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PendingOperationAttempted = table.Column<bool>(type: "boolean", nullable: false),
                    ExpirySuspended = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("CK_tickets_note_duration", "\"Action\" <> 'Note' OR \"DurationSeconds\" IS NULL");
                    table.CheckConstraint("CK_tickets_number", "\"Number\" > 0");
                    table.CheckConstraint("CK_tickets_observed_duration", "\"SourceAuditId\" IS NULL OR ((\"DurationKnown\" OR \"DurationSeconds\" IS NULL) AND (\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0))");
                    table.CheckConstraint("CK_tickets_pending_operation", "(\"PendingOperationId\" IS NULL AND \"PendingRequestedAtUtc\" IS NULL) OR (\"SourceAuditId\" IS NOT NULL AND \"PendingOperationId\" IS NOT NULL AND \"PendingRequestedAtUtc\" IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_ChannelId_MessageId",
                table: "tickets",
                columns: new[] { "GuildId", "ChannelId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_Number",
                table: "tickets",
                columns: new[] { "GuildId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_PreviousChannelId_PreviousMessageId",
                table: "tickets",
                columns: new[] { "GuildId", "PreviousChannelId", "PreviousMessageId" });

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
                name: "IX_tickets_GuildId_SubjectUserId_Hidden_Id",
                table: "tickets",
                columns: new[] { "GuildId", "SubjectUserId", "Hidden", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_ModeratorUserId_CreatedAtUtc_Id",
                table: "tickets",
                columns: new[] { "ModeratorUserId", "CreatedAtUtc", "Id" },
                filter: "\"Completion\" = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_configurations");

            migrationBuilder.DropTable(
                name: "ticket_moderators");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropSequence(
                name: "ticket_numbers");
        }
    }
}
