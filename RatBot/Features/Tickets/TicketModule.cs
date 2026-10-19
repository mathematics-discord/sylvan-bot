using System.Text;
using Microsoft.EntityFrameworkCore;

namespace RatBot.Features.Tickets;

[Group("ticket", "Moderator tickets.")]
[DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
[RequireContext(ContextType.Guild)]
public sealed class TicketModule(
    TicketWorkflow workflow, ITicketDiscord discord, ILogger logger, IHostApplicationLifetime lifetime)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("note", "Save a moderator note and publish it to the staff tickets channel.")]
    public Task NoteAsync(
        [Summary("user", "Subject's mention or user ID, including departed users.")] string user,
        [Summary("comment", "Moderator note.")] string comment)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.CreateAsync(actor, TicketReference.ParseUser(user), comment, cancellationToken);
            await ReplyAsync(TicketPresentation.Change(change));
        });

    [SlashCommand("show", "Look up visible tickets for a user or one ticket number.")]
    public Task ShowAsync(
        [Summary("user", "User mention or ID, including departed users.")] string? user = null,
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long? id = null,
        [Summary("message", "Ticket message link in this server.")] string? message = null,
        [Summary("page", "User results page, starting at 1.")] int page = 1)
        => ExecuteAsync((actor, cancellationToken) => ShowAsync(actor, user, id, message, page, hidden: false, cancellationToken));

    [SlashCommand("showhidden", "Look up hidden tickets for a user or one ticket number.")]
    public Task ShowHiddenAsync(
        [Summary("user", "User mention or ID, including departed users.")] string? user = null,
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long? id = null,
        [Summary("message", "Ticket message link in this server.")] string? message = null,
        [Summary("page", "User results page, starting at 1.")] int page = 1)
        => ExecuteAsync((actor, cancellationToken) => ShowAsync(actor, user, id, message, page, hidden: true, cancellationToken));

    [SlashCommand("set", "Replace supplied ticket fields. Currently supports notes only.")]
    public Task SetAsync(
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id,
        [Summary("duration", "Notes do not accept a duration.")] string? duration = null,
        [Summary("comment", "Replacement comment; omitted means unchanged.")] string? comment = null)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.SetAsync(actor, id, duration, comment, cancellationToken: cancellationToken);
            await ReplyAsync(TicketPresentation.Change(change));
        });

    [SlashCommand("append", "Append text to a note, preserving the existing comment.")]
    public Task AppendAsync(
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id,
        [Summary("comment", "Text to append, including any desired separator.")] string comment)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.AppendAsync(actor, id, comment, cancellationToken);
            await ReplyAsync(TicketPresentation.Change(change));
        });

    [SlashCommand("edit", "Open a note's existing comment in an editor.")]
    public Task EditAsync([Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id)
        => OpenEditorAsync(id);

    [ComponentInteraction("ticket:edit:*", true)]
    public Task EditButtonAsync(long id) => OpenEditorAsync(id);

    private Task OpenEditorAsync(long number)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            // Opening a modal must be the initial response; it cannot follow a deferral.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var ticket = await workflow.PrepareEditAsync(actor, number, deadline.Token);

            if (ticket.Comment?.Length > TicketPresentation.ModalCommentLimit)
            {
                await ReplyAsync("This comment exceeds the editor's 4,000-character capacity. "
                    + $"Use /ticket set id:{number} comment: to replace it, "
                    + $"or /ticket append id:{number} comment: to preserve it and add text.");
                return;
            }

            await RespondWithModalAsync(
                $"ticket-edit:{actor.GuildId}:{actor.UserId}:{ticket.Number}:{ticket.Version}",
                new TicketCommentModal { Title = $"Edit Ticket #{ticket.Number}", Comment = ticket.Comment ?? string.Empty });
        }, defer: false);

    [ModalInteraction("ticket-edit:*:*:*:*", true)]
    public Task SubmitEditAsync(ulong guildId, ulong ownerId, long number, int version, TicketCommentModal modal)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            if (actor.GuildId != guildId || actor.UserId != ownerId)
                throw new UnauthorizedAccessException();

            var change = await workflow.SetAsync(actor, number, comment: modal.Comment,
                expectedVersion: version, cancellationToken: cancellationToken);

            await ReplyAsync(TicketPresentation.Change(change));
        });

    [SlashCommand("hide", "Hide a note and remove its Sylvan-authored publication where possible.")]
    public Task HideAsync([Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.HideAsync(actor, id, cancellationToken);
            await ReplyAsync(TicketPresentation.Change(change, hiding: true));
        });

    [ComponentInteraction("ticket:hide:*", true)]
    public Task HideButtonAsync(long id)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.HideAsync(actor, id, cancellationToken);
            var interaction = (IComponentInteraction)Context.Interaction;

            if (interaction.Message.Flags?.HasFlag(MessageFlags.Ephemeral) == true)
            {
                await ModifyOriginalResponseAsync(properties =>
                {
                    properties.Content = TicketPresentation.Change(change, hiding: true);
                    properties.Embed = TicketPresentation.Describe(change.Ticket);
                    properties.Components = TicketPresentation.Buttons(change.Ticket);
                    properties.AllowedMentions = AllowedMentions.None;
                });
            }
            else
            {
                await FollowupAsync(TicketPresentation.Change(change, hiding: true),
                    ephemeral: true, allowedMentions: AllowedMentions.None);
            }
        }, updateMessage: true);

    private async Task ShowAsync(
        TicketActor actor, string? user, long? number, string? message, int page, bool hidden,
        CancellationToken cancellationToken)
    {
        var supplied = (user is null ? 0 : 1) + (number is null ? 0 : 1) + (message is null ? 0 : 1);

        if (supplied != 1)
        {
            await ReplyAsync("Supply exactly one of user: (mention or user ID), id: (ticket number), or message: (message link).");
            return;
        }

        if (number is not null || message is not null)
        {
            var result = number is not null
                ? await workflow.ShowAsync(actor, number.Value, hidden, cancellationToken)
                : await workflow.ShowMessageAsync(actor, message!, hidden, cancellationToken);

            await ShowTicketAsync(actor, result, cancellationToken);
            return;
        }

        var subjectId = TicketReference.ParseUser(user!);
        var resultPage = await workflow.ListAsync(actor, subjectId, page, hidden, cancellationToken);
        var text = new StringBuilder();

        foreach (var ticket in resultPage.Items)
        {
            var line = $"**#{ticket.Number}** | {TicketPresentation.ActionName(ticket.Action)}";

            if (ticket.ModeratorUserId is not null)
                line += $" | <@{ticket.ModeratorUserId}>";

            var jumpLink = await JumpLinkAsync(actor, ticket, cancellationToken);

            if (jumpLink.Length > 0)
                line += $" | {jumpLink}";

            text.AppendLine(line);
        }

        if (resultPage.Items.IsEmpty)
            text.AppendLine("No matching tickets.");

        var command = hidden ? "showhidden" : "show";
        text.Append(resultPage.HasMore
            ? $"Next: /ticket {command} user:{subjectId} page:{page + 1}"
            : "End of results.");

        var embed = new EmbedBuilder()
            .WithTitle($"{(hidden ? "Hidden tickets" : "Tickets")} | {subjectId} | page {page}")
            .WithDescription(text.ToString())
            .Build();

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Embed = embed;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    private async Task ShowTicketAsync(
        TicketActor actor, Option<Ticket> result, CancellationToken cancellationToken)
    {
        if (result.Value is not Some<Ticket>(var ticket))
        {
            await ReplyAsync("No matching ticket in this view of this server.");
            return;
        }

        var jumpLink = await JumpLinkAsync(actor, ticket, cancellationToken);
        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = jumpLink;
            properties.Embed = TicketPresentation.Describe(ticket);
            properties.Components = TicketPresentation.Buttons(ticket);
            properties.AllowedMentions = AllowedMentions.None;
        });

        if (ticket.Comment?.Length > TicketPresentation.InlineCommentLimit)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));
            await FollowupWithFileAsync(stream, $"{ticket.Number}-comment.txt", ephemeral: true,
                allowedMentions: AllowedMentions.None);
        }
    }

    private async Task<string> JumpLinkAsync(TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
    {
        var result = await discord.FindMessageAsync(actor, ticket, cancellationToken);

        if (result.Value is Some<TicketMessage>(var publication))
        {
            var link = TicketPresentation.Link(ticket.GuildId, publication.ChannelId, publication.MessageId);
            return $"[Jump to ticket message]({link})";
        }

        return string.Empty;
    }

    private async Task ExecuteAsync(
        Func<TicketActor, CancellationToken, Task> operation, bool defer = true, bool updateMessage = false)
    {
        if (defer)
        {
            // Component hides acknowledge the source message. Modal submissions always create a private reply.
            if (updateMessage)
                await DeferAsync();
            else if (Context.Interaction is IModalInteraction modal)
                await modal.DeferLoadingAsync(ephemeral: true);
            else
                await DeferAsync(ephemeral: true);
        }

        if (Context.User is not IGuildUser moderator || Context.Guild is null)
        {
            await ReplyAsync("Use this command in a server.");
            return;
        }

        var permissions = moderator.GuildPermissions;
        var actor = new TicketActor(Context.Guild.Id, moderator.Id, permissions.Administrator,
            permissions.ModerateMembers, permissions.BanMembers, permissions.KickMembers,
            permissions.MuteMembers, permissions.DeafenMembers, permissions.ManageRoles);

        try
        {
            await operation(actor, lifetime.ApplicationStopping);
        }
        catch (Exception exception)
        {
            var response = exception switch
            {
                DbUpdateConcurrencyException => "This ticket changed since you opened it. Read it again before editing.",
                UnauthorizedAccessException => "Staff access and channel visibility are required. "
                    + "Changing notes requires Moderate Members or Administrator; Mute Members alone is insufficient.",
                NotSupportedException => "This ticket type is read-only. Only notes can be changed or hidden.",
                KeyNotFoundException => "No matching ticket in this server.",
                ArgumentException => "Check the user ID, ticket number, message link, page and comment. "
                    + "Comments cannot be blank; notes do not accept duration changes.",
                InvalidOperationException => "The operation is unavailable. Check ticket configuration and the channel. "
                    + "Sylvan needs View Channel, Read Message History, Send Messages, Embed Links and Attach Files.",
                OperationCanceledException => "The operation was interrupted. Check the ticket before retrying.",
                _ => "The operation could not be completed. Check /ticket show and the channel before retrying.",
            };

            logger.Warning("Ticket interaction failed in guild {GuildId}, moderator {ModeratorId}: {FailureType}",
                actor.GuildId, actor.UserId, exception.GetType().Name);

            if (updateMessage)
                await FollowupAsync(response, ephemeral: true, allowedMentions: AllowedMentions.None);
            else
                await ReplyAsync(response);
        }
    }

    private Task ReplyAsync(string text)
    {
        if (!Context.Interaction.HasResponded)
            return RespondAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);

        return ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = text;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}