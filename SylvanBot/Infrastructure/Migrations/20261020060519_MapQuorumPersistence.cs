#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SylvanBot.Infrastructure.Migrations;

/// <inheritdoc />
public partial class MapQuorumPersistence : Migration
{
    // Historical migrations already create these tables; only EF model metadata changes.
    protected override void Up(MigrationBuilder migrationBuilder) { }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
