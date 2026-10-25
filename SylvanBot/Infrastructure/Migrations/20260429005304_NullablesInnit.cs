namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class NullablesInnit : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "ForumChannelId",
                table: "MetaSuggestions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_MetaSuggestions_Motivation_NotBlank",
                table: "MetaSuggestions",
                sql: "length(btrim(\"Motivation\")) > 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_MetaSuggestions_Specification_NotBlank",
                table: "MetaSuggestions",
                sql: "length(btrim(\"Specification\")) > 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_MetaSuggestions_Summary_NotBlank",
                table: "MetaSuggestions",
                sql: "length(btrim(\"Summary\")) > 0"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_MetaSuggestions_Title_NotBlank",
                table: "MetaSuggestions",
                sql: "length(btrim(\"Title\")) > 0"
            );
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_MetaSuggestions_Motivation_NotBlank", table: "MetaSuggestions");

            migrationBuilder.DropCheckConstraint(name: "CK_MetaSuggestions_Specification_NotBlank", table: "MetaSuggestions");

            migrationBuilder.DropCheckConstraint(name: "CK_MetaSuggestions_Summary_NotBlank", table: "MetaSuggestions");

            migrationBuilder.DropCheckConstraint(name: "CK_MetaSuggestions_Title_NotBlank", table: "MetaSuggestions");

            migrationBuilder.AlterColumn<long>(
                name: "ForumChannelId",
                table: "MetaSuggestions",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint"
            );
        }
    }
}
