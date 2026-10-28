using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedByUserId",
                table: "tickets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HidePending",
                table: "tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "HidePending",
                table: "tickets");
        }
    }
}
