using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Infrastructure.Migrations;

[DbContext(typeof(BotDbContext))]
[Migration("20261119000000_AddSealHunts")]
public sealed class AddSealHunts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE seal_hunts (
                guild_id bigint PRIMARY KEY,
                next_at timestamptz NOT NULL,
                role_id bigint,
                channel_id bigint,
                started_at timestamptz,
                message_id bigint
            );
            CREATE TABLE seal_destinations (
                guild_id bigint NOT NULL,
                destination_id bigint NOT NULL,
                PRIMARY KEY (guild_id, destination_id)
            );
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE seal_destinations, seal_hunts");
}
