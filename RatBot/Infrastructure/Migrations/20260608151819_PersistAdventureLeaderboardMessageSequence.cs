namespace RatBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class PersistAdventureLeaderboardMessageSequence : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"AdventureLeaderboardMessageState\" WHERE \"Id\" <> 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdventureLeaderboardMessageState_SingletonId",
                table: "AdventureLeaderboardMessageState",
                sql: "\"Id\" = 1"
            );
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_AdventureLeaderboardMessageState_SingletonId", table: "AdventureLeaderboardMessageState");
        }
    }
}
