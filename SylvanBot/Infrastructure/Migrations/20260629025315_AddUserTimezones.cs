namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class AddUserTimezones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_timezones",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    timezone_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_timezones", x => x.user_id);
                    table.CheckConstraint("CK_user_timezones_timezone_id_not_empty", "length(btrim(timezone_id)) > 0");
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "user_timezones");
        }
    }
}
