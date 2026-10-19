using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RatBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ticket_reference_numbers");

            migrationBuilder.CreateTable(
                name: "ticket_configurations",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    DestinationChannelId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_configurations", x => x.GuildId);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SubjectUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModeratorUserId = table.Column<long>(type: "bigint", nullable: true),
                    Comment = table.Column<string>(type: "text", nullable: true),
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
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("CK_tickets_note_duration", "\"Action\" <> 'Note' OR \"DurationSeconds\" IS NULL");
                    table.CheckConstraint("CK_tickets_reference", "\"Reference\" ~ '^(T-[0-9]{4,}|[0-9]+)$'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_ChannelId_MessageId",
                table: "tickets",
                columns: new[] { "GuildId", "ChannelId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_PreviousChannelId_PreviousMessageId",
                table: "tickets",
                columns: new[] { "GuildId", "PreviousChannelId", "PreviousMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_Reference",
                table: "tickets",
                columns: new[] { "GuildId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SubjectUserId_Hidden_Id",
                table: "tickets",
                columns: new[] { "GuildId", "SubjectUserId", "Hidden", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_configurations");

            migrationBuilder.DropTable(
                name: "tickets");

            migrationBuilder.DropSequence(
                name: "ticket_reference_numbers");
        }
    }
}
