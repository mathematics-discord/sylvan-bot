namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddConfigRelations : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Configs");

            migrationBuilder.CreateTable(
                name: "QuorumScopeConfigs",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    QuorumProportion = table.Column<double>(type: "double precision", precision: 6, scale: 4, nullable: false),
                    RoleIds = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_QuorumScopeConfigs",
                        x => new
                        {
                            x.GuildId,
                            x.ScopeType,
                            x.ScopeId,
                        }
                    );
                }
            );

            migrationBuilder.CreateIndex(name: "IX_QuorumScopeConfigs_GuildId", table: "QuorumScopeConfigs", column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_QuorumScopeConfigs_GuildId_ScopeType",
                table: "QuorumScopeConfigs",
                columns: new[] { "GuildId", "ScopeType" }
            );
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuorumScopeConfigs");

            migrationBuilder.CreateTable(
                name: "Configs",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configs", x => new { x.Key, x.Value });
                }
            );
        }
    }
}
