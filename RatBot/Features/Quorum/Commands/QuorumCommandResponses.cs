using System.Globalization;
using RatBot.Features.Quorum;

namespace RatBot.Features.Quorum.Commands;

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
        string roles = configuration.VoterRoles.IsEmpty
            ? "none"
            : string.Join(", ", configuration.VoterRoles.RoleIds.Order().Select(MentionUtils.MentionRole));

        return $"Channel: {MentionUtils.MentionChannel(configuration.Scope.ChannelId)}\n"
               + $"Proportion: {FormatProportion(configuration.Proportion)}\n"
               + $"Voter roles: {roles}\n"
               + $"Complete: {(configuration.IsComplete ? "yes" : "no")}";
    }

    public static string Registration(QuorumRegistration registration)
    {
        return
            $"{(registration.Created ? "Registered" : "Updated")} quorum for {MentionUtils.MentionChannel(registration.Configuration.Scope.ChannelId)} "
            + $"at {FormatProportion(registration.Configuration.Proportion)}.";
    }

    public static string Removed(QuorumScope scope)
    {
        return $"Removed quorum configuration for {MentionUtils.MentionChannel(scope.ChannelId)}.";
    }

    public static string Role(QuorumScope scope, IRole role, bool shouldAdd)
    {
        return shouldAdd
            ? $"Added {role.Mention} as a voter role for {MentionUtils.MentionChannel(scope.ChannelId)}."
            : $"Removed {role.Mention} from the voter roles for {MentionUtils.MentionChannel(scope.ChannelId)}.";
    }

    private static string FormatProportion(QuorumProportion proportion)
    {
        return $"{(proportion.Value * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}