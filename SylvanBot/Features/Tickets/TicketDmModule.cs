namespace SylvanBot.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

[RequireContext(ContextType.DM)]
public sealed class TicketDmModule(TicketCoordinator coordinator, TicketBoundary boundary, TicketDeliveryWorker worker, TicketLocks locks,
    IDbContextFactory<BotDbContext> contexts, IHostApplicationLifetime lifetime, ILogger logger) : InteractionModuleBase<IInteractionContext>
{
    internal static MessageComponent Buttons(Ticket ticket) => new ComponentBuilder()
        .WithButton("Edit", $"ticket-dm-edit:{ticket.GuildId}:{ticket.ModeratorUserId}:{ticket.Number}", disabled: ticket.Hidden)
        .Build();

    [ComponentInteraction("ticket-dm-edit:*:*:*", true)]
    public async Task EditAsync(ulong guildId, ulong ownerId, long number)
    {
        if (Context.User.Id != ownerId || Context.Interaction is not IComponentInteraction component
            || component.Message.Author.Id != Context.Client.CurrentUser.Id)
            throw new BotCommandException("This ticket editor belongs to another moderator.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var actor = await boundary.GetActorAsync(guildId, ownerId, deadline.Token);
        var ticket = await coordinator.ShowAsync(actor, number, false, deadline.Token);

        if (ticket is null || ticket.ModeratorUserId != ownerId || !ticket.CanCorrect(actor))
            throw new BotCommandException("This ticket is no longer available to edit.");

        if (ticket.Action is TicketAction.Note or TicketAction.Kick)
        {
            await RespondWithModalAsync(
                $"ticket-dm-kick-save:{guildId}:{ownerId}:{number}:{ticket.Version}",
                new TicketCommentModal { Title = $"Edit Ticket #{number}", Comment = ticket.Comment });
            return;
        }

        var duration = "permanent";

        if (ticket.DurationSeconds is not null)
            duration = $"{ticket.DurationSeconds} seconds";

        await RespondWithModalAsync(
            $"ticket-dm-save:{guildId}:{ownerId}:{number}:{ticket.Version}",
            new TicketModerationEditModal
            {
                Title = $"Edit Ticket #{number}",
                Comment = ticket.Comment,
                Duration = duration,
            });
    }

    [ModalInteraction("ticket-dm-kick-save:*:*:*:*", true)]
    public Task SaveKickAsync(ulong guildId, ulong ownerId, long number, int version, TicketCommentModal modal)
        => SaveAsync(guildId, ownerId, number, version, new TicketModerationEditModal { Comment = modal.Comment });

    [ModalInteraction("ticket-dm-save:*:*:*:*", true)]
    public async Task SaveAsync(ulong guildId, ulong ownerId, long number, int version, TicketModerationEditModal modal)
    {
        if (Context.User.Id != ownerId || Context.Interaction is not IModalInteraction interaction
            || interaction.Message?.Author.Id != Context.Client.CurrentUser.Id)
            throw new BotCommandException("This ticket editor belongs to another moderator.");

        using var timing = new OperationTiming(logger.ForContext("GuildId", guildId).ForContext("UserId", ownerId), "ticket.dm_save", "acknowledge");
        await interaction.DeferAsync();
        timing.Next("actor");
        var cancellationToken = lifetime.ApplicationStopping;
        var actor = await boundary.GetActorAsync(guildId, ownerId, cancellationToken);
        var ticket = await coordinator.ShowAsync(actor, number, false, cancellationToken);

        if (ticket is null || ticket.ModeratorUserId != ownerId)
            throw new BotCommandException("This ticket is no longer available to edit.");

        var details = TicketDetails.ParseFields(string.IsNullOrWhiteSpace(modal.Duration) ? null : modal.Duration, modal.Comment);
        using var moderatorGate = await locks.ModeratorAsync(ownerId, cancellationToken);
        timing.Next("save");
        var result = await coordinator.EditAsync(actor, number, version, details, null, cancellationToken);

        using (await locks.TicketAsync(ticket.Id, cancellationToken))
        {
            ticket = await coordinator.ShowAsync(actor, number, false, cancellationToken) ?? throw new BotCommandException("The ticket is no longer visible.");
            timing.Next("response");
            await interaction.ModifyOriginalResponseAsync(properties =>
            {
                properties.Content = result;
                properties.Embed = TicketPresentation.Describe(ticket);
                properties.Components = Buttons(ticket);
                properties.AllowedMentions = AllowedMentions.None;
            });

            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            await db.TicketModerators
                .Where(moderator => moderator.UserId == ownerId && moderator.ActiveTicketId == ticket.Id
                    && moderator.PromptMessageId == interaction.Message.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(moderator => moderator.PromptRevision, ticket.Version), cancellationToken);
        }

        worker.Wake(guildId);
    }
}
