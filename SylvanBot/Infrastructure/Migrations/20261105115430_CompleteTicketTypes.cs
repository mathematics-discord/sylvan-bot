using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteTicketTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_SourceAuditId",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ticket_operations_command",
                table: "ticket_operations");

            migrationBuilder.AddColumn<long[]>(
                name: "TrackedRoleIds",
                table: "ticket_configurations",
                type: "bigint[]",
                nullable: false,
                defaultValue: new long[0]);

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_ticket_operations_command",
                table: "ticket_operations",
                sql: "(\"Kind\" IN ('RemoveBan', 'RemoveVoiceMute', 'RemoveVoiceDeafen', 'RemoveRole') OR (\"Kind\" = 'SetTimeoutExpiry' AND \"ExpiryUtc\" IS NOT NULL)) AND \"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_SourceAuditId_Action",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "IX_tickets_GuildId_SourceAuditId_Action_RoleId",
                table: "tickets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ticket_operations_command",
                table: "ticket_operations");

            migrationBuilder.DropColumn(
                name: "TrackedRoleIds",
                table: "ticket_configurations");

            migrationBuilder.CreateIndex(
                name: "IX_tickets_GuildId_SourceAuditId",
                table: "tickets",
                columns: new[] { "GuildId", "SourceAuditId" },
                unique: true,
                filter: "\"SourceAuditId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ticket_operations_command",
                table: "ticket_operations",
                sql: "(\"Kind\" = 'RemoveBan' OR (\"Kind\" = 'SetTimeoutExpiry' AND \"ExpiryUtc\" IS NOT NULL)) AND \"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }
    }
}
