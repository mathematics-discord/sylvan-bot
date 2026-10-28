using System.Collections.Concurrent;

namespace SylvanBot.Features.WorkingHours;

/// <summary>Owns guild state shared by commands, reconciliation and gateway observations.</summary>
public sealed class WorkingHoursRuntime : IDisposable
{
    private readonly ConcurrentDictionary<ulong, WorkingHoursGuildState> _guilds = new ConcurrentDictionary<ulong, WorkingHoursGuildState>();

    public WorkingHoursGuildState ForGuild(ulong guildId) => _guilds.GetOrAdd(guildId, _ => new WorkingHoursGuildState());

    public void DataReady(ulong? guildId = null)
    {
        foreach (var (id, state) in _guilds)
        {
            if (!guildId.HasValue || id == guildId.Value)
                state.Invalidate();
        }
    }

    public void MemberChanged(SocketGuildUser member)
    {
        if (_guilds.TryGetValue(member.Guild.Id, out var state))
            state.Observe(member);
    }

    public void MemberLeft(ulong guildId, ulong memberId)
    {
        if (_guilds.TryGetValue(guildId, out var state))
            state.MemberLeft(memberId);
    }

    public void Dispose()
    {
        foreach (var state in _guilds.Values)
            state.Dispose();
    }
}