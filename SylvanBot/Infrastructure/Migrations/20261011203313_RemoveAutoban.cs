#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    using System;
    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class RemoveAutoban : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing autoban records are permanently discarded. Down restores only the table structure.
            migrationBuilder.DropTable(
                name: "AutobannedUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutobannedUsers",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    BannedUser = table.Column<long>(type: "bigint", nullable: false),
                    Moderator = table.Column<long>(type: "bigint", nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutobannedUsers", x => new { x.GuildId, x.BannedUser });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutobannedUsers_BannedUser",
                table: "AutobannedUsers",
                column: "BannedUser");

            migrationBuilder.CreateIndex(
                name: "IX_AutobannedUsers_GuildId",
                table: "AutobannedUsers",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_AutobannedUsers_Moderator",
                table: "AutobannedUsers",
                column: "Moderator");
        }
    }
}
