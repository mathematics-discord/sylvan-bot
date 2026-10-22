namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddImageBurstSpamDetectorSettings : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ImageSpamSettings");
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImageSpamSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    RequiredChannelCount = table.Column<int>(type: "integer", nullable: false),
                    RequiredAttachmentCount = table.Column<int>(type: "integer", nullable: false),
                    BurstDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageSpamSettings", x => x.Id);
                    table.CheckConstraint("CK_ImageSpamSettings_BurstDurationSeconds_Positive", "\"BurstDurationSeconds\" > 0");
                    table.CheckConstraint("CK_ImageSpamSettings_RequiredAttachmentCount_Positive", "\"RequiredAttachmentCount\" > 0");
                    table.CheckConstraint("CK_ImageSpamSettings_RequiredChannelCount_Positive", "\"RequiredChannelCount\" > 0");
                }
            );
        }
    }
}
