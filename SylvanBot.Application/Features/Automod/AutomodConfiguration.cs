using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Automod;

public sealed class AutomodConfiguration
{
    public static AutomodConfiguration Empty { get; } = new AutomodConfiguration([], []);

    public AutomodConfiguration(IEnumerable<AutomodRule> rules, IEnumerable<ulong> exemptRoleIds)
        : this(rules, exemptRoleIds, [])
    {
    }

    public AutomodConfiguration(IEnumerable<AutomodRule> rules, IEnumerable<ulong> exemptRoleIds, IEnumerable<AutomodAction> disabledActions)
    {
        ImmutableHashSet<AutomodAction> disabled = disabledActions.ToImmutableHashSet();
        IEnumerable<AutomodRule> enabledRules = rules
            .Where(rule => !disabled.Contains(rule.Action));

        Matcher = new AutomodMatcher(enabledRules);
        ExemptRoleIds = exemptRoleIds.ToImmutableHashSet();
    }

    public AutomodMatcher Matcher { get; }

    public ImmutableHashSet<ulong> ExemptRoleIds { get; }
}
