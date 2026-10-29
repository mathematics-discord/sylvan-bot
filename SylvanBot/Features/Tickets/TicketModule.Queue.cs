namespace SylvanBot.Features.Tickets;

using SylvanBot.Application.Common;
using SylvanBot.Application.Features.Tickets;

public sealed partial class TicketModule
{
    [SlashCommand("review", "Work through tickets awaiting approval.")]
    public Task ReviewAsync() => ExecuteAsync((actor, token) => ShowQueueAsync(actor, 0, token));

    [ComponentInteraction("ticket:review:*:*:*", true)]
    public Task ReviewButtonAsync(ulong owner, long after, string action)
        => ExecuteAsync(async (actor, token) =>
        {
            if (actor.UserId != owner)
                throw new BotCommandException("This review panel belongs to another moderator.");

            if (action is not ("skip" or "refresh" or "start"))
                throw new BotCommandException("Unknown review action.");

            await ShowQueueAsync(actor, after, token);
        });

    [ComponentInteraction("ticket:review-approve:*:*:*", true)]
    public Task ReviewApproveAsync(ulong owner, long number, int version)
        => ExecuteAsync(async (actor, token) =>
        {
            if (actor.UserId != owner)
                throw new BotCommandException("This review panel belongs to another moderator.");

            try
            {
                await workflow.ApproveAsync(actor, number, version, token);
            }
            catch (BotCommandException exception)
            {
                await ShowQueueAsync(actor, number - 1, token, exception.Message);
                return;
            }

            worker.Wake();
            await ShowQueueAsync(actor, number, token, $"Approved ticket #{number}.");
        });

    [ComponentInteraction("ticket:review-open:*:*", true)]
    public Task ReviewEditAsync(ulong owner, long number)
        => OpenEditorAsync((actor, token) =>
        {
            if (actor.UserId != owner || (!actor.IsAdministrator && !actor.CanViewAuditLog))
                throw new BotCommandException("This editor requires the panel's senior moderator.");

            return workflow.PrepareNoteEditAsync(actor, number, token);
        }, review: true);

    [ModalInteraction("ticket-review-edit:*:*:*:*", true)]
    public Task SaveReviewEditAsync(ulong guild, ulong owner, long number, int version, TicketModerationEditModal modal)
        => ExecuteAsync(async (actor, token) =>
        {
            if (actor.GuildId != guild || actor.UserId != owner || (!actor.IsAdministrator && !actor.CanViewAuditLog))
                throw new BotCommandException("This editor belongs to another moderator or server.");

            var details = TicketDetails.ParseFields(string.IsNullOrWhiteSpace(modal.Duration) ? null : modal.Duration, modal.Comment);
            await workflow.SetModerationAsync(actor, number, details, token, expectedVersion: version);
            worker.Wake();
            await ShowQueueAsync(actor, number - 1, token, $"Saved ticket #{number}. Review the changes before approving.");
        });

    private async Task ShowQueueAsync(TicketActor actor, long after, CancellationToken token, string? outcome = null)
    {
        var queue = await workflow.ReviewQueueAsync(actor, after, token);
        var buttons = new ComponentBuilder();
        var content = $"Ready for review: {queue.Ready} · Awaiting details or pending changes: {queue.Waiting}";
        if (outcome is not null)
            content += $"\n{outcome}";

        if (queue.Ticket is { } ticket)
        {
            buttons.WithButton("Approve & next", $"ticket:review-approve:{actor.UserId}:{ticket.Number}:{ticket.Version}", ButtonStyle.Success);
            buttons.WithButton("Edit", $"ticket:review-open:{actor.UserId}:{ticket.Number}");
            buttons.WithButton("Skip", $"ticket:review:{actor.UserId}:{ticket.Number}:skip");
            buttons.WithButton("Refresh", $"ticket:review:{actor.UserId}:{ticket.Number - 1}:refresh");
        }
        else
        {
            content += queue.Ready == 0 ? "\nNo tickets ready for approval." : "\nEnd of this pass. Skipped tickets still await review.";
            buttons.WithButton("Start again", $"ticket:review:{actor.UserId}:0:start");
            buttons.WithButton("Refresh", $"ticket:review:{actor.UserId}:{after}:refresh");
        }

        await ModifyOriginalResponseAsync(properties =>
        {
            properties.Content = content;
            properties.Embeds = queue.Ticket is null ? Array.Empty<Embed>() : new[] { TicketPresentation.Describe(queue.Ticket) };
            properties.Components = buttons.Build();
            properties.AllowedMentions = AllowedMentions.None;
        });
    }
}
