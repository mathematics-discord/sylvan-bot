using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.WorkingHours;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursConfigurationView(DiscordSocketClient client, IDbContextFactory<BotDbContext> contexts, WorkingHoursGuildRules rules)
{
    public async Task<string> ShowAsync(ulong guildId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);

        var settings = await db.WorkingHoursSettings
            .AsNoTracking()
            .Include(item => item.EligibleRoles)
            .SingleOrDefaultAsync(item => item.GuildId == guildId, cancellationToken: ct);

        settings ??= new WorkingHoursSettings { GuildId = guildId };

        IEnumerable<string> eligible = settings.EligibleRoles
            .OrderBy(role => role.RoleId)
            .Select(role => $"<@&{role.RoleId}>");

        IEnumerable<string> exemptions = settings
            .EligibleRoles
            .Where(role => role.IsExempt)
            .OrderBy(role => role.RoleId)
            .Select(role => $"<@&{role.RoleId}>");
        var guild = client.GetGuild(guildId);
        ICollection<string> problems = rules.ConfigurationProblems(settings: settings, guild: guild);

        if (!rules.IsReady(guild))
            problems.Add("Member data is not ready; role reconciliation is pending.");

        var target = settings.PingRoleId is { } id ? $"<@&{id}>" : "unset";

        return $"Working Hours: {(settings.Enabled ? "enabled" : "disabled")}\n"
               + $"Ping role: {target}\nEligible roles: {WorkingHoursPresentation.FormatRoles(eligible)}\nExemption roles: {WorkingHoursPresentation.FormatRoles(exemptions)}\n"
               + (problems.Count == 0 ? "Configuration is valid." : string.Join(separator: '\n', values: problems));
    }
}