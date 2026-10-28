using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Infrastructure.Migrations;

#nullable disable

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SylvanBot.Infrastructure.Data;

[DbContext(typeof(BotDbContext))]
[Migration("20260625213000_AddQuorumConfigurationTables")]
public sealed class AddQuorumConfigurationTables : Migration
{
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "quorum_voter_roles");
        migrationBuilder.DropTable(name: "quorum_configurations");
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "quorum_configurations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                guild_id = table.Column<long>(type: "bigint", nullable: false),
                channel_id = table.Column<long>(type: "bigint", nullable: false),
                channel_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                proportion = table.Column<decimal>(type: "numeric(9,8)", precision: 9, scale: 8, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quorum_configurations", x => x.id);
                table.UniqueConstraint("ak_quorum_configurations_guild_id_channel_id", x => new { x.guild_id, x.channel_id });
                table.CheckConstraint("ck_quorum_configurations_channel_kind", "channel_kind IN ('text', 'forum')");
                table.CheckConstraint("ck_quorum_configurations_proportion", "proportion > 0 AND proportion <= 1");
            }
        );

        migrationBuilder.CreateTable(
            name: "quorum_voter_roles",
            columns: table => new
            {
                quorum_configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                role_id = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quorum_voter_roles", x => new { x.quorum_configuration_id, x.role_id });
                table.ForeignKey(
                    name: "fk_quorum_voter_roles_quorum_configurations",
                    column: x => x.quorum_configuration_id,
                    principalTable: "quorum_configurations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade
                );
            }
        );
    }
}
