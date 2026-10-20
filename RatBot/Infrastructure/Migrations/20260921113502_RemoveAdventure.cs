#nullable disable

namespace RatBot.Infrastructure.Migrations
{
    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class RemoveAdventure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdventureForumThreadLinks");

            migrationBuilder.DropTable(name: "AdventureLeaderboardMessageState");

            migrationBuilder.DropTable(name: "AdventureSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdventureForumThreadLinks",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    ScorePartIndex = table.Column<int>(type: "integer", nullable: false),
                    ThreadId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureForumThreadLinks", x => new { x.GuildId, x.ScorePartIndex });
                    table.CheckConstraint("CK_AdventureForumThreadLinks_ScorePartIndex", "\"ScorePartIndex\" >= 1 AND \"ScorePartIndex\" <= 20");
                }
            );

            migrationBuilder.CreateTable(
                name: "AdventureLeaderboardMessageState",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    LastRenderHash = table.Column<string>(type: "text", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureLeaderboardMessageState", x => new { x.GuildId, x.Id });
                }
            );

            migrationBuilder.CreateTable(
                name: "AdventureSettings",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    AdventurerRoleId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureSettings", x => x.GuildId);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AdventureForumThreadLinks_GuildId_ScorePartIndex",
                table: "AdventureForumThreadLinks",
                columns: new[] { "GuildId", "ScorePartIndex" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AdventureForumThreadLinks_GuildId_ThreadId",
                table: "AdventureForumThreadLinks",
                columns: new[] { "GuildId", "ThreadId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AdventureLeaderboardMessageState_GuildId_Id",
                table: "AdventureLeaderboardMessageState",
                columns: new[] { "GuildId", "Id" },
                unique: true
            );
        }
    }
}
