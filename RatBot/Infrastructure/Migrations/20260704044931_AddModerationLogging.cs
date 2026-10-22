namespace RatBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddModerationLogging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "logging_configurations",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    delete_log_channel_id = table.Column<long>(type: "bigint", nullable: true),
                    edit_log_channel_id = table.Column<long>(type: "bigint", nullable: true),
                    evidence_retention_period = table.Column<TimeSpan>(type: "interval", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logging_configurations", x => x.guild_id);
                }
            );

            migrationBuilder.CreateTable(
                name: "logging_excluded_channels",
                columns: table => new
                {
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    excluded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_logging_excluded_channels", x => new { x.guild_id, x.channel_id });
                }
            );

            migrationBuilder.CreateTable(
                name: "message_log_entries",
                columns: table => new
                {
                    original_message_id = table.Column<long>(type: "bigint", nullable: false),
                    log_message_id = table.Column<long>(type: "bigint", nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_log_entries", x => new { x.original_message_id, x.log_message_id });
                }
            );

            migrationBuilder.CreateTable(
                name: "observed_messages",
                columns: table => new
                {
                    original_message_id = table.Column<long>(type: "bigint", nullable: false),
                    guild_id = table.Column<long>(type: "bigint", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observed_messages", x => x.original_message_id);
                }
            );

            migrationBuilder.CreateIndex(name: "IX_message_log_entries_log_message_id", table: "message_log_entries", column: "log_message_id");

            migrationBuilder.CreateIndex(name: "IX_observed_messages_author_id", table: "observed_messages", column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_observed_messages_guild_id_channel_id",
                table: "observed_messages",
                columns: new[] { "guild_id", "channel_id" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "logging_configurations");

            migrationBuilder.DropTable(name: "logging_excluded_channels");

            migrationBuilder.DropTable(name: "message_log_entries");

            migrationBuilder.DropTable(name: "observed_messages");
        }
    }
}
