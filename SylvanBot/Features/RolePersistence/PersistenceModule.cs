using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RolePersistence;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Interactions;

namespace SylvanBot.Features.RolePersistence;

[Group("persistence", "Restore selected roles when members rejoin")]
[CommandContextType(InteractionContextType.Guild)]
[RequireContext(ContextType.Guild)]
[DefaultMemberPermissions(GuildPermission.ViewAuditLog)]
[RequireUserPermission(GuildPermission.ViewAuditLog)]
public sealed class PersistenceModule(IDbContextFactory<BotDbContext> contexts, RolePersistenceWorker worker, IHostApplicationLifetime lifetime)
    : SlashCommandBase
{
    [SlashCommand("add", "Remember this role when members leave")]
    public Task AddAsync(SocketRole role) => ChangeAsync(role, true);

    [SlashCommand("remove", "Stop remembering this role and clear pending restorations")]
    public Task RemoveAsync(SocketRole role) => ChangeAsync(role, false);

    private async Task ChangeAsync(SocketRole role, bool add)
    {
        await DeferAsync(ephemeral: true);
        var ct = lifetime.ApplicationStopping;
        var gate = worker.Gate(Context.Guild.Id);
        await gate.WaitAsync(ct);
        try
        {
            var guild = Context.Guild;
            var actor = (SocketGuildUser)Context.User;
            if (role.Guild.Id != guild.Id)
            {
                await ReplyAsync("Choose a role from this server.");
                return;
            }
            if (add && !PersistentRolePolicy.IsSafe(guild, role))
            {
                await ReplyAsync("Only ordinary roles without staff or moderation permissions, including channel overrides, can be persisted.");
                return;
            }
            if (add && (!guild.CurrentUser.GuildPermissions.ManageRoles || guild.CurrentUser.Hierarchy <= role.Position))
            {
                await ReplyAsync("The bot needs Manage Roles and a highest role above the target role.");
                return;
            }
            if (add && actor.Id != guild.OwnerId && actor.Hierarchy <= role.Position)
            {
                await ReplyAsync("Choose a role below your highest role.");
                return;
            }
            await using var db = await contexts.CreateDbContextAsync(ct);
            var saved = await db.PersistentRoles.FindAsync([guild.Id, role.Id], ct);
            if (add && saved is null)
                db.PersistentRoles.Add(new PersistentRole { GuildId = guild.Id, RoleId = role.Id });
            if (!add && saved is not null)
                db.PersistentRoles.Remove(saved);
            await db.SaveChangesAsync(ct);
            await ReplyAsync(
                add
                    ? $"Persistence enabled for <@&{role.Id}>."
                    : "Persistence disabled and pending restorations cleared. Current memberships are unchanged."
            );
        }
        finally
        {
            gate.Release();
        }
    }

    [SlashCommand("list", "List roles configured for persistence")]
    public async Task ListAsync()
    {
        await DeferAsync(ephemeral: true);
        await using var db = await contexts.CreateDbContextAsync(lifetime.ApplicationStopping);
        var ids = await db
            .PersistentRoles.Where(x => x.GuildId == Context.Guild.Id)
            .Select(x => x.RoleId)
            .OrderBy(x => x)
            .ToArrayAsync(lifetime.ApplicationStopping);
        if (ids.Length == 0)
        {
            await ReplyAsync("No roles configured for persistence.");
            return;
        }
        foreach (var page in ids.Chunk(15))
            await ReplyAsync(
                string.Join(
                    "\n",
                    page.Select(id =>
                        Context.Guild.GetRole(id) is { } role
                            ? $"<@&{id}>"
                                + (
                                    !PersistentRolePolicy.IsSafe(Context.Guild, role)
                                    || !Context.Guild.CurrentUser.GuildPermissions.ManageRoles
                                    || Context.Guild.CurrentUser.Hierarchy <= role.Position
                                        ? " — restoration blocked"
                                        : ""
                                )
                            : $"Deleted role {id}"
                    )
                )
            );
    }

    private Task ReplyAsync(string text) => FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
}
