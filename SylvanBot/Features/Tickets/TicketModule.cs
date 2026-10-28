namespace SylvanBot.Features.Tickets;

using System.Globalization;
using System.Text;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

[Group("ticket", "Moderator tickets.")]
[DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
[RequireContext(ContextType.Guild)]
public sealed class TicketModule(
    TicketWorkflow workflow,
    TicketWorker worker,
    IDiscordTicket discord,
    ILogger logger,
    IHostApplicationLifetime lifetime)
    : InteractionModuleBase<IInteractionContext>
{
    [UserCommand("Add Moderation Note")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task AddModerationNoteAsync(IUser user)
        => NoteAsync(user.Id.ToString(CultureInfo.InvariantCulture));

    [UserCommand("List Tickets")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task ListTicketsAsync(IUser user)
        => ExecuteAsync((actor, cancellationToken) =>
            ShowHistoryAsync(actor, user.Id, page: 1, hidden: false, cancellationToken));

    [MessageCommand("Edit Ticket")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task EditTicketAsync(IMessage msg)
        => OpenEditorAsync((actor, cancellationToken) => workflow.PrepareNoteEditAsync(
            actor,
            new TicketMessage(msg.Channel.Id, msg.Id),
            cancellationToken));

    [SlashCommand("note", "Save a moderator note and publish it to the staff tickets channel.")]
    public Task NoteAsync(
        [Summary("user", "Subject's mention or user ID, including departed users.")] string user,
        [Summary("comment", "Moderator note; omit to open an editor.")] [MaxLength(Ticket.CommentLimit)] string? comment = null)
        => ExecuteAsync(
            async (actor, cancellationToken) =>
            {
                var subjectId = TicketReference.ParseUser(user);

                if (comment is null)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    await workflow.PrepareCreateNoteAsync(actor, subjectId, deadline.Token);

                    await RespondWithModalAsync(
                        $"ticket-note:{actor.GuildId}:{actor.UserId}:{subjectId}",
                        new TicketCommentModal { Title = "Add ticket note" });

                    return;
                }

                var change = await workflow.CreateNoteAsync(actor, subjectId, comment, cancellationToken);
                await ReplyToChangeAsync(change);
            },
            defer: comment is not null);

    [ModalInteraction("ticket-note:*:*:*", true)]
    public Task SubmitNoteAsync(ulong guildId, ulong ownerId, ulong subjectId, TicketCommentModal modal)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            if (actor.GuildId != guildId || actor.UserId != ownerId)
                throw new BotCommandException(
                    "This note form belongs to another moderator or server. Nothing was changed.");

            var change = await workflow.CreateNoteAsync(actor, subjectId, modal.Comment, cancellationToken);
            await ReplyToChangeAsync(change);
        });

    [SlashCommand("show", "Look up a user's tickets or one visible ticket number.")]
    public Task ShowAsync(
        [Summary("user", "User mention or ID, including departed users.")] string? user = null,
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long? id = null,
        [Summary("message", "Ticket message link in this server.")] string? message = null,
        [Summary("page", "User results page, starting at 1.")] int page = 1)
        => ExecuteAsync((actor, cancellationToken) => ShowAsync(
            actor,
            user,
            id,
            message,
            page,
            hidden: false,
            cancellationToken));

    [SlashCommand("showhidden", "Look up hidden tickets for a user or one ticket number.")]
    public Task ShowHiddenAsync(
        [Summary("user", "User mention or ID, including departed users.")] string? user = null,
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long? id = null,
        [Summary("message", "Ticket message link in this server.")] string? message = null,
        [Summary("page", "User results page, starting at 1.")] int page = 1)
        => ExecuteAsync((actor, cancellationToken) => ShowAsync(
            actor,
            user,
            id,
            message,
            page,
            hidden: true,
            cancellationToken));

    [SlashCommand("set", "Replace supplied note or automatically recorded moderation-ticket fields.")]
    public Task SetNoteAsync(
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id,
        [Summary("duration", "Duration from the action time, or permanent for bans. Changes active moderation actions.")] string? duration = null,
        [Summary("comment", "Replacement comment; omitted means unchanged.")] [MaxLength(Ticket.CommentLimit)] string? comment = null,
        [Summary("hidden", "Whether to hide this note; omitted means unchanged.")] bool? hidden = null)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.SetAsync(
                actor,
                id,
                duration,
                comment,
                hidden,
                cancellationToken: cancellationToken);

            await ReplyToChangeAsync(change, hiding: hidden == true);
            worker.Wake();
        });

    [SlashCommand("top", "Request DM redelivery of the oldest ticket in your moderator queue.")]
    public Task TopAsync()
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            await workflow.RedeliverAsync(actor, cancellationToken);
            worker.Wake();
            await ReplyAsync("Redelivery requested. The ticket stays queued if your DMs are unavailable.");
        });

    [SlashCommand("append", "Append text to a note, preserving the existing comment.")]
    public Task AppendNoteAsync(
        [Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id,
        [Summary("comment", "Text to append; the complete comment must fit within the ticket comment limit.")] [MaxLength(Ticket.CommentLimit)] string comment)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.AppendNoteAsync(actor, id, comment, cancellationToken);
            await ReplyToChangeAsync(change);
        });

    [SlashCommand("edit", "Open a note's existing comment in an editor.")]
    public Task EditAsync([Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id)
        => OpenEditorAsync((actor, cancellationToken) => workflow.PrepareNoteEditAsync(actor, id, cancellationToken));

    [ComponentInteraction("ticket:edit:*", true)]
    public Task EditButtonAsync(long id) => EditAsync(id);

    private Task OpenEditorAsync(Func<TicketActor, CancellationToken, Task<Ticket>> prepareEdit)
        => ExecuteAsync(
            async (actor, cancellationToken) =>
            {
                // Opening a modal must be the initial response; it cannot follow a deferral.
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(2));
                var ticket = await prepareEdit(actor, deadline.Token);

                if (ticket.Comment?.Length > Ticket.CommentLimit)
                {
                    await ReplyAsync(
                        $"This stored comment exceeds the {Ticket.CommentLimit:N0}-character limit and has been left intact. "
                        + $"Use /ticket set id:{ticket.Number} comment: to replace it with a comment of at most {Ticket.CommentLimit:N0} characters, "
                        + "or set hidden: without supplying comment: to change visibility and retain the full text.");

                    return;
                }

                await RespondWithModalAsync(
                    $"ticket-edit:{actor.GuildId}:{actor.UserId}:{ticket.Number}:{ticket.Version}",
                    new TicketEditModal
                    {
                        Title = $"Edit Ticket #{ticket.Number}",
                        Comment = ticket.Comment ?? string.Empty,
                        Hidden = ticket.Hidden,
                    });
            },
            defer: false);

    [ModalInteraction("ticket-edit:*:*:*:*", true)]
    public Task SubmitEditAsync(ulong guildId, ulong ownerId, long number, int version, TicketEditModal modal)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            if (actor.GuildId != guildId || actor.UserId != ownerId)
                throw new BotCommandException("This note editor belongs to another moderator or server. Nothing was changed.");

            var change = await workflow.SetNoteAsync(
                actor,
                number,
                comment: modal.Comment,
                hidden: modal.Hidden,
                expectedVersion: version,
                cancellationToken: cancellationToken);

            await ReplyToChangeAsync(change, hiding: modal.Hidden);
        });

    [SlashCommand("hide", "Hide a note and remove its Sylvan-authored publication where possible.")]
    public Task HideNoteAsync([Summary("id", "Positive ticket number in this server.")] [MinValue(1)] long id)
        => ExecuteAsync(async (actor, cancellationToken) =>
        {
            var change = await workflow.HideNoteAsync(actor, id, cancellationToken);
            await ReplyToChangeAsync(change, hiding: true);
        });

    [ComponentInteraction("ticket:hide:*", true)]
    public Task HideButtonAsync(long id)
        => ExecuteAsync(
            async (actor, cancellationToken) =>
            {
                var change = await workflow.HideNoteAsync(actor, id, cancellationToken);
                var interaction = (IComponentInteraction)Context.Interaction;
                var response = TicketPresentation.Change(change, hiding: true);

                try
                {
                    if (interaction.Message.Flags?.HasFlag(MessageFlags.Ephemeral) == true)
                    {
                        var canViewHidden = actor.IsAdministrator || actor.CanViewAuditLog;

                        await ModifyOriginalResponseAsync(properties =>
                        {
                            properties.Content = response;
                            properties.Embed = canViewHidden ? TicketPresentation.Describe(change.Ticket) : null;

                            properties.Components = canViewHidden
                                ? TicketPresentation.Buttons(change.Ticket)
                                : new ComponentBuilder().Build();

                            properties.AllowedMentions = AllowedMentions.None;
                        });
                    }
                    else
                    {
                        await FollowupAsync(response, ephemeral: true, allowedMentions: AllowedMentions.None);
                    }
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        "Ticket {Number} in guild {GuildId}: delivering the hidden outcome failed ({FailureType})",
                        change.Ticket.Number,
                        change.Ticket.GuildId,
                        exception.GetType().Name);

                    throw new BotCommandException(response, exception);
                }
            },
            updateMessage: true);

    private async Task ShowAsync(
        TicketActor actor,
        string? user,
        long? number,
        string? message,
        int page,
        bool hidden,
        CancellationToken cancellationToken)
    {
        var supplied = (user is null ? 0 : 1) + (number is null ? 0 : 1) + (message is null ? 0 : 1);

        if (supplied != 1)
            throw new BotCommandException(
                "Supply exactly one of user: (mention or user ID), id: (ticket number), or message: (message link).");

        if (number is not null || message is not null)
        {
            DotNext.Optional<Ticket> result = number is not null
                ? await workflow.ShowAsync(actor, number.Value, hidden, cancellationToken)
                : await workflow.ShowMessageAsync(
                    actor,
                    TicketReference.ParseMessageLink(message!, actor.GuildId),
                    hidden,
                    cancellationToken);

            await ShowTicketAsync(actor, result, cancellationToken);

            return;
        }

        await ShowHistoryAsync(actor, TicketReference.ParseUser(user!), page, hidden, cancellationToken);
    }

    private async Task ShowHistoryAsync(
        TicketActor actor,
        ulong subjectId,
        int page,
        bool hidden,
        CancellationToken cancellationToken)
    {
        var resultPage = await workflow.ListAsync(actor, subjectId, page, hidden, cancellationToken);
        var text = new StringBuilder();

        IEnumerable<Ticket> detailedTickets = resultPage.Items
            .Where(ticket => hidden || !ticket.Hidden);

        foreach (var ticket in detailedTickets)
        {
            var line = $"**#{ticket.Number}** | {TicketPresentation.ActionName(ticket.Action)}";

            if (ticket.ModeratorUserId is not null)
                line += $" | <@{ticket.ModeratorUserId}>";

            var jumpLink = await JumpLinkAsync(actor, ticket, cancellationToken);

            if (jumpLink.Length > 0)
                line += $" | {jumpLink}";

            text.AppendLine(line);
        }

        if (!hidden)
        {
            var hiddenIds = resultPage.Items
                .Where(ticket => ticket.Hidden)
                .Select(ticket => $"#{ticket.Number}")
                .ToArray();

            if (hiddenIds.Length > 0)
            {
                text.AppendLine();
                text.AppendLine("**Hidden**");
                text.AppendLine(string.Join(", ", hiddenIds));
                text.AppendLine();
            }
        }

        if (resultPage.Items.IsEmpty)
            text.AppendLine("No matching tickets.");

        var command = hidden ? "showhidden" : "show";

        text.Append(
            resultPage.HasMore
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

    private async Task ShowTicketAsync(TicketActor actor, DotNext.Optional<Ticket> result, CancellationToken cancellationToken)
    {
        if (!result.TryGet(out var ticket))
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

            await FollowupWithFileAsync(
                stream,
                $"{ticket.Number}-comment.txt",
                ephemeral: true,
                allowedMentions: AllowedMentions.None);
        }
    }

    private async Task<string> JumpLinkAsync(TicketActor actor, Ticket ticket, CancellationToken cancellationToken)
    {
        DotNext.Optional<TicketMessage> result = await discord.FindMessageAsync(actor, ticket, cancellationToken);

        if (!result.TryGet(out var publication))
            return string.Empty;

        var link = TicketPresentation.Link(ticket.GuildId, publication.ChannelId, publication.MessageId);

        return $"[Jump to ticket message]({link})";
    }

    private async Task ExecuteAsync(
        Func<TicketActor, CancellationToken, Task> operation,
        bool defer = true,
        bool updateMessage = false)
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
            throw new BotCommandException("Use this command in a server.");

        var permissions = moderator.GuildPermissions;

        var actor = new TicketActor(
            Context.Guild.Id,
            moderator.Id,
            permissions.Administrator,
            permissions.ModerateMembers,
            permissions.BanMembers,
            permissions.KickMembers,
            permissions.MuteMembers,
            permissions.DeafenMembers,
            permissions.ManageRoles,
            permissions.ViewAuditLog);

        await operation(actor, lifetime.ApplicationStopping);
    }

    private async Task ReplyToChangeAsync(TicketChange change, bool hiding = false)
    {
        var response = TicketPresentation.Change(change, hiding);

        try
        {
            await ReplyAsync(response);
        }
        catch (Exception exception)
        {
            logger.Warning(
                "Ticket {Number} in guild {GuildId}: delivering the saved outcome failed ({FailureType})",
                change.Ticket.Number,
                change.Ticket.GuildId,
                exception.GetType().Name);

            // The shared handler can try a private response without losing the completed operation's outcome.
            throw new BotCommandException(response, exception);
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