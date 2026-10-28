using SylvanBot.Application.Features.WorkingHours;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursGuildState : IDisposable
{
    public SemaphoreSlim Gate { get; } = new SemaphoreSlim(1, 1);

    private Lock Sync { get; } = new Lock();
    private Dictionary<ulong, bool> Members { get; } = new Dictionary<ulong, bool>();
    private HashSet<ulong> Losses { get; } = [];
    private HashSet<ulong> EligibleRoles { get; set; } = [];
    private ulong? PingRoleId { get; set; }
    private bool Seeded { get; set; }

    public void Invalidate()
    {
        lock (Sync)
            Seeded = false;
    }

    public void Observe(IGuildUser member)
    {
        lock (Sync)
            IndexMember(member);
    }

    public void MemberLeft(ulong memberId)
    {
        lock (Sync)
        {
            if (Members.Remove(memberId))
                Losses.Add(memberId);
        }
    }

    public void ForgetMember(ulong memberId)
    {
        lock (Sync)
            Members.Remove(memberId);
    }

    public ISet<ulong> MemberIds()
    {
        lock (Sync)
            return new HashSet<ulong>(Members.Keys);
    }

    public bool ConsumeLoss(ulong memberId)
    {
        lock (Sync)
            return Losses.Remove(memberId);
    }

    public ulong[] TakeLosses(ulong? onlyMember)
    {
        lock (Sync)
        {
            if (onlyMember is { } memberId)
                return Losses.Remove(memberId) ? [memberId] : [];

            var losses = Losses
                .ToArray();
            Losses.Clear();

            return losses;
        }
    }

    public void RestoreLosses(IEnumerable<ulong> members)
    {
        lock (Sync)
            Losses.UnionWith(members);
    }

    public void Dispose() => Gate.Dispose();

    private void IndexMember(IGuildUser member)
    {
        var eligible = EligibleRoles.Overlaps(member.RoleIds);

        var lostEligibility = Members.GetValueOrDefault(member.Id) && !eligible;

        if (lostEligibility)
            Losses.Add(member.Id);

        var hasTarget = PingRoleId is { } target && member.RoleIds.Contains(target);

        if (eligible || hasTarget)
            Members[member.Id] = eligible;
        else
            Members.Remove(member.Id);
    }

    public void PublishSettingsAndSeed(WorkingHoursSettings settings, SocketGuild? guild, bool ready)
    {
        HashSet<ulong> eligibleRoles = settings.EligibleRoles
            .Select(role => role.RoleId)
            .ToHashSet();

        lock (Sync)
        {
            var changed = PingRoleId != settings.PingRoleId || !EligibleRoles.SetEquals(eligibleRoles);
            PingRoleId = settings.PingRoleId;
            EligibleRoles = eligibleRoles;

            if (changed)
                Seeded = false;

            if (Seeded || !ready || guild is null)
                return;

            var previousEligible = Members
                .Where(pair => pair.Value)
                .Select(pair => pair.Key)
                .ToArray();
            Members.Clear();

            foreach (var member in guild.Users)
            {
                IReadOnlyCollection<ulong> roles = ((IGuildUser)member).RoleIds;
                var eligible = eligibleRoles.Overlaps(roles);
                var hasTarget = settings.PingRoleId is { } target && roles.Contains(target);

                if (eligible || hasTarget)
                    Members[member.Id] = eligible;
            }

            foreach (var memberId in previousEligible)
                // A missing member is unknown; only a present member proves loss of eligibility here.
                if (guild.GetUser(memberId) is not null && !Members.GetValueOrDefault(memberId))
                    Losses.Add(memberId);

            Seeded = true;
        }
    }
}