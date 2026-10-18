using System.Globalization;

namespace RatBot.Features.Quorum.Commands;

internal static class QuorumCommandResponses
{
    public static string Calculation(int eligibleVoterCount, decimal proportion, int requiredVotes)
    {
        return $"Eligible voters: {eligibleVoterCount}\n"
               + $"Proportion: {FormatProportion(proportion)}\n"
               + $"Required quorum: {requiredVotes}";
    }

    public static string Inspection(QuorumConfiguration configuration)
    {
        var roles = configuration.RoleIds.IsEmpty
            ? "none"
            : string.Join(", ", configuration.RoleIds
                .Order()
                .Select(MentionUtils.MentionRole));

        return $"Channel: {MentionUtils.MentionChannel(configuration.ChannelId)}\n"
               + $"Proportion: {FormatProportion(configuration.Proportion)}\n"
               + $"Voter roles: {roles}\n"
               + $"Complete: {(configuration.RoleIds.IsEmpty ? "no" : "yes")}";
    }

    public static string FormatProportion(decimal proportion)
    {
        return $"{(proportion * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}