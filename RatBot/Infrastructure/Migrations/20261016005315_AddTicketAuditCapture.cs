using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RatBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAuditCapture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditCollectionPositions",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    CaptureStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastScannedAuditId = table.Column<long>(type: "bigint", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditCollectionPositions", x => x.GuildId);
                });

            migrationBuilder.CreateTable(
                name: "CapturedAuditEntries",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    AuditId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: true),
                    TargetId = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    AddedRoleIds = table.Column<long[]>(type: "bigint[]", nullable: false),
                    RemovedRoleIds = table.Column<long[]>(type: "bigint[]", nullable: false),
                    VoiceMuteBefore = table.Column<bool>(type: "boolean", nullable: true),
                    VoiceMuteAfter = table.Column<bool>(type: "boolean", nullable: true),
                    VoiceDeafenBefore = table.Column<bool>(type: "boolean", nullable: true),
                    VoiceDeafenAfter = table.Column<bool>(type: "boolean", nullable: true),
                    HasTimeoutChange = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutBeforeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeoutAfterUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapturedAuditEntries", x => new { x.GuildId, x.AuditId });

                    table.CheckConstraint(
                        "CK_CapturedAuditEntries_TimeoutPresence",
                        "\"HasTimeoutChange\" OR (\"TimeoutBeforeUtc\" IS NULL AND \"TimeoutAfterUtc\" IS NULL)");

                    table.CheckConstraint(
                        "CK_CapturedAuditEntries_VoiceDeafenPair",
                        "(\"VoiceDeafenBefore\" IS NULL) = (\"VoiceDeafenAfter\" IS NULL)");

                    table.CheckConstraint(
                        "CK_CapturedAuditEntries_VoiceMutePair",
                        "(\"VoiceMuteBefore\" IS NULL) = (\"VoiceMuteAfter\" IS NULL)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditCollectionPositions");

            migrationBuilder.DropTable(
                name: "CapturedAuditEntries");
        }
    }
}