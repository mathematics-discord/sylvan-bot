using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RatBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "ticket_notes",
                columns: table => new
                {
                    TicketNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'1', '1', '1', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    SubjectUserId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedChannelId = table.Column<long>(type: "bigint", nullable: true),
                    PublishedMessageId = table.Column<long>(type: "bigint", nullable: true),
                    LegacyTicketNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_notes", x => x.TicketNumber);
                    table.CheckConstraint("CK_ticket_notes_positive_number", "\"TicketNumber\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_notes_GuildId_LegacyTicketNumber",
                table: "ticket_notes",
                columns: new[] { "GuildId", "LegacyTicketNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_configurations");

            migrationBuilder.DropTable(
                name: "ticket_notes");
        }
    }
}
