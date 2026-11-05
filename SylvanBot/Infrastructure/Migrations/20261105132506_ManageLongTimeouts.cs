using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManageLongTimeouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AppliedTimeoutUntilUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TimeoutCheckAtUtc",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NativeExpiryUtc",
                table: "ticket_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE tickets
                SET "AppliedTimeoutUntilUtc" = "ExpiresAtUtc",
                    "TimeoutCheckAtUtc" = CURRENT_TIMESTAMP
                WHERE "Action" = 'Timeout';

                UPDATE ticket_operations
                SET "NativeExpiryUtc" = "ExpiryUtc"
                WHERE "Kind" = 'SetTimeoutExpiry';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedTimeoutUntilUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "TimeoutCheckAtUtc",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "NativeExpiryUtc",
                table: "ticket_operations");
        }
    }
}
