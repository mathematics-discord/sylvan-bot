namespace SylvanBot.Features.Tickets;

using static System.Globalization.CultureInfo;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

[Group("ticket", "Moderator tickets.")]
[DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
[RequireContext(ContextType.Guild)]
public sealed class TicketModule(
    TicketCoordinator coordinator,
    IDbContextFactory<BotDbContext> contexts,
    TicketDeliveryWorker worker,
    IHostApplicationLifetime lifetime,
    ILogger logger) : InteractionModuleBase<IInteractionContext>
{
    [UserCommand("Add Moderation Note")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task AddNoteAsync(IUser user) => NoteAsync(user.Id.ToString(InvariantCulture));

    [UserCommand("List Tickets")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    [RequireUserPermission(GuildPermission.ModerateMembers)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task ListTicketsAsync(IUser user) => ExecuteAsync(
        (actor, token) => ShowAsync(actor, user.Id.ToString(InvariantCulture), number: null, message: null, page: 1, hidden: false, token),
        ephemeral: true);

    [MessageCommand("Manage Ticket")]
    [DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task ManageAsync(IMessage message) => ExecuteAsync(async (actor, token) =>
                                                                  await DisplayAsync(
                                                                      actor,
                                                                      await ShowMessageAsync(
                                                                          actor,
                                                                          new TicketMessage(message.Channel.Id, message.Id),
                                                                          hidden: false,
                                                                          token)));

    [MessageCommand("Approve Ticket")]
    [DefaultMemberPermissions(GuildPermission.ViewAuditLog)]
    [RequireUserPermission(GuildPermission.ViewAuditLog)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task ApproveMessageAsync(IMessage message) => ExecuteAsync(async (actor, token) =>
    {
        var ticket = await ShowMessageAsync(
            actor,
            new TicketMessage(message.Channel.Id, message.Id),
            hidden: false,
            token);

        if (ticket is null)
            throw new BotCommandException("No matching ticket in this view of this server.");

        await ChangedAsync(await coordinator.ApproveAsync(actor, ticket.Number, ticket.Version, token));
    });

    [MessageCommand("Revert Ticket")]
    [DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task RevertMessageAsync(IMessage message) => ExecuteAsync(async (actor, token) =>
    {
        var ticket = await ShowMessageAsync(
            actor,
            new TicketMessage(message.Channel.Id, message.Id),
            hidden: false,
            token);

        if (ticket is null)
            throw new BotCommandException("No matching ticket in this view of this server.");

        try
        {
            await ChangedAsync(await coordinator.RevertAsync(actor, ticket.Number, ticket.Version, token));
        }
        finally
        {
            worker.Wake(Context.Guild!.Id);
        }
    });

    [MessageCommand("Hide Ticket")]
    [DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
    [CommandContextType(InteractionContextType.Guild)]
    public Task HideMessageAsync(IMessage message) => ExecuteAsync(async (actor, token) =>
    {
        var ticket = await ShowMessageAsync(
            actor,
            new TicketMessage(message.Channel.Id, message.Id),
            hidden: false,
            token);

        if (ticket is null)
            throw new BotCommandException("No matching ticket in this view of this server.");

        await ChangedAsync(
            await coordinator.EditAsync(
                actor,
                ticket.Number,
                ticket.Version,
                TicketDetails.ParseFields(duration: null, comment: null),
                hidden: true,
                token));
    });

    [SlashCommand("note", "Save a moderator note and publish it to the staff tickets channel.")]
    public Task NoteAsync(
        [Summary("user", "Subject mention or user ID, including departed users.")] string user,
        [MaxLength(Ticket.CommentLimit)] string? comment = null) => ExecuteAsync(
        async (actor, token) =>
        {
            var subject = TicketReference.ParseUser(user);

            if (comment is null)
            {
                await coordinator.PrepareNoteAsync(actor, subject, token);

                await RespondWithModalAsync(
                    $"ticket-note:{actor.GuildId}:{actor.UserId}:{subject}",
                    new TicketCommentModal { Title = "Add ticket note" });
            }
            else
            {
                await ChangedAsync(await coordinator.CreateAsync(actor, subject, comment, token));
            }
        },
        defer: comment is not null);

    [ModalInteraction("ticket-note:*:*:*", true)]
    public Task SaveNoteAsync(ulong guild, ulong owner, ulong subject, TicketCommentModal modal) => ExecuteAsync(async (actor, token) =>
    {
        RequireOwner(actor, guild, owner);
        await ChangedAsync(await coordinator.CreateAsync(actor, subject, modal.Comment, token));
    });

    [SlashCommand("show", "Look up a user's tickets or one visible ticket number.")]
    public Task ShowAsync(
        string? user = null,
        [Summary("ticket_id", "Ticket ID, not a user ID.")] [MinValue(1)] long? ticketId = null,
        string? message = null,
        int page = 1)
        => ExecuteAsync((actor, token) => ShowAsync(actor, user, ticketId, message, page, hidden: false, token), ephemeral: false);

    [SlashCommand("showhidden", "Look up hidden tickets for a user or one ticket number.")]
    public Task ShowHiddenAsync(string? user = null, [MinValue(1)] long? id = null, string? message = null, int page = 1)
        => ExecuteAsync((actor, token) => ShowAsync(actor, user, id, message, page, hidden: true, token), ephemeral: false);

    [SlashCommand("repair", "Inspect or repair a ticket publication after checking the channel.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public Task RepairAsync(
        [MinValue(1)] long id,
        [Summary("message", "Adopt this existing Sylvan ticket message link.")] string? message = null,
        [Summary("resend", "Retry publication after checking that no unrecorded copy exists.")] bool resend = false)
        => ExecuteAsync(async (actor, token) =>
            await ChangedAsync(await coordinator.RepairAsync(actor, id, message, resend, token)));

    [SlashCommand("set", "Replace supplied ticket fields.")]
    public Task SetAsync(
        [MinValue(1)] long id,
        string? duration = null,
        [MaxLength(Ticket.CommentLimit)] string? comment = null,
        [Summary("hidden", "Hide the record and lift its restriction, or restore the record without reapplying it.")] bool? hidden = null)
        => ExecuteAsync(async (actor, token)
                            => await ChangedAsync(
                                await coordinator.EditAsync(actor, id, version: null, TicketDetails.ParseFields(duration, comment), hidden, token)));

    [SlashCommand("append", "Append text to a ticket, preserving the existing comment.")]
    public Task AppendAsync([MinValue(1)] long id, [MaxLength(Ticket.CommentLimit)] string comment)
        => ExecuteAsync(async (actor, token) => await ChangedAsync(
                            await coordinator.EditAsync(
                                actor,
                                id,
                                version: null,
                                new TicketDetails(new TicketDurationEdit.Keep(), new TicketCommentEdit.Append(comment)),
                                hidden: null,
                                token)));

    [SlashCommand("revert", "Lift the ticket's active restriction and keep its record.")]
    public Task RevertAsync([MinValue(1)] long id) => ExecuteAsync(async (actor, token) => await ChangedAsync(
                                                                       await coordinator.RevertAsync(actor, id, version: null, token)));

    [SlashCommand("hide", "Lift the ticket's restriction, hide its record and remove its Sylvan-authored publications.")]
    public Task HideAsync([MinValue(1)] long id) => ExecuteAsync(async (actor, token) => await ChangedAsync(
                                                                     await coordinator.EditAsync(
                                                                         actor,
                                                                         id,
                                                                         version: null,
                                                                         TicketDetails.ParseFields(duration: null, comment: null),
                                                                         hidden: true,
                                                                         token)));

    [ComponentInteraction("ticket:hide:*", true)]
    public Task HideButtonAsync(long id) => HideAsync(id);

    [ComponentInteraction("ticket:moderation-hide:*:*", true)]
    public Task HideModerationAsync(long number, int version) => ExecuteAsync(async (actor, token) => await ChangedAsync(
                                                                                  await coordinator.EditAsync(
                                                                                      actor,
                                                                                      number,
                                                                                      version,
                                                                                      TicketDetails.ParseFields(duration: null, comment: null),
                                                                                      hidden: true,
                                                                                      token)));

    [SlashCommand("top", "Request DM redelivery of the newest ticket in your moderator stack.")]
    public Task TopAsync() => ExecuteAsync(async (actor, token) =>
    {
        await coordinator.RedeliverAsync(actor, token);
        await ReplyAsync("Redelivery attempted. The ticket stays queued if your DMs are unavailable.");
    });

    [SlashCommand("edit", "Open a ticket's existing details in an editor.")]
    public Task EditAsync([MinValue(1)] long id) => OpenEditorAsync(id);

    [ComponentInteraction("ticket:edit:*", true)]
    public Task EditButtonAsync(long id) => OpenEditorAsync(id);

    private Task OpenEditorAsync(long number, ulong? reviewOwner = null) => ExecuteAsync(
        async (actor, token) =>
        {
            if (reviewOwner is { } owner && (owner != actor.UserId || actor is { IsAdministrator: false, CanViewAuditLog: false }))
                throw new BotCommandException("This review editor belongs to another moderator.");

            var ticket = await coordinator.PrepareEditAsync(actor, number, token);

            if (ticket.Comment.Length > Ticket.CommentLimit)
                throw new BotCommandException(
                    $"This historical comment exceeds the editor limit. Use /ticket set id:{number} comment: to replace it.");

            if (ticket.Action == TicketAction.Kick)
            {
                await RespondWithModalAsync(
                    $"ticket-kick-edit:{actor.GuildId}:{actor.UserId}:{number}:{ticket.Version}",
                    new TicketCommentModal { Title = $"Edit Ticket #{number}", Comment = ticket.Comment });
            }
            else if (ticket.Action == TicketAction.Note)
            {
                await RespondWithModalAsync(
                    $"ticket-edit:{actor.GuildId}:{actor.UserId}:{number}:{ticket.Version}",
                    new TicketEditModal { Title = $"Edit Ticket #{number}", Comment = ticket.Comment, Hidden = ticket.Hidden });
            }
            else
            {
                await RespondWithModalAsync(
                    $"{
                        (reviewOwner is null ? "ticket-moderation-edit" : "ticket-review-edit")
                    }:{
                        actor.GuildId
                    }:{
                        actor.UserId
                    }:{
                        number
                    }:{
                        ticket.Version
                    }",
                    new TicketModerationEditModal
                    {
                        Title = $"Edit Ticket #{number}", Comment = ticket.Comment,
                        Duration = ticket.Action is not (TicketAction.Note or TicketAction.Kick)
                            ? ticket.DurationSeconds is { } seconds ? $"{seconds} seconds" : "permanent"
                            : string.Empty,
                    });
            }
        },
        defer: false);

    [ModalInteraction("ticket-kick-edit:*:*:*:*", true)]
    public Task SaveKickAsync(ulong guild, ulong owner, long number, int version, TicketCommentModal modal) => ExecuteAsync(async (actor, token) =>
    {
        RequireOwner(actor, guild, owner);
        await ChangedAsync(await coordinator.EditAsync(actor, number, version, TicketDetails.ParseFields(null, modal.Comment), null, token));
    });

    [ModalInteraction("ticket-edit:*:*:*:*", true)]
    public Task SaveNoteEditAsync(ulong guild, ulong owner, long number, int version, TicketEditModal modal) => ExecuteAsync(async (actor, token) =>
    {
        RequireOwner(actor, guild, owner);
        var details = TicketDetails.ParseFields(duration: null, modal.Comment);
        await ChangedAsync(await coordinator.EditAsync(actor, number, version, details, modal.Hidden, token));
    });

    [ModalInteraction("ticket-moderation-edit:*:*:*:*", true)]
    public Task SaveModerationAsync(ulong guild, ulong owner, long number, int version, TicketModerationEditModal modal)
        => ExecuteAsync(async (actor, token) =>
        {
            RequireOwner(actor, guild, owner);

            var result = await coordinator.EditAsync(
                actor,
                number,
                version,
                TicketDetails.ParseFields(string.IsNullOrWhiteSpace(modal.Duration) ? null : modal.Duration, modal.Comment),
                hidden: null,
                token);

            worker.Wake(Context.Guild!.Id);
            await DisplayAsync(actor, await coordinator.ShowAsync(actor, number, hidden: false, token), result);
        });

    [ComponentInteraction("ticket:approve:*:*", true)]
    public Task ApproveAsync(long number, int version) => ExecuteAsync(async (actor, token) =>
    {
        var result = await coordinator.ApproveAsync(actor, number, version, token);
        worker.Wake(Context.Guild!.Id);
        await DisplayAsync(actor, await coordinator.ShowAsync(actor, number, hidden: false, token), result);
    });

    [SlashCommand("review", "Work through tickets awaiting approval.")]
    public Task ReviewAsync() => ExecuteAsync((actor, token) => QueueAsync(actor, 0, token));

    [ComponentInteraction(TicketReviewSummary.ReviewButtonId, true)]
    public Task OpenReviewAsync() => ReviewAsync();

    [ComponentInteraction("ticket:review:*:*:*", true)]
    public Task ReviewButtonAsync(ulong owner, long after, string action) => ExecuteAsync(async (actor, token) =>
    {
        RequireOwner(actor, actor.GuildId, owner);

        if (action is not ("skip" or "refresh" or "start"))
            throw new BotCommandException("Unknown review action.");

        await QueueAsync(actor, after, token);
    });

    [ComponentInteraction("ticket:review-approve:*:*:*", true)]
    public Task ReviewApproveAsync(ulong owner, long number, int version) => ExecuteAsync(async (actor, token) =>
    {
        RequireOwner(actor, actor.GuildId, owner);

        try
        {
            var result = await coordinator.ApproveAsync(actor, number, version, token);
            await QueueAsync(actor, number, token, result);
        }
        catch (BotCommandException exception)
        {
            await QueueAsync(actor, number - 1, token, exception.Message);
        }

        worker.Wake(Context.Guild!.Id);
    });

    [ComponentInteraction("ticket:review-open:*:*", true)]
    public Task ReviewEditAsync(ulong owner, long number) => OpenEditorAsync(number, owner);

    [ModalInteraction("ticket-review-edit:*:*:*:*", true)]
    public Task SaveReviewAsync(ulong guild, ulong owner, long number, int version, TicketModerationEditModal modal)
        => ExecuteAsync(async (actor, token) =>
        {
            RequireOwner(actor, guild, owner);

            if (actor is { IsAdministrator: false, CanViewAuditLog: false })
                throw new BotCommandException("Review requires Administrator or View Audit Log.");

            var result = await coordinator.EditAsync(
                actor,
                number,
                version,
                TicketDetails.ParseFields(string.IsNullOrWhiteSpace(modal.Duration) ? null : modal.Duration, modal.Comment),
                hidden: null,
                token);

            worker.Wake(Context.Guild!.Id);
            await QueueAsync(actor, number - 1, token, result);
        });

    private async Task QueueAsync(TicketActor actor, long after, CancellationToken token, string? outcome = null)
    {
        var queue = await coordinator.ReviewQueueAsync(actor, after, token);
        var buttons = new ComponentBuilder();
        var content = $"Ready for review: {queue.Ready} · Awaiting details or pending changes: {queue.Waiting}\n{outcome}";

        if (queue.Ticket is { } ticket)
        {
            buttons.WithButton("Approve & next", $"ticket:review-approve:{actor.UserId}:{ticket.Number}:{ticket.Version}", ButtonStyle.Success);
            buttons.WithButton("Edit", $"ticket:review-open:{actor.UserId}:{ticket.Number}");
            buttons.WithButton("Skip", $"ticket:review:{actor.UserId}:{ticket.Number}:skip");
            buttons.WithButton("Refresh", $"ticket:review:{actor.UserId}:{ticket.Number - 1}:refresh");
        }
        else
        {
            content += "\nEnd of this pass. Skipped tickets still await review.";
            buttons.WithButton("Start again", $"ticket:review:{actor.UserId}:0:start");
            buttons.WithButton("Refresh", $"ticket:review:{actor.UserId}:{after}:refresh");
        }

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = content;
            properties.Embeds = queue.Ticket is { } next ? new[] { TicketPresentation.Describe(next) } : Array.Empty<Embed>();
            properties.Components = buttons.Build();
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    private async Task ShowAsync(TicketActor actor, string? user, long? number, string? message, int page, bool hidden, CancellationToken token)
    {
        if ((user is null ? 0 : 1) + (number is null ? 0 : 1) + (message is null ? 0 : 1) != 1)
            throw new BotCommandException("Supply exactly one of user:, id:, or message:.");

        if (user is null)
        {
            var ticket = number is { } id
                ? await coordinator.ShowAsync(actor, id, hidden, token)
                : await ShowMessageAsync(actor, TicketReference.ParseMessageLink(message!, actor.GuildId), hidden, token);

            await DisplayAsync(actor, ticket, ephemeral: false);

            return;
        }

        var subject = TicketReference.ParseUser(user);
        var result = await ListAsync(actor, subject, page, hidden, token);
        var text = new StringBuilder();

        foreach (var ticket in result.Items)
        {
            if (ticket.Hidden && !hidden)
            {
                text.AppendLine(InvariantCulture, $"**Hidden:** #{ticket.Number}");
            }
            else
            {
                var publication = TicketPublisher.FindMessage(actor, ticket);

                text.Append(InvariantCulture, $"**#{ticket.Number}** | {TicketPresentation.ActionName(ticket.Action)} | <@{ticket.ModeratorUserId}> ")
                   .AppendLine(
                        publication is null
                            ? string.Empty
                            : $"| {TicketPresentation.Link(actor.GuildId, publication.ChannelId, publication.MessageId)}");
            }
        }

        if (result.Items.IsEmpty)
            text.AppendLine("No matching tickets.");

        text.Append(result.HasMore ? $"Next: /ticket {(hidden ? "showhidden" : "show")} user:{subject} page:{page + 1}" : "End of results.");

        var embed = new EmbedBuilder()
           .WithTitle($"{(hidden ? "Hidden tickets" : "Tickets")} | {subject} | page {page}")
           .WithDescription(text.ToString())
           .Build();

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Embed = embed;
            properties.AllowedMentions = AllowedMentions.None;
        });
    }

    private async Task DisplayAsync(TicketActor actor, Ticket? ticket, string? outcome = null, bool ephemeral = true)
    {
        if (ticket is null)
        {
            await ReplyAsync("No matching ticket in this view of this server.");

            return;
        }

        var publication = TicketPublisher.FindMessage(actor, ticket);

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = (outcome is null ? string.Empty : outcome + "\n")
                + TicketPresentation.OperationalDetails(ticket)
                + (publication is null
                    ? string.Empty
                    : $"\n[Jump to ticket message]({
                        TicketPresentation.Link(actor.GuildId, publication.ChannelId, publication.MessageId)
                    })");

            properties.Embed = TicketPresentation.Describe(ticket);
            properties.Components = TicketPresentation.Buttons(ticket, actor);
            properties.AllowedMentions = AllowedMentions.None;
        });

        if (ticket.Comment.Length > TicketPresentation.InlineCommentLimit)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(ticket.Comment));
            await FollowupWithFileAsync(stream, $"{ticket.Number}-comment.txt", ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
        }
    }

    private async Task ExecuteAsync(Func<TicketActor, CancellationToken, Task> operation, bool defer = true, bool ephemeral = true)
    {
        using var timing = new OperationTiming(logger, "ticket.interaction", "acknowledge");

        if (defer)
        {
            if (Context.Interaction is IComponentInteraction component)
            {
                if (component.Message.Flags?.HasFlag(MessageFlags.Ephemeral) == true)
                    await component.DeferAsync();
                else
                    await component.DeferLoadingAsync(ephemeral: ephemeral);
            }
            else if (Context.Interaction is IModalInteraction modal)
            {
                if (modal.Message?.Flags?.HasFlag(MessageFlags.Ephemeral) == true)
                    await modal.DeferAsync();
                else
                    await modal.DeferLoadingAsync(ephemeral: ephemeral);
            }
            else
                await DeferAsync(ephemeral: ephemeral);
        }

        timing.Next("execute");

        if (Context.User is not IGuildUser member || Context.Guild is null)
            throw new BotCommandException("Use this command in a server.");

        var actor = TicketBoundary.Actor(member);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);

        if (!defer)
            deadline.CancelAfter(TimeSpan.FromSeconds(2));

        await operation(actor, deadline.Token);
    }

    private async Task ChangedAsync(string result)
    {
        worker.Wake(Context.Guild!.Id);
        await ReplyAsync(result);
    }

    private Task ReplyAsync(string text) => ModifyOriginalResponseAsync(properties =>
    {
        properties.Content = text;
        properties.Embeds = Array.Empty<Embed>();
        properties.Components = new ComponentBuilder().Build();
        properties.AllowedMentions = AllowedMentions.None;
    });

    private static void RequireOwner(TicketActor actor, ulong guild, ulong owner)
    {
        if (actor.GuildId != guild || actor.UserId != owner)
            throw new BotCommandException("This editor belongs to another moderator or server.");
    }

    private async Task<Ticket?> ShowMessageAsync(TicketActor actor, TicketMessage message, bool hidden, CancellationToken ct)
    {
        TicketCoordinator.RequireStaff(actor, hidden);
        await using var db = await contexts.CreateDbContextAsync(ct);
        await TicketCoordinator.EnabledAsync(db, actor.GuildId, ct);

        var ticket = await db.Tickets
            .AsNoTracking()
            .Where(value => value.GuildId == actor.GuildId
                       && value.Hidden == hidden
                       && value.Publications.Any(reference
                                                     => reference.ChannelId == message.ChannelId && reference.MessageId == message.MessageId))
            .SingleOrDefaultAsync(ct);

        return ticket;
    }

    private async Task<TicketPage> ListAsync(TicketActor actor, ulong subject, int page, bool hidden, CancellationToken ct)
    {
        if (subject == 0 || page is < 1 or > 1_000_000)
            throw new BotCommandException("Supply a valid user and a page between 1 and 1,000,000.");

        await using var db = await contexts.CreateDbContextAsync(ct);
        await TicketCoordinator.EnabledAsync(db, actor.GuildId, ct);
        TicketCoordinator.RequireStaff(actor, hidden);
        var senior = actor.IsAdministrator || actor.CanViewAuditLog;

        List<Ticket> tickets = await db.Tickets
            .AsNoTracking()
            .Where(ticket => ticket.GuildId == actor.GuildId
                       && ticket.SubjectUserId == subject
                       && (!hidden || ticket.Hidden)
                       && (senior || !ticket.Hidden))
            .OrderByDescending(ticket => ticket.CreatedAtUtc)
            .ThenByDescending(ticket => ticket.Id)
            .Skip((page - 1) * 10)
            .Take(11)
            .ToListAsync(ct);

        ImmutableArray<Ticket> items = tickets
            .Take(10)
            .ToImmutableArray();

        return new TicketPage(items, page, tickets.Count > 10);
    }

    private sealed record TicketPage(ImmutableArray<Ticket> Items, int Page, bool HasMore);
}
