using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.Activity;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Activity;

[Group("activity", "Manage your participation in activity XP.")]
[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
public sealed class ActivityModule(ActivityService activity, IHostApplicationLifetime lifetime) : SlashCommandBase
{
    [SlashCommand("optout", "Stop earning activity XP, erase your progress, and remove your activity roles.")]
    public Task OptOutAsync() => SetParticipationAsync(optedOut: true);

    [SlashCommand("optin", "Resume earning activity XP from zero after opting out.")]
    public Task OptInAsync() => SetParticipationAsync(optedOut: false);

    private async Task SetParticipationAsync(bool optedOut)
    {
        await DeferAsync(ephemeral: true);
        var ct = lifetime.ApplicationStopping;
        var response = optedOut ? "Activity tracking stopped and your progress erased." : "You are participating in activity XP.";

        await activity.RunAsync(
            Context.Guild.Id,
            async db =>
            {
                var member = await db.ActivityMembers.FindAsync([Context.Guild.Id, Context.User.Id], ct);

                if (!optedOut && member is not { OptedOut: true })
                    return;

                member ??= new ActivityMember { GuildId = Context.Guild.Id, UserId = Context.User.Id };

                if (db.Entry(member).State == EntityState.Detached)
                    db.ActivityMembers.Add(member);

                member.Xp = 0;
                member.OptedOut = optedOut;
                member.ResetAt = DateTimeOffset.UtcNow;
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                await db.ActivityAwards
                    .Where(row => row.GuildId == member.GuildId && row.UserId == member.UserId)
                    .ExecuteDeleteAsync(ct);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                var settings = await db.ActivitySettings.FindAsync([Context.Guild.Id], ct) ?? new ActivitySettings();

                try
                {
                    await ActivityService.ReconcileAsync(Context.Guild, member, settings, ct);
                    await db.SaveChangesAsync(ct);
                }
                catch (Discord.Net.HttpException)
                {
                    response += " Discord refused activity-role removal. Your progress is still zero; run /activity optout to retry.";
                }
            },
            ct);

        await ModifyOriginalResponseAsync(properties => properties.Content = response);
    }
}