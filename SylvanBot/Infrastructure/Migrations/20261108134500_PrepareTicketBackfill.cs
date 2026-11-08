using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations;

public partial class PrepareTicketBackfill : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("OrderedPublicationPending", "ticket_configurations",
            type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.DropCheckConstraint("CK_tickets_duration", "tickets");
        migrationBuilder.AddCheckConstraint("CK_tickets_duration", "tickets",
            "(\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0 OR \"Status\" <> 'Active') AND (\"Action\" <> 'Timeout' OR (\"DurationSeconds\" IS NOT NULL AND \"ExpiresAtUtc\" IS NOT NULL))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_tickets_duration", "tickets");
        migrationBuilder.AddCheckConstraint("CK_tickets_duration", "tickets",
            "(\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0) AND (\"Action\" <> 'Timeout' OR (\"DurationSeconds\" IS NOT NULL AND \"ExpiresAtUtc\" IS NOT NULL))");
        migrationBuilder.DropColumn("OrderedPublicationPending", "ticket_configurations");
    }
}
