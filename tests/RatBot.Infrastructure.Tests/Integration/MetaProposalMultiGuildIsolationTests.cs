using Microsoft.EntityFrameworkCore;
using RatBot.Domain.Features.Meta;
using RatBot.Infrastructure.Data;
using RatBot.Infrastructure.Features.Meta;
using Serilog;

namespace RatBot.Infrastructure.Tests.Integration;

[TestFixture]
public sealed class MetaProposalMultiGuildIsolationTests
{
    [SetUp]
    public async Task SetUp()
    {
        await PostgresDatabaseFixture.ResetAsync();
    }

    [Test]
    public async Task TrackSuggestionThreadAsync_AllowsSameThreadShapedIdInDifferentGuilds()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        MetaProposalService service = new MetaProposalService(db, Log.Logger);

        await service.TrackSuggestionThreadAsync(1, 10, 100, 1000, DateTimeOffset.UtcNow, CancellationToken.None);
        await service.TrackSuggestionThreadAsync(2, 10, 200, 2000, DateTimeOffset.UtcNow, CancellationToken.None);

        int trackedCount =
            await db.MetaProposalStates.CountAsync(state => state.SuggestionThreadChannelId == 10,
                CancellationToken.None);

        trackedCount.ShouldBe(2);
    }

    [Test]
    public async Task PollMessageLookups_AreGuildScoped()
    {
        await using BotDbContext db = PostgresDatabaseFixture.CreateDbContext();
        MetaProposalService service = new MetaProposalService(db, Log.Logger);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await service.TrackSuggestionThreadAsync(1, 10, 100, 1000, now, CancellationToken.None);
        await service.TrackSuggestionThreadAsync(2, 10, 200, 2000, now, CancellationToken.None);

        ErrorOr<MetaProposalState> guildOneStart = await service.StartPollAsync(
            1,
            10,
            1000,
            "A",
            "summary",
            "motivation",
            "specification",
            900,
            now.AddHours(1),
            now,
            CancellationToken.None
        );
        ErrorOr<MetaProposalState> guildTwoStart = await service.StartPollAsync(
            2,
            10,
            2000,
            "B",
            "summary",
            "motivation",
            "specification",
            900,
            now.AddHours(1),
            now,
            CancellationToken.None
        );

        guildOneStart.IsError.ShouldBeFalse();
        guildTwoStart.IsError.ShouldBeFalse();

        ErrorOr<MetaProposalState> guildTwoPoll = await service.GetByPollMessageAsync(2, 900, CancellationToken.None);
        guildTwoPoll.IsError.ShouldBeFalse();
        guildTwoPoll.Value.GuildId.ShouldBe((ulong)2);

        ErrorOr<MetaProposalState> clearGuildTwo =
            await service.ClearDeletedPollByMessageAsync(2, 900, CancellationToken.None);
        clearGuildTwo.IsError.ShouldBeFalse();

        MetaProposalState guildOne =
            await db.MetaProposalStates.SingleAsync(state => state.GuildId == 1, CancellationToken.None);
        MetaProposalState guildTwo =
            await db.MetaProposalStates.SingleAsync(state => state.GuildId == 2, CancellationToken.None);

        guildOne.PollMessageId.ShouldBe((ulong)900);
        guildTwo.PollMessageId.ShouldBeNull();
    }
}