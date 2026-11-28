using System.Collections.Immutable;

namespace SylvanBot.Application.Features.Automod;

public sealed record AutomodMatchResult(Optional<AutomodMatch> Match, ImmutableArray<long> TimedOutRuleIds);