namespace SylvanBot.Infrastructure.Migrations
{
#nullable disable

    using Microsoft.EntityFrameworkCore.Migrations;
    using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

    /// <inheritdoc />
    public partial class RedesignMetaProposals : Migration
    {
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MetaProposalStates");

            migrationBuilder.DropColumn(name: "CabinetChairRoleId", table: "MetaSuggestionSettings");

            migrationBuilder.DropColumn(name: "CabinetRoleId", table: "MetaSuggestionSettings");

            migrationBuilder.DropColumn(name: "CommitteeRoleId", table: "MetaSuggestionSettings");

            migrationBuilder.DropColumn(name: "ProposalsForumChannelId", table: "MetaSuggestionSettings");

            migrationBuilder.RenameColumn(name: "SuggestionsForumChannelId", table: "MetaSuggestionSettings", newName: "SuggestForumChannelId");

            migrationBuilder.CreateTable(
                name: "MetaSuggestions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    ForumChannelId = table.Column<long>(type: "bigint", nullable: true),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    Motivation = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: false),
                    Specification = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    ThreadChannelId = table.Column<long>(type: "bigint", nullable: true),
                    Title = table.Column<string>(type: "character varying(75)", maxLength: 75, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaSuggestions", x => x.Id);
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

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MetaSuggestions");

            migrationBuilder.RenameColumn(name: "SuggestForumChannelId", table: "MetaSuggestionSettings", newName: "SuggestionsForumChannelId");

            migrationBuilder.AddColumn<long>(
                name: "CabinetChairRoleId",
                table: "MetaSuggestionSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<long>(
                name: "CabinetRoleId",
                table: "MetaSuggestionSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<long>(
                name: "CommitteeRoleId",
                table: "MetaSuggestionSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.AddColumn<long>(
                name: "ProposalsForumChannelId",
                table: "MetaSuggestionSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L
            );

            migrationBuilder.CreateTable(
                name: "MetaProposalStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    SuggestionThreadChannelId = table.Column<long>(type: "bigint", nullable: false),
                    SuggestionsForumChannelId = table.Column<long>(type: "bigint", nullable: false),
                    OriginalThreadAuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    TrackedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailedPollAttempts = table.Column<int>(type: "integer", nullable: false),
                    ProposalAuthorUserId = table.Column<long>(type: "bigint", nullable: true),
                    ProposalTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    Motivation = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: true),
                    Specification = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: true),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PollMessageId = table.Column<long>(type: "bigint", nullable: true),
                    PollExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PollFinalizationRetries = table.Column<int>(type: "integer", nullable: false),
                    ProposalThreadChannelId = table.Column<long>(type: "bigint", nullable: true),
                    PublicationErrorMessageId = table.Column<long>(type: "bigint", nullable: true),
                    LastPublicationRetryAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublicationRetryFailures = table.Column<int>(type: "integer", nullable: false),
                    VetoedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    VetoedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VetoReason = table.Column<string>(type: "character varying(1950)", maxLength: 1950, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetaProposalStates", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(name: "IX_MetaProposalStates_GuildId", table: "MetaProposalStates", column: "GuildId");

            migrationBuilder.CreateIndex(name: "IX_MetaProposalStates_PollMessageId", table: "MetaProposalStates", column: "PollMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_ProposalThreadChannelId",
                table: "MetaProposalStates",
                column: "ProposalThreadChannelId"
            );

            migrationBuilder.CreateIndex(name: "IX_MetaProposalStates_Status", table: "MetaProposalStates", column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_Status_PollExpiresAtUtc",
                table: "MetaProposalStates",
                columns: new[] { "Status", "PollExpiresAtUtc" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MetaProposalStates_SuggestionThreadChannelId",
                table: "MetaProposalStates",
                column: "SuggestionThreadChannelId",
                unique: true
            );
        }
    }
}
