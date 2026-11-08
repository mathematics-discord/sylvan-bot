using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveTicketEmbedFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Give existing tickets their original layout until selected for an in-place update.
            migrationBuilder.AddColumn<bool>(
                name: "UseOriginalEmbedFormat",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("""ALTER TABLE tickets ALTER COLUMN "UseOriginalEmbedFormat" SET DEFAULT FALSE""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseOriginalEmbedFormat",
                table: "tickets");
        }
    }
}
