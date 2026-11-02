using System.Globalization;
using System.Text.RegularExpressions;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Automod;

[Group("automod", "Manage and view this guild's automod rules")]
[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ViewAuditLog)]
[RequireUserPermission(GuildPermission.ViewAuditLog)]
public sealed class AutomodModule(AutomodManagement management, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [SlashCommand("add", "Add automod rules, one for each comma-separated pattern")]
    public async Task AddAsync(
        [Summary("string", "Comma-separated words or regexes (as inline code-blocks)")]
        string input,
        [Summary("action", "The action for each rule")]
        AutomodAction action,
        [Summary("duration", "Mute/Ban duration, e.g. 30m or 2 days; default: Mute 28 days, Ban permanent")]
        string? duration = null)
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: true);
        string response;

        try
        {
            (ImmutableArray<AutomodRule> Rules, bool RuntimeRefreshed) result = await management.AddAsync(
                Context.Guild.Id,
                input,
                action,
                duration is null ? default : new DotNext.Optional<string>(duration),
                lifetime.ApplicationStopping);

            IEnumerable<long> ids = result.Rules
                .Select(rule => rule.Id);

            response = "Added automod rule IDs (input order): " + string.Join(", ", ids);
            response = AutomodReply.WithRefreshStatus(response, result.RuntimeRefreshed);
        }
        catch (RegexMatchTimeoutException)
        {
            response = "No rules added. Regex validation timed out; simplify the pattern and try again.";
        }
        catch (ArgumentException)
        {
            response = "No rules added. Check the regex syntax and choose Delete, Note, Mute or Ban.";
        }
        catch (FormatException ex)
        {
            response = "No rules added. " + ex.Message;
        }
        catch (OverflowException)
        {
            response = "No rules added. The duration is too large; choose a shorter duration.";
        }

        await AutomodReply.SendAsync(Context.Interaction, response);
    }

    [SlashCommand("remove", "Remove an automod rule from this guild")]
    public async Task RemoveAsync([Summary("id", "The rule ID shown by /automod list")] long id)
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: true);
        var result = await management.RemoveAsync(Context.Guild.Id, id, lifetime.ApplicationStopping);

        var response = result.Removed
            ? $"Removed automod rule {id}."
            : $"No automod rule with ID {id} exists in this guild. Use /automod list to find its rules.";

        response = AutomodReply.WithRefreshStatus(response, result.RuntimeRefreshed);

        await AutomodReply.SendAsync(Context.Interaction, response);
    }

    [SlashCommand("list", "List this guild's automod rules")]
    public async Task ListAsync()
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: true);
        ImmutableArray<AutomodRule> rules = await management.ListAsync(Context.Guild.Id, lifetime.ApplicationStopping);

        IEnumerable<string> rows = rules
            .Select(rule =>
            {
                var duration = rule.Action switch
                {
                    AutomodAction.Delete or AutomodAction.Note => "no duration",
                    AutomodAction.Ban when rule.DurationSeconds is null => "permanent",
                    _ => $"{rule.DurationSeconds} seconds",
                };

                var pattern = rule.Pattern
                    .Replace("\r", "\\r", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);

                return string.Create(CultureInfo.InvariantCulture, $"{rule.Id} | {rule.MatchType} | {pattern} | {rule.Action} | {duration}");
            });

        await AutomodReply.SendAsync(Context.Interaction, rules.IsEmpty ? "No automod rules in this guild." : string.Join("\n", rows));
    }

    private void RequireAuditLogAccess()
    {
        if (Context.User is not SocketGuildUser { GuildPermissions.ViewAuditLog: true })
            throw new BotCommandException("You need View Audit Log permission in this guild to use /automod.");
    }
}