namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class RenameQuorumSettingsColumn : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "Proportion", table: "QuorumConfigs", newName: "QuorumProportion");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "QuorumProportion", table: "QuorumConfigs", newName: "Proportion");
        }
    }
}
