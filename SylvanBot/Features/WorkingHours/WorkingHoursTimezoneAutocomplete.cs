using NodaTime;

namespace SylvanBot.Features.WorkingHours;

public sealed class WorkingHoursTimezoneAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services
    )
    {
        var query = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = DateTimeZoneProviders
            .Tzdb.Ids.Where(id => id.Contains(value: query, comparisonType: StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => !id.StartsWith(value: query, comparisonType: StringComparison.OrdinalIgnoreCase))
            .ThenBy(id => id, comparer: StringComparer.Ordinal)
            .Take(25)
            .Select(id => new AutocompleteResult(name: id, value: id));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }
}
