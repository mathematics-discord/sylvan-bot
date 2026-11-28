using System.Globalization;
using System.Text;
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
        [Summary("duration", "Required for Mute (up to 28 days) and Ban. Use e.g. 30m, 2 days, or permanent for Ban.")]
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

    [SlashCommand("edit", "Replace an active automod rule with the same pattern and a new punishment")]
    public async Task EditAsync(
        [Summary("id", "The active rule ID shown by /automod list")]
        long id,
        [Summary("action", "The action for the replacement rule")]
        AutomodAction action,
        [Summary("duration", "Required for Mute (up to 28 days) and Ban. Use e.g. 30m, 2 days, or permanent for Ban.")]
        string? duration = null)
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: true);
        string response;

        try
        {
            (Optional<long> ReplacementId, bool RuntimeRefreshed) result = await management.EditAsync(
                Context.Guild.Id,
                id,
                action,
                duration is null ? default : new Optional<string>(duration),
                lifetime.ApplicationStopping);

            response = result.ReplacementId.HasValue
                ? AutomodReply.WithRefreshStatus($"Disabled automod rule {id} and added rule {result.ReplacementId.Value}.", result.RuntimeRefreshed)
                : $"No active automod rule with ID {id} exists in this guild.";
        }
        catch (ArgumentOutOfRangeException)
        {
            response = "No rule edited. Choose Delete, Note, Mute or Ban.";
        }
        catch (FormatException ex)
        {
            response = "No rule edited. " + ex.Message;
        }
        catch (OverflowException)
        {
            response = "No rule edited. The duration is too large; choose a shorter duration.";
        }

        await AutomodReply.SendAsync(Context.Interaction, response);
    }

    [SlashCommand("disable", "Disable an active automod rule and keep it available by ID")]
    public async Task DisableAsync([Summary("id", "The active rule ID shown by /automod list")] long id)
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: true);
        (bool Disabled, bool RuntimeRefreshed) result = await management.DisableAsync(Context.Guild.Id, id, lifetime.ApplicationStopping);

        var response = result.Disabled
            ? AutomodReply.WithRefreshStatus($"Disabled automod rule {id}.", result.RuntimeRefreshed)
            : $"No active automod rule with ID {id} exists in this guild.";

        if (result is { Disabled: false, RuntimeRefreshed: false })
            response += " The runtime refresh also failed.";

        await AutomodReply.SendAsync(Context.Interaction, response);
    }

    [SlashCommand("show", "Privately view an active or disabled automod rule by ID")]
    public async Task ShowAsync([Summary("id", "The rule ID from /automod list or a ticket")] long id)
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: true);
        Optional<AutomodRule> rule = await management.FindAsync(Context.Guild.Id, id, lifetime.ApplicationStopping);

        if (!rule.TryGet(out AutomodRule? foundRule))
        {
            await AutomodReply.SendAsync(Context.Interaction, $"No automod rule with ID {id} exists in this guild.");
            return;
        }

        (var text, var attachPattern) = AutomodReply.FormatRule(foundRule);
        await using MemoryStream? pattern = attachPattern ? new MemoryStream(Encoding.UTF8.GetBytes(foundRule.Pattern)) : null;

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = text;
            properties.AllowedMentions = AllowedMentions.None;

            if (pattern is not null)
                properties.Attachments = new[] { new FileAttachment(pattern, "automod-pattern.txt") };
        });
    }

    [SlashCommand("list", "List this guild's active automod rules")]
    public async Task ListAsync()
    {
        RequireAuditLogAccess();
        await DeferAsync(ephemeral: false);
        ImmutableArray<AutomodRule> rules = await management.ListAsync(Context.Guild.Id, lifetime.ApplicationStopping);
        MessageComponent components = BuildListComponents(rules, page: 0);

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
        MessageComponent components = BuildListComponents(rules, page);

        await component.ModifyOriginalResponseAsync(properties =>
        {
            properties.Components = components;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    private MessageComponent BuildListComponents(ImmutableArray<AutomodRule> rules, int page)
    {
        (string Text, int Index, int Count) result = AutomodRuleList.CreatePage(rules, page);
        var previousId = string.Create(CultureInfo.InvariantCulture, $"automod-list:{Context.Guild.Id}:{Math.Max(0, result.Index - 1)}:prev");

        var nextId = string.Create(
            CultureInfo.InvariantCulture,
            $"automod-list:{Context.Guild.Id}:{Math.Min(result.Count - 1, result.Index + 1)}:next");

        ActionRowBuilder controls = new ActionRowBuilder()
            .WithButton("Prev", previousId, ButtonStyle.Secondary, disabled: result.Index == 0)
            .WithButton("Next", nextId, ButtonStyle.Secondary, disabled: result.Index == result.Count - 1);

        ContainerBuilder container = new ContainerBuilder()
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