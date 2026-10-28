namespace SylvanBot.Features.RoleColours;

public sealed class RoleColourOption
{
    // EF Core private ctor
    private RoleColourOption() { }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public ulong DisplayRoleId { get; private set; }
    public ulong GuildId { get; private set; }
    public long OptionId { get; private set; }
    public ulong SourceRoleId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<RoleColourOption> Create(ulong guildId, ulong sourceRoleId, ulong displayRoleId)
    {
        if (guildId == 0)
            return new Result<RoleColourOption>(OperationFailureException.Validation("RoleColourOption.GuildRequired", "Guild is required."));

        if (sourceRoleId == displayRoleId)
        {
            return new Result<RoleColourOption>(
                OperationFailureException.Validation("RoleColourOption.RolesMustDiffer", "Source and display role IDs must be different."));
        }

        var now = DateTimeOffset.UtcNow;

        return new RoleColourOption
        {
            GuildId = guildId,
            SourceRoleId = sourceRoleId,
            DisplayRoleId = displayRoleId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public Result<ValueTuple> Update(ulong sourceRoleId, ulong displayRoleId)
    {
        if (sourceRoleId == displayRoleId)
        {
            return new Result<ValueTuple>(
                OperationFailureException.Validation("RoleColourOption.RolesMustDiffer", "Source and display role IDs must be different."));
        }

        SourceRoleId = sourceRoleId;
        DisplayRoleId = displayRoleId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return new Result<ValueTuple>(default(ValueTuple));
    }
}
