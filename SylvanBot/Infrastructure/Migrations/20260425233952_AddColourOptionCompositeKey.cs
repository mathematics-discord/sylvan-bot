namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddColourOptionCompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_RoleColourOptions_SourceRoleId_DisplayRoleId", table: "RoleColourOptions");

            migrationBuilder.CreateIndex(name: "IX_RoleColourOptions_SourceRoleId", table: "RoleColourOptions", column: "SourceRoleId", unique: true);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_RoleColourOptions_SourceRoleId", table: "RoleColourOptions");

            migrationBuilder.CreateIndex(
                name: "IX_RoleColourOptions_SourceRoleId_DisplayRoleId",
                table: "RoleColourOptions",
                columns: new[] { "SourceRoleId", "DisplayRoleId" },
                unique: true
            );
        }
    }
}
