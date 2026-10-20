namespace RatBot.Features.Quorum.Commands;

using System.Globalization;
using RatBot.Application.Features.Quorum;

internal static class QuorumCommandResponses
{
    public static string Calculation(QuorumCalculation calculation)
    {
        return $"Eligible voters: {calculation.EligibleVoterCount}\n"
               + $"Proportion: {FormatProportion(calculation.Proportion)}\n"
               + $"Required quorum: {calculation.RequiredVotes}";
    }

    public static string Inspection(QuorumConfiguration configuration)
    {
        IEnumerable<string> roleMentions = configuration.VoterRoles
            .Select(role => role.RoleId)
            .Order()
            .Select(MentionUtils.MentionRole);

        var roles = configuration.VoterRoles.Count == 0 ? "none" : string.Join(", ", roleMentions);

        return $"Channel: {MentionUtils.MentionChannel(configuration.ChannelId)}\n"
               + $"Proportion: {FormatProportion(configuration.Proportion)}\n"
               + $"Voter roles: {roles}\n"
               + $"Complete: {(configuration.VoterRoles.Count == 0 ? "no" : "yes")}";
    }

    public static string FormatProportion(decimal proportion)
    {
        return $"{(proportion * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}