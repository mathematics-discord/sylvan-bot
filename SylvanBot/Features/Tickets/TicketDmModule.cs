namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

[RequireContext(ContextType.DM)]
public sealed class TicketDmModule(TicketWorkflow workflow, IDiscordTicket discord, TicketWorker worker,
    IHostApplicationLifetime lifetime) : InteractionModuleBase<IInteractionContext>
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
        var actor = await discord.GetActorAsync(guildId, ownerId, deadline.Token);
        DotNext.Optional<Ticket> result = await workflow.ShowAsync(actor, number, cancellationToken: deadline.Token);

        if (!result.TryGet(out var ticket) || ticket.ModeratorUserId != ownerId || !ticket.CanCorrect(actor))
            throw new BotCommandException("This ticket is no longer available to edit.");

        var duration = ticket.DurationKnown ? "permanent" : string.Empty;

        if (ticket.DurationSeconds is not null)
            duration = $"{ticket.DurationSeconds} seconds";

        await RespondWithModalAsync(
            $"ticket-dm-save:{guildId}:{ownerId}:{number}:{ticket.Version}",
            new TicketModerationEditModal
            {
                Title = $"Edit Ticket #{number}",
                Comment = ticket.Comment ?? string.Empty,
                Duration = duration,
            });
    }

    [ModalInteraction("ticket-dm-save:*:*:*:*", true)]
    public async Task SaveAsync(ulong guildId, ulong ownerId, long number, int version, TicketModerationEditModal modal)
    {
        if (Context.User.Id != ownerId || Context.Interaction is not IModalInteraction interaction
            || interaction.Message?.Author.Id != Context.Client.CurrentUser.Id)
            throw new BotCommandException("This ticket editor belongs to another moderator.");

        await interaction.DeferLoadingAsync(ephemeral: true);
        var cancellationToken = lifetime.ApplicationStopping;
        var actor = await discord.GetActorAsync(guildId, ownerId, cancellationToken);
        DotNext.Optional<Ticket> result = await workflow.ShowAsync(actor, number, cancellationToken: cancellationToken);

        if (!result.TryGet(out var ticket) || ticket.ModeratorUserId != ownerId)
            throw new BotCommandException("This ticket is no longer available to edit.");

        var details = TicketDetails.ParseFields(string.IsNullOrWhiteSpace(modal.Duration) ? null : modal.Duration, modal.Comment);
        var change = await workflow.SetModerationAsync(actor, number, details, cancellationToken, expectedVersion: version);
        await interaction.Message.ModifyAsync(properties =>
        {
            properties.Content = "Ticket details saved.";
            properties.Embed = TicketPresentation.Describe(change.Ticket);
            properties.Components = Buttons(change.Ticket);
            properties.AllowedMentions = AllowedMentions.None;
        });
        await ModifyOriginalResponseAsync(properties => properties.Content = change.ModerationOutcome ?? "Ticket details saved.");
        worker.Wake();
    }
}
