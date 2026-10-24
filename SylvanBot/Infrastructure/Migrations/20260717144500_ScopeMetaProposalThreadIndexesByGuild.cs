namespace SylvanBot.Infrastructure.Migrations;

#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

public partial class ScopeMetaProposalThreadIndexesByGuild : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_MetaProposalStates_SuggestionThreadChannelId";
            DROP INDEX IF EXISTS "IX_MetaProposalStates_ProposalThreadChannelId";
            DROP INDEX IF EXISTS "IX_MetaProposalStates_PollMessageId";

            CREATE UNIQUE INDEX "IX_MetaProposalStates_GuildId_SuggestionThreadChannelId"
                ON "MetaProposalStates" ("GuildId", "SuggestionThreadChannelId");

            CREATE INDEX "IX_MetaProposalStates_GuildId_ProposalThreadChannelId"
                ON "MetaProposalStates" ("GuildId", "ProposalThreadChannelId");

            CREATE INDEX "IX_MetaProposalStates_GuildId_PollMessageId"
                ON "MetaProposalStates" ("GuildId", "PollMessageId");
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_MetaProposalStates_GuildId_SuggestionThreadChannelId";
            DROP INDEX IF EXISTS "IX_MetaProposalStates_GuildId_ProposalThreadChannelId";
            DROP INDEX IF EXISTS "IX_MetaProposalStates_GuildId_PollMessageId";

            CREATE UNIQUE INDEX "IX_MetaProposalStates_SuggestionThreadChannelId"
                ON "MetaProposalStates" ("SuggestionThreadChannelId");

            CREATE INDEX "IX_MetaProposalStates_ProposalThreadChannelId"
                ON "MetaProposalStates" ("ProposalThreadChannelId");

            CREATE INDEX "IX_MetaProposalStates_PollMessageId"
                ON "MetaProposalStates" ("PollMessageId");
            """
        );
    }
}
