namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;
    using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

    /// <inheritdoc />
    public partial class AddMetaSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MetaSuggestions");

            migrationBuilder.DropTable(name: "MetaSuggestionSettings");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetaSuggestions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "character varying(75)", maxLength: 75, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Motivation = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: false),
                    Specification = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: false),
                    Anonymity = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ForumChannelId = table.Column<long>(type: "bigint", nullable: false),
                    ThreadChannelId = table.Column<long>(type: "bigint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaSuggestions", x => x.Id);
                    table.CheckConstraint("CK_MetaSuggestions_Motivation_NotBlank", "length(btrim(\"Motivation\")) > 0");
                    table.CheckConstraint("CK_MetaSuggestions_Specification_NotBlank", "length(btrim(\"Specification\")) > 0");
                    table.CheckConstraint("CK_MetaSuggestions_Summary_NotBlank", "length(btrim(\"Summary\")) > 0");
                    table.CheckConstraint("CK_MetaSuggestions_Title_NotBlank", "length(btrim(\"Title\")) > 0");
                }
            );

            migrationBuilder.CreateTable(
                name: "MetaSuggestionSettings",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    SuggestForumChannelId = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaSuggestionSettings", x => x.GuildId);
                }
            );

            migrationBuilder.CreateIndex(name: "IX_MetaSuggestions_GuildId", table: "MetaSuggestions", column: "GuildId");

            migrationBuilder.CreateIndex(name: "IX_MetaSuggestions_GuildId_State", table: "MetaSuggestions", columns: new[] { "GuildId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MetaSuggestions_ThreadChannelId",
                table: "MetaSuggestions",
                column: "ThreadChannelId",
                unique: true
            );
        }
    }
}
