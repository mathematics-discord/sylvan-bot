using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SylvanBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSylvanMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sylvan_mail_conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    ForumId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ThreadId = table.Column<long>(type: "bigint", nullable: true),
                    Retired = table.Column<bool>(type: "boolean", nullable: false),
                    CreationStarted = table.Column<bool>(type: "boolean", nullable: false),
                    NoticeStarted = table.Column<bool>(type: "boolean", nullable: false),
                    NoticeMessageId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sylvan_mail_conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sylvan_mail_settings",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false),
                    ForumId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sylvan_mail_settings", x => x.GuildId);
                    table.CheckConstraint("CK_sylvan_mail_single_destination", "\"Slot\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "sylvan_mail_operations",
                columns: table => new
                {
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: false),
                    SourceChannelId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorId = table.Column<long>(type: "bigint", nullable: false),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToStaff = table.Column<bool>(type: "boolean", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DestinationChannelId = table.Column<long>(type: "bigint", nullable: true),
                    ReceiptMessageId = table.Column<long>(type: "bigint", nullable: true),
                    AcknowledgementStarted = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgementMessageId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sylvan_mail_operations", x => x.SourceMessageId);
                    table.ForeignKey(
                        name: "FK_sylvan_mail_operations_sylvan_mail_conversations_Conversati~",
                        column: x => x.ConversationId,
                        principalTable: "sylvan_mail_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sylvan_mail_deliveries",
                columns: table => new
                {
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sylvan_mail_deliveries", x => new { x.SourceMessageId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_sylvan_mail_deliveries_sylvan_mail_operations_SourceMessage~",
                        column: x => x.SourceMessageId,
                        principalTable: "sylvan_mail_operations",
                        principalColumn: "SourceMessageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sylvan_mail_conversations_GuildId_ForumId_UserId",
                table: "sylvan_mail_conversations",
                columns: new[] { "GuildId", "ForumId", "UserId" },
                unique: true,
                filter: "NOT \"Retired\"");

            migrationBuilder.CreateIndex(
                name: "IX_sylvan_mail_conversations_Number",
                table: "sylvan_mail_conversations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sylvan_mail_conversations_ThreadId",
                table: "sylvan_mail_conversations",
                column: "ThreadId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sylvan_mail_operations_ConversationId",
                table: "sylvan_mail_operations",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_sylvan_mail_settings_ForumId",
                table: "sylvan_mail_settings",
                column: "ForumId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sylvan_mail_settings_Slot",
                table: "sylvan_mail_settings",
                column: "Slot",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sylvan_mail_deliveries");

            migrationBuilder.DropTable(
                name: "sylvan_mail_settings");

            migrationBuilder.DropTable(
                name: "sylvan_mail_operations");

            migrationBuilder.DropTable(
                name: "sylvan_mail_conversations");
        }
    }
}