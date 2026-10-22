namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class UpdateSuggestionAnonymityType : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsAnonymous", table: "MetaSuggestions");

            migrationBuilder.AddColumn<int>(name: "Anonymity", table: "MetaSuggestions", type: "integer", nullable: false, defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Anonymity", table: "MetaSuggestions");

            migrationBuilder.AddColumn<bool>(name: "IsAnonymous", table: "MetaSuggestions", type: "boolean", nullable: false, defaultValue: false);
        }
    }
}
