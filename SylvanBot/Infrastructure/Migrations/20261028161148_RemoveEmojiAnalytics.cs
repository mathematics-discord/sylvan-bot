using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEmojiAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmojiUsageCounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmojiUsageCounts",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    EmojiId = table.Column<long>(type: "bigint", nullable: false),
                    MessageUsageCount = table.Column<int>(type: "integer", nullable: false),
                    ReactionUsageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmojiUsageCounts", x => new { x.GuildId, x.EmojiId });
                });
        }
    }
}
