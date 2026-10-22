namespace RatBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddMessageUsageCountToEmojiAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MessageUsageCount", table: "EmojiUsageCounts");

            migrationBuilder.RenameColumn(name: "ReactionUsageCount", table: "EmojiUsageCounts", newName: "UsageCount");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "UsageCount", table: "EmojiUsageCounts", newName: "ReactionUsageCount");

            migrationBuilder.AddColumn<int>(name: "MessageUsageCount", table: "EmojiUsageCounts", type: "integer", nullable: false, defaultValue: 0);
        }
    }
}
