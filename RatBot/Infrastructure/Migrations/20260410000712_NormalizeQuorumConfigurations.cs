namespace RatBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class NormalizeQuorumConfigurations : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "QuorumScopeConfigRoles");

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

            migrationBuilder.Sql(
                """
                INSERT INTO "Configs" ("Key", "Value")
                SELECT
                    'Quorum:GuildScopeConfig',
                    jsonb_build_object(
                        'GuildId', config."GuildId",
                        'ScopeType', config."ScopeType",
                        'ScopeId', config."ScopeId",
                        'RoleIds', COALESCE(jsonb_agg(role."RoleId" ORDER BY role."RoleId"), '[]'::jsonb),
                        'QuorumProportion', config."QuorumProportion"
                    )
                FROM "QuorumScopeConfigs" AS config
                LEFT JOIN "QuorumScopeConfigRoles" AS role
                    ON role."GuildId" = config."GuildId"
                   AND role."ScopeType" = config."ScopeType"
                   AND role."ScopeId" = config."ScopeId"
                GROUP BY config."GuildId", config."ScopeType", config."ScopeId", config."QuorumProportion";
                """
            );
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.Sql(
                """
                INSERT INTO "QuorumScopeConfigs" ("GuildId", "ScopeType", "ScopeId", "QuorumProportion")
                SELECT
                    ("Value"->>'GuildId')::numeric(20,0),
                    ("Value"->>'ScopeType')::integer,
                    ("Value"->>'ScopeId')::numeric(20,0),
                    ("Value"->>'QuorumProportion')::double precision
                FROM "Configs"
                WHERE "Key" = 'Quorum:GuildScopeConfig';
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "QuorumScopeConfigRoles" ("GuildId", "ScopeType", "ScopeId", "RoleId")
                SELECT
                    (config."Value"->>'GuildId')::numeric(20,0),
                    (config."Value"->>'ScopeType')::integer,
                    (config."Value"->>'ScopeId')::numeric(20,0),
                    role_id::numeric(20,0)
                FROM "Configs" AS config,
                     LATERAL jsonb_array_elements_text(config."Value"->'RoleIds') AS role_id
                WHERE config."Key" = 'Quorum:GuildScopeConfig';
                """
            );

            migrationBuilder.DropTable(name: "Configs");
        }
    }
}
