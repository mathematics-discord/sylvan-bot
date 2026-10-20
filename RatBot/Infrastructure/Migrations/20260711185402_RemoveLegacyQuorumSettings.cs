namespace RatBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class RemoveLegacyQuorumSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuorumConfigRoles");

            migrationBuilder.DropTable(name: "QuorumConfigs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuorumConfigs",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    Proportion = table.Column<double>(type: "double precision", precision: 6, scale: 4, nullable: false),
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
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
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
