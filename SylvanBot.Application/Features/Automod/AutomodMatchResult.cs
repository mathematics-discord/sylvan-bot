using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Automod;

public sealed record AutomodMatchResult(Optional<AutomodRule> Rule, ImmutableArray<long> TimedOutRuleIds, Optional<string> Reason);
