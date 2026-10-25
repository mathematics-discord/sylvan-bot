namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddColourModuleTables : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MemberColourPreferences");

            migrationBuilder.DropTable(name: "RoleColourOptions");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleColourOptions",
                columns: table => new
                {
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalisedKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRoleId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayRoleId = table.Column<long>(type: "bigint", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleColourOptions", x => x.OptionId);
                    table.CheckConstraint("CK_RoleColourOptions_DifferentRoles", "\"SourceRoleId\" <> \"DisplayRoleId\"");
                    table.CheckConstraint("CK_RoleColourOptions_Key_NotBlank", "length(btrim(\"Key\")) > 0");
                    table.CheckConstraint("CK_RoleColourOptions_Label_NotBlank", "length(btrim(\"Label\")) > 0");
                }
            );

            migrationBuilder.CreateTable(
                name: "MemberColourPreferences",
                columns: table => new
                {
                    PreferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberColourPreferences", x => x.PreferenceId);
                    table.CheckConstraint(
                        "CK_MemberColourPreferences_ConfiguredOption_SelectedOption_Not~",
                        "(\"Kind\" <> 1) OR (\"SelectedOptionId\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_MemberColourPreferences_NoColour_SelectedOption_Null",
                        "(\"Kind\" <> 2) OR (\"SelectedOptionId\" IS NULL)"
                    );
                    table.ForeignKey(
                        name: "FK_MemberColourPreferences_RoleColourOptions_SelectedOptionId",
                        column: x => x.SelectedOptionId,
                        principalTable: "RoleColourOptions",
                        principalColumn: "OptionId",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MemberColourPreferences_SelectedOptionId",
                table: "MemberColourPreferences",
                column: "SelectedOptionId"
            );

            migrationBuilder.CreateIndex(name: "IX_MemberColourPreferences_UserId", table: "MemberColourPreferences", column: "UserId", unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleColourOptions_DisplayRoleId",
                table: "RoleColourOptions",
                column: "DisplayRoleId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RoleColourOptions_NormalisedKey",
                table: "RoleColourOptions",
                column: "NormalisedKey",
                unique: true
            );

            migrationBuilder.CreateIndex(name: "IX_RoleColourOptions_SourceRoleId", table: "RoleColourOptions", column: "SourceRoleId", unique: true);
        }
    }
}
