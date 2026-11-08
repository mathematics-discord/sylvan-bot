using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTickets : Migration
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
                    TrackedRoleIds = table.Column<long[]>(type: "bigint[]", nullable: false),
                    ReviewExemptRoleIds = table.Column<long[]>(type: "bigint[]", nullable: false),
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
                    SubjectUserId = table.Column<long>(type: "bigint", nullable: false),
                    ModeratorUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedTimeoutUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeoutCheckAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Hidden = table.Column<bool>(type: "boolean", nullable: false),
                    ReviewExempt = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderExempt = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceAuditId = table.Column<long>(type: "bigint", nullable: true),
                    LastAuditId = table.Column<long>(type: "bigint", nullable: true),
                    AuditReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: true),
                    SupersededByNumber = table.Column<long>(type: "bigint", nullable: true),
                    ExpirySuspended = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SendUncertain = table.Column<bool>(type: "boolean", nullable: false),
                    InitialPromptPending = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("CK_tickets_approval", "(\"ApprovedByUserId\" IS NULL) = (\"ApprovedAtUtc\" IS NULL)");
                    table.CheckConstraint("CK_tickets_audit", "\"SourceAuditId\" IS NULL OR (\"SourceAuditId\" > 0 AND \"LastAuditId\" IS NOT NULL AND \"LastAuditId\" >= \"SourceAuditId\")");
                    table.CheckConstraint("CK_tickets_duration", "(\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0) AND (\"Action\" <> 'Timeout' OR (\"DurationSeconds\" IS NOT NULL AND \"ExpiresAtUtc\" IS NOT NULL))");
                    table.CheckConstraint("CK_tickets_identity", "\"GuildId\" > 0 AND \"SubjectUserId\" > 0 AND \"ModeratorUserId\" > 0");
                    table.CheckConstraint("CK_tickets_note_duration", "\"Action\" <> 'Note' OR \"DurationSeconds\" IS NULL");
                    table.CheckConstraint("CK_tickets_number", "\"Number\" > 0");
                    table.CheckConstraint("CK_tickets_role", "\"Action\" <> 'Role' OR (\"RoleId\" IS NOT NULL AND \"RoleId\" > 0)");
                    table.CheckConstraint("CK_tickets_supersession", "\"Status\" <> 'Superseded' OR (\"SupersededByNumber\" IS NOT NULL AND \"SupersededByNumber\" > 0 AND \"SupersededByNumber\" <> \"Number\")");
                });

            migrationBuilder.CreateTable(
                name: "ticket_history",
                columns: table => new
                {
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    HistoricalText = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
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
                name: "ticket_operations",
                columns: table => new
                {
                    TicketId = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ExpiryUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NativeExpiryUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedBy = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attempted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_operations", x => x.TicketId);
                    table.CheckConstraint("CK_ticket_operations_command", "(\"Kind\" IN ('RemoveBan', 'RemoveVoiceMute', 'RemoveVoiceDeafen', 'RemoveRole') OR (\"Kind\" = 'SetTimeoutExpiry' AND \"ExpiryUtc\" IS NOT NULL)) AND \"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_ticket_operations_tickets_TicketId",
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
                    Current = table.Column<bool>(type: "boolean", nullable: false),
                    Removed = table.Column<bool>(type: "boolean", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_ticket_publications_ChannelId_MessageId",
                table: "ticket_publications",
                columns: new[] { "ChannelId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_Number",
                table: "tickets",
                columns: new[] { "GuildId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SourceAuditId_Action",
                table: "tickets",
                columns: new[] { "GuildId", "SourceAuditId", "Action" },
                unique: true,
                filter: "\"SourceAuditId\" IS NOT NULL AND \"RoleId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SourceAuditId_Action_RoleId",
                table: "tickets",
                columns: new[] { "GuildId", "SourceAuditId", "Action", "RoleId" },
                unique: true,
                filter: "\"SourceAuditId\" IS NOT NULL AND \"RoleId\" IS NOT NULL");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_configurations");

            migrationBuilder.DropTable(
                name: "ticket_history");

            migrationBuilder.DropTable(
                name: "ticket_moderators");

            migrationBuilder.DropTable(
                name: "ticket_operations");

            migrationBuilder.DropTable(
                name: "ticket_publications");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropSequence(
                name: "ticket_numbers");
        }
    }
}
