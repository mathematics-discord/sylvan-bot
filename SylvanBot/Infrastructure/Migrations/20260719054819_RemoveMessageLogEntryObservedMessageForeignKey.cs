namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class RemoveMessageLogEntryObservedMessageForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_message_log_entries_observed_messages_guild_id_original_message_id",
                table: "message_log_entries"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_message_log_entries_observed_messages_guild_id_original_message_id",
                table: "message_log_entries",
                columns: new[] { "guild_id", "original_message_id" },
                principalTable: "observed_messages",
                principalColumns: new[] { "guild_id", "original_message_id" },
                onDelete: ReferentialAction.Cascade
            );
        }
    }
}
