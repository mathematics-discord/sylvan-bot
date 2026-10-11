namespace RatBot.Features.Meta.Commands;

internal static class MetaCommandIds
{
    public const uint DefaultPollHours = 72;
    public const string ProposalModalPrefix = "meta-propose";
    public const string ResubmitPrefix = "meta-resubmit";
    public const string VetoModalPrefix = "meta-veto";

    public static string ResubmitCustomId(Guid stateId)
    {
        return $"{ResubmitPrefix}:{stateId:N}";
    }
}