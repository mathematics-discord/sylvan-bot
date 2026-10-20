#nullable disable

namespace RatBot.Infrastructure.Migrations
{
    using System;
    using Microsoft.EntityFrameworkCore.Migrations;

    /// <inheritdoc />
    public partial class RemoveMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stored Meta data is permanently discarded; Down restores only the schema.
            migrationBuilder.DropTable(
                name: "MetaProposalStates");

            migrationBuilder.DropTable(
                name: "MetaSuggestionSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetaProposalStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FailedPollAttempts = table.Column<int>(type: "integer", nullable: false),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    LastPublicationRetryAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Motivation = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: true),
                    OriginalThreadAuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    PollExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PollFinalizationRetries = table.Column<int>(type: "integer", nullable: false),
                    PollMessageId = table.Column<long>(type: "bigint", nullable: true),
                    ProposalAuthorUserId = table.Column<long>(type: "bigint", nullable: true),
                    ProposalThreadChannelId = table.Column<long>(type: "bigint", nullable: true),
                    ProposalTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublicationErrorMessageId = table.Column<long>(type: "bigint", nullable: true),
                    PublicationRetryFailures = table.Column<int>(type: "integer", nullable: false),
                    Specification = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SuggestionThreadChannelId = table.Column<long>(type: "bigint", nullable: false),
                    SuggestionsForumChannelId = table.Column<long>(type: "bigint", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    TrackedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VetoReason = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: true),
                    VetoedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VetoedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaProposalStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetaSuggestionSettings",
                columns: table => new
                {
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    CabinetChairRoleId = table.Column<long>(type: "bigint", nullable: false),
                    CabinetRoleId = table.Column<long>(type: "bigint", nullable: false),
                    CommitteeRoleId = table.Column<long>(type: "bigint", nullable: false),
                    ProposalsForumChannelId = table.Column<long>(type: "bigint", nullable: false),
                    SuggestionsForumChannelId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaSuggestionSettings", x => x.GuildId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_GuildId",
                table: "MetaProposalStates",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_GuildId_PollMessageId",
                table: "MetaProposalStates",
                columns: new[] { "GuildId", "PollMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_GuildId_ProposalThreadChannelId",
                table: "MetaProposalStates",
                columns: new[] { "GuildId", "ProposalThreadChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_GuildId_SuggestionThreadChannelId",
                table: "MetaProposalStates",
                columns: new[] { "GuildId", "SuggestionThreadChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_Status",
                table: "MetaProposalStates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_Status_PollExpiresAtUtc",
                table: "MetaProposalStates",
                columns: new[] { "Status", "PollExpiresAtUtc" });
        }
    }
}
