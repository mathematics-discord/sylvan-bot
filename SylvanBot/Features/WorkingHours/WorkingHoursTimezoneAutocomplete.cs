namespace SylvanBot.Features.WorkingHours;

using NodaTime;

public sealed class WorkingHoursTimezoneAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction interaction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var query = interaction.Data.Current.Value?.ToString() ?? string.Empty;

        IEnumerable<AutocompleteResult> matches = DateTimeZoneProviders.Tzdb.Ids
            .Where(id => id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => !id.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(id => id, StringComparer.Ordinal)
            .Take(25)
            .Select(id => new AutocompleteResult(id, id));

        return Task.FromResult(AutocompletionResult.FromSuccess(matches));
    }
}