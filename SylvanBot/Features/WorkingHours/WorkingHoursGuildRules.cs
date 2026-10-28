using System.Diagnostics.CodeAnalysis;
using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursGuildRules(DiscordSocketClient client)
{
    public ICollection<string> ConfigurationProblems(WorkingHoursSettings settings, SocketGuild? guild)
    {
        List<string> problems = [];

        if (!settings.PingRoleId.HasValue)
            problems.Add("Set a ping role before enabling.");

        if (settings.EligibleRoles.Count == 0)
            problems.Add("Add at least one eligible role before enabling.");

        if (client.ConnectionState != ConnectionState.Connected || guild is not { IsConnected: true })
        {
            problems.Add("Guild data is unavailable; role references and bot permissions cannot be verified.");

            return problems;
        }

        foreach (var entry in settings.EligibleRoles)
        {
            var invalidReference = entry.RoleId == guild.Id || entry.RoleId == settings.PingRoleId || guild.GetRole(entry.RoleId) is null;

            if (invalidReference)
                problems.Add($"Eligible role {entry.RoleId} is missing or invalid.");
        }

        if (settings.PingRoleId is not { } targetId)
            return problems;

        var target = guild.GetRole(targetId);
        var bot = guild.CurrentUser;

        var manageable =
            target is not null
            && target.Id != guild.Id
            && !target.IsManaged
            && bot is not null
            && bot.GuildPermissions.ManageRoles
            && bot.Hierarchy > target.Position;

        if (!manageable)
            problems.Add("The ping role is missing, @everyone, integration-managed or beyond the bot's Manage Roles permission/hierarchy.");

        return problems;
    }

    public bool IsReady([NotNullWhen(true)] SocketGuild? guild) =>
        client.ConnectionState == ConnectionState.Connected && guild is { IsConnected: true, HasAllMembers: true };

    public static bool IsEligible(WorkingHoursSettings settings, IGuildUser member)
    {
        return settings.EligibleRoles
            .Any(role => member.RoleIds.Contains(role.RoleId));
    }
}