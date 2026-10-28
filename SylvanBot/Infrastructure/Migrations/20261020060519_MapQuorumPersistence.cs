#nullable disable

namespace SylvanBot.Infrastructure.Migrations;

using Microsoft.EntityFrameworkCore.Migrations;

/// <inheritdoc />
public partial class MapQuorumPersistence : Migration
{
    // Historical migrations already create these tables; only EF model metadata changes.
    protected override void Up(MigrationBuilder migrationBuilder) { }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
