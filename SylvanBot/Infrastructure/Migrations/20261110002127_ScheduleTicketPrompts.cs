using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScheduleTicketPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InitialPromptAfterUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE tickets SET "InitialPromptAfterUtc" = CURRENT_TIMESTAMP
                WHERE "InitialPromptPending";
                """);

            migrationBuilder.DropColumn(
                name: "InitialPromptPending",
                table: "tickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "InitialPromptPending",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE tickets SET "InitialPromptPending" = "InitialPromptAfterUtc" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(name: "InitialPromptAfterUtc", table: "tickets");
        }
    }
}
