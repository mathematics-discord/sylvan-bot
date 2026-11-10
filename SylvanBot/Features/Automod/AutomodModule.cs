using System.Globalization;
using System.Text.RegularExpressions;
using SylvanBot.Application.Features.Automod;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Automod;

using DotNext;

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
                duration is null ? default : new Optional<string>(duration),
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
        await DeferAsync(ephemeral: false);
        ImmutableArray<AutomodRule> rules = await management.ListAsync(Context.Guild.Id, lifetime.ApplicationStopping);
        var components = BuildListComponents(rules, page: 0);

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = string.Empty;
            properties.Flags = MessageFlags.ComponentsV2;
            properties.Components = components;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    [ComponentInteraction("automod-list:*:*:*", ignoreGroupNames: true)]
    public async Task ChangePageAsync(ulong guildId, int page, string direction)
    {
        RequireAuditLogAccess();

        if (Context.Interaction is not SocketMessageComponent component)
            throw new BotCommandException("This control is not an automod list.");

        var validSource = guildId == Context.Guild.Id
            && component.Message.Author.Id == Context.Client.CurrentUser.Id
            && direction is "prev" or "next";

        IEnumerable<TextDisplayComponent> listText = component.Message.Components
            .OfType<ContainerComponent>()
            .SelectMany(container => container.Components)
            .OfType<TextDisplayComponent>();

        var isRuleList = listText
            .Any(text => text.Content.StartsWith(AutomodRuleList.Heading, StringComparison.Ordinal));

        if (!validSource || !isRuleList)
            throw new BotCommandException("This control is not an automod list for this guild. Use /automod list to open one.");

        await component.DeferAsync();
        ImmutableArray<AutomodRule> rules = await management.ListAsync(Context.Guild.Id, lifetime.ApplicationStopping);
        var components = BuildListComponents(rules, page);

        await component.ModifyOriginalResponseAsync(properties =>
        {
            properties.Components = components;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    private MessageComponent BuildListComponents(ImmutableArray<AutomodRule> rules, int page)
    {
        var result = AutomodRuleList.CreatePage(rules, page);
        var previousId = string.Create(CultureInfo.InvariantCulture, $"automod-list:{Context.Guild.Id}:{Math.Max(0, result.Index - 1)}:prev");
        var nextId = string.Create(CultureInfo.InvariantCulture, $"automod-list:{Context.Guild.Id}:{Math.Min(result.Count - 1, result.Index + 1)}:next");
        var controls = new ActionRowBuilder()
            .WithButton("Prev", previousId, ButtonStyle.Secondary, disabled: result.Index == 0)
            .WithButton("Next", nextId, ButtonStyle.Secondary, disabled: result.Index == result.Count - 1);

        var container = new ContainerBuilder()
            .WithTextDisplay(new TextDisplayBuilder().WithContent(result.Text))
            .WithActionRow(controls);

        return new ComponentBuilderV2(container).Build();
    }

    private void RequireAuditLogAccess()
    {
        if (Context.User is not SocketGuildUser { GuildPermissions.ViewAuditLog: true })
            throw new BotCommandException("You need View Audit Log permission in this guild to use /automod.");
    }
}