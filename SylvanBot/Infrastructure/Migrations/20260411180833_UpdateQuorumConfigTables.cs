namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class UpdateQuorumConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuorumConfigRoles");

            migrationBuilder.DropTable(name: "QuorumConfigs");

            migrationBuilder.CreateTable(
                name: "QuorumScopeConfigs",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    QuorumProportion = table.Column<double>(type: "double precision", precision: 6, scale: 4, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "QuorumScopeConfigRoles",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    ScopeType = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_QuorumScopeConfigRoles",
                        x => new
                        {
                            x.GuildId,
                            x.ScopeType,
                            x.ScopeId,
                            x.RoleId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_QuorumScopeConfigRoles_QuorumScopeConfigs_GuildId_ScopeType~",
                        columns: x => new
                        {
                            x.GuildId,
                            x.ScopeType,
                            x.ScopeId,
                        },
                        principalTable: "QuorumScopeConfigs",
                        principalColumns: new[] { "GuildId", "ScopeType", "ScopeId" },
                        onDelete: ReferentialAction.Cascade
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
            migrationBuilder.DropTable(name: "QuorumScopeConfigRoles");

            migrationBuilder.DropTable(name: "QuorumScopeConfigs");

            migrationBuilder.CreateTable(
                name: "QuorumConfigs",
                columns: table => new
                {
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    QuorumProportion = table.Column<double>(type: "double precision", precision: 6, scale: 4, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_QuorumConfigs",
                        x => new
                        {
                            x.GuildId,
                            x.TargetType,
                            x.TargetId,
                        }
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "QuorumConfigRoles",
                columns: table => new
                {
                    RoleId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    GuildId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_QuorumConfigRoles",
                        x => new
                        {
                            x.GuildId,
                            x.TargetType,
                            x.TargetId,
                            x.RoleId,
                        }
                    );
                    table.ForeignKey(
                        name: "FK_QuorumConfigRoles_QuorumConfigs_GuildId_TargetType_TargetId",
                        columns: x => new
                        {
                            x.GuildId,
                            x.TargetType,
                            x.TargetId,
                        },
                        principalTable: "QuorumConfigs",
                        principalColumns: new[] { "GuildId", "TargetType", "TargetId" },
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(name: "IX_QuorumConfigs_GuildId", table: "QuorumConfigs", column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_QuorumConfigs_GuildId_TargetType",
                table: "QuorumConfigs",
                columns: new[] { "GuildId", "TargetType" }
            );
        }
    }
}
