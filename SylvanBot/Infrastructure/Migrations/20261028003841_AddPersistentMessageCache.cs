using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentMessageCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cached_messages",
                columns: table => new
                {
                    bot_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_cached_messages",
                        x => new
                        {
                            x.bot_id,
                            x.guild_id,
                            x.message_id,
                        }
                    );
                    table.CheckConstraint("ck_cached_messages_expiry", "expires_at_utc = created_at_utc + interval '24 hours'");
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_cached_messages_bot_id_guild_id_expires_at_utc",
                table: "cached_messages",
                columns: new[] { "bot_id", "guild_id", "expires_at_utc" }
            );

            migrationBuilder.CreateIndex(name: "IX_cached_messages_expires_at_utc", table: "cached_messages", column: "expires_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "cached_messages");
        }
    }
}
