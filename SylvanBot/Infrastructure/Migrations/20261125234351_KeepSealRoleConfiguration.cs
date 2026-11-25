using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261125234351_KeepSealRoleConfiguration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "abandoned",
            table: "seal_hunts");

        migrationBuilder.DropColumn(
            name: "channel_id",
            table: "seal_hunts");

        migrationBuilder.DropColumn(
            name: "expires_at",
            table: "seal_hunts");

        migrationBuilder.DropColumn(
            name: "message_id",
            table: "seal_hunts");

        migrationBuilder.DropColumn(
            name: "next_at",
            table: "seal_hunts");

        migrationBuilder.DropColumn(
            name: "started_at",
            table: "seal_hunts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long[]>(
            name: "abandoned",
            table: "seal_hunts",
            type: "bigint[]",
            nullable: false,
            defaultValue: new long[0]);

        migrationBuilder.AddColumn<long>(
            name: "channel_id",
            table: "seal_hunts",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "expires_at",
            table: "seal_hunts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "message_id",
            table: "seal_hunts",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_at",
            table: "seal_hunts",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "started_at",
            table: "seal_hunts",
            type: "timestamp with time zone",
            nullable: true);
    }
}
