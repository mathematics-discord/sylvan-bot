using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <summary>Adds persisted reaction role mappings.</summary>
    public partial class AddRoleReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleReactions",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    EmojiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    EmojiText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                    table.PrimaryKey(
                        "PK_RoleReactions",
                        x => new
                        {
                            x.GuildId,
                            x.MessageId,
                            x.EmojiKey,
                        }
                    )
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RoleReactions");
        }
    }
}
