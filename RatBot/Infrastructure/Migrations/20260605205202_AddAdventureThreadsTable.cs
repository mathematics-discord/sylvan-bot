namespace RatBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddAdventureThreadsTable : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AdventureForumThreadLinks");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdventureForumThreadLinks",
                columns: table => new
                {
                    ScorePartIndex = table.Column<int>(type: "integer", nullable: false),
                    ThreadId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdventureForumThreadLinks", x => x.ScorePartIndex);
                    table.CheckConstraint("CK_AdventureForumThreadLinks_ScorePartIndex", "\"ScorePartIndex\" >= 1 AND \"ScorePartIndex\" <= 20");
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AdventureForumThreadLinks_ScorePartIndex",
                table: "AdventureForumThreadLinks",
                column: "ScorePartIndex",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AdventureForumThreadLinks_ThreadId",
                table: "AdventureForumThreadLinks",
                column: "ThreadId",
                unique: true
            );
        }
    }
}
