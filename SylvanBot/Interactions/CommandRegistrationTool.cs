using Discord.Rest;

namespace SylvanBot.Interactions;

internal static class CommandRegistrationTool
{
    internal static bool TryParse(string[] args, out string action, out bool global, out ulong[] guildIds)
    {
        action = string.Empty;
        global = false;
        guildIds = [];

        if (args.Length < 2 || args[0] is not ("inspect" or "clear"))
            return false;

        action = args[0];

        if (string.Equals(args[1], "global", comparisonType: StringComparison.Ordinal))
        {
            global = true;

            return args.Length == 2;
        }

        if (!string.Equals(args[1], "guild", comparisonType: StringComparison.Ordinal) || args.Length < 3)
            return false;

        List<ulong> ids = [];

        foreach (var argument in args[2..])
        {
            if (!ulong.TryParse(s: argument, out var id) || id == 0 || ids.Contains(id))
                return false;

            ids.Add(id);
        }

        guildIds = [.. ids];

        return true;
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        if (!TryParse(args: args, out var action, out var global, out var guildIds))
        {
            await Console.Error.WriteLineAsync("Usage: commands <inspect|clear> global | commands <inspect|clear> guild <guild-id> [guild-id ...]");

            return 2;
        }

        var token = Environment.GetEnvironmentVariable("DISCORD__TOKEN");

        if (string.IsNullOrWhiteSpace(token))
        {
            await Console.Error.WriteLineAsync("DISCORD__TOKEN must be set in the environment for command maintenance.");

            return 2;
        }

        try
        {
            await using var client = new DiscordRestClient();
            await client.LoginAsync(tokenType: TokenType.Bot, token: token);
            Console.WriteLine($"Authenticated as {client.CurrentUser.Username} ({client.CurrentUser.Id}).");

            if (global)
            {
                try
                {
                    IReadOnlyCollection<RestGlobalCommand> commands = await client.GetGlobalApplicationCommands(true);

                    if (string.Equals(a: action, "inspect", comparisonType: StringComparison.Ordinal))
                        PrintCommands("global", commands: commands);
                    else
                    {
                        IReadOnlyCollection<RestGlobalCommand> remaining = await client.BulkOverwriteGlobalCommands([]);

                        if (remaining.Count != 0)
                            throw new InvalidOperationException("Discord returned commands after clearing global scope.");

                        Console.WriteLine($"Cleared global scope: {commands.Count} application commands removed.");
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Failed to {action} global application commands: {ex}");

                    return 1;
                }
            }

            var failed = false;

            foreach (var guildId in guildIds)
                try
                {
                    IReadOnlyCollection<RestGuildCommand> commands = await client.GetGuildApplicationCommands(
                        guildId: guildId,
                        withLocalizations: true
                    );

                    if (string.Equals(a: action, "inspect", comparisonType: StringComparison.Ordinal))
                        PrintCommands($"guild {guildId}", commands: commands);
                    else
                    {
                        IReadOnlyCollection<RestGuildCommand> remaining = await client.BulkOverwriteGuildCommands([], guildId: guildId);

                        if (remaining.Count != 0)
                            throw new InvalidOperationException($"Discord returned commands after clearing guild {guildId} scope.");

                        Console.WriteLine($"Cleared guild {guildId} scope: {commands.Count} application commands removed.");
                    }
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Failed to {action} application commands in guild {guildId}: {ex}");

                    failed = true;
                }

            return failed ? 1 : 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Failed to authenticate command maintenance: {ex}");

            return 1;
        }
    }

    private static void PrintCommands(string scope, IReadOnlyCollection<IApplicationCommand> commands)
    {
        Console.WriteLine($"{scope}: {commands.Count} application commands.");

        foreach (var command in commands.OrderBy(command => command.Name, comparer: StringComparer.Ordinal))
        {
            Console.WriteLine($"  {command.Type} {command.Name} ({command.Id}): {command.Description}");

            Console.WriteLine($"    Default member permissions: {command.DefaultMemberPermissions}; NSFW: {command.IsNsfw}");
            PrintOptions(options: command.Options, "    ");
        }
    }

    private static void PrintOptions(IEnumerable<IApplicationCommandOption> options, string indent)
    {
        foreach (var option in options)
        {
            Console.WriteLine($"{indent}{option.Type} {option.Name} (required: {option.IsRequired}): {option.Description}");

            if (option.Choices.Count > 0)
                Console.WriteLine($"{indent}  Choices: {string.Join(", ", option.Choices.Select(choice => $"{choice.Name}={choice.Value}"))}");

            if (option.IsAutocomplete == true)
                Console.WriteLine($"{indent}  Autocomplete: true");

            PrintOptions(options: option.Options, indent + "  ");
        }
    }
}