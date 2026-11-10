using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Automod;

public sealed class AutomodConfiguration
{
    public AutomodConfiguration(IEnumerable<AutomodRule> rules, IEnumerable<ulong> exemptRoleIds)
        : this(rules, exemptRoleIds, [])
    {
    }

    public AutomodConfiguration(IEnumerable<AutomodRule> rules, IEnumerable<ulong> exemptRoleIds, IEnumerable<AutomodAction> disabledActions, bool enabled = true)
    {
        ImmutableHashSet<AutomodAction> disabled = disabledActions.ToImmutableHashSet();

        IEnumerable<AutomodRule> enabledRules = rules
            .Where(rule => !disabled.Contains(rule.Action));

        Enabled = enabled;
        Matcher = new AutomodMatcher(enabledRules);
        ExemptRoleIds = exemptRoleIds.ToImmutableHashSet();
    }

    public static AutomodConfiguration Empty { get; } = new AutomodConfiguration([], [], [], enabled: false);

    public bool Enabled { get; }

    public AutomodMatcher Matcher { get; }

    public ImmutableHashSet<ulong> ExemptRoleIds { get; }
}