namespace RatBot.Infrastructure.Features.Meta.Persistence;

internal static class MetaProposalQueries
{
    public static IQueryable<MetaProposalState> ForAnyThread(this IQueryable<MetaProposalState> query, ulong guildId,
        ulong threadChannelId)
    {
        return query.Where(x =>
            x.GuildId == guildId && (x.SuggestionThreadChannelId == threadChannelId ||
                                     x.ProposalThreadChannelId == threadChannelId));
    }

    public static IQueryable<MetaSuggestionSettings> ForGuild(this IQueryable<MetaSuggestionSettings> query,
        ulong guildId)
    {
        return query.Where(x => x.GuildId == guildId);
    }

    public static IQueryable<MetaProposalState> ForId(this IQueryable<MetaProposalState> query, Guid stateId)
    {
        return query.Where(x => x.Id == stateId);
    }

    public static IQueryable<MetaProposalState> ForPollMessage(this IQueryable<MetaProposalState> query, ulong guildId,
        ulong pollMessageId)
    {
        return query.Where(x => x.GuildId == guildId && x.PollMessageId == pollMessageId);
    }

    public static IQueryable<MetaProposalState> ForSuggestionThread(
        this IQueryable<MetaProposalState> query,
        ulong guildId,
        ulong suggestionThreadChannelId
    )
    {
        return query.Where(x => x.GuildId == guildId && x.SuggestionThreadChannelId == suggestionThreadChannelId);
    }

    public static IQueryable<MetaProposalState> PollExpiringBeforeOrAt(this IQueryable<MetaProposalState> query,
        DateTimeOffset nowUtc)
    {
        return Queryable.Where(
            query,
            x => x.Status == MetaProposalStatus.PollActive && x.PollMessageId.HasValue && x.PollExpiresAtUtc.HasValue &&
                 x.PollExpiresAtUtc <= nowUtc
        );
    }
}