namespace RatBot.Features.RoleColours;

public sealed class RoleColourOption
{
    // EF Core private ctor
    private RoleColourOption()
    {
    }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public ulong DisplayRoleId { get; private set; }
    public ulong GuildId { get; private set; }
    public long OptionId { get; private set; }
    public ulong SourceRoleId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static RoleColourOption Create(ulong guildId, ulong sourceRoleId, ulong displayRoleId)
    {
        if (guildId == 0)
            throw new ArgumentOutOfRangeException(nameof(guildId), guildId, "Guild is required.");

        if (sourceRoleId == displayRoleId)
            throw new ArgumentException("Source and display role IDs must be different.", nameof(displayRoleId));

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

    public void Update(ulong sourceRoleId, ulong displayRoleId)
    {
        if (sourceRoleId == displayRoleId)
            throw new ArgumentException("Source and display role IDs must be different.", nameof(displayRoleId));

        SourceRoleId = sourceRoleId;
        DisplayRoleId = displayRoleId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}