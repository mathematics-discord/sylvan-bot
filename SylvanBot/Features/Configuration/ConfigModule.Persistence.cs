using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Features.RolePersistence;
using SylvanBot.Application.Infrastructure.Data;
using SylvanBot.Features.RolePersistence;
using SylvanBot.Interactions;

namespace SylvanBot.Features.Configuration;

public sealed partial class ConfigModule
{
    [Group("persistence", "Restore selected roles when members rejoin")]
    [CommandContextType(InteractionContextType.Guild)]
    [RequireContext(ContextType.Guild)]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [RequireUserPermission(GuildPermission.Administrator)]
    public sealed class PersistenceModule(IDbContextFactory<BotDbContext> contexts, RolePersistenceWorker worker, IHostApplicationLifetime lifetime)
        : SlashCommandBase
    {
        [SlashCommand("add", "Remember this role when members leave")]
        public Task AddAsync(SocketRole role) => ChangeAsync(role, add: true);

        [SlashCommand("remove", "Stop remembering this role and clear pending restorations")]
        public Task RemoveAsync(SocketRole role) => ChangeAsync(role, add: false);

        private async Task ChangeAsync(SocketRole role, bool add)
        {
            await DeferAsync(ephemeral: true);
            var ct = lifetime.ApplicationStopping;
            var guild = Context.Guild;
            var gate = worker.Gate(guild.Id);
            await gate.WaitAsync(ct);

            try
            {
                if (role.Guild.Id != guild.Id)
                {
                    await ReplyAsync("Choose a role from this server.");
                    return;
                }

                await using var db = await contexts.CreateDbContextAsync(ct);

                if (add)
                    await AddRoleAsync(role, db, ct);
                else
                    await RemoveRoleAsync(role, db, ct);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task AddRoleAsync(SocketRole role, BotDbContext db, CancellationToken ct)
        {
            var guild = Context.Guild;
            var actor = (SocketGuildUser)Context.User;

            if (!PersistentRolePolicy.IsSafe(guild, role))
            {
                await ReplyAsync("Only ordinary roles without staff or moderation permissions, including channel overrides, can be persisted.");
                return;
            }

            var cannotManageRole = !guild.CurrentUser.GuildPermissions.ManageRoles || guild.CurrentUser.Hierarchy <= role.Position;

            if (cannotManageRole)
            {
                await ReplyAsync("The bot needs Manage Roles and a highest role above the target role.");
                return;
            }

            if (actor.Id != guild.OwnerId && actor.Hierarchy <= role.Position)
            {
                await ReplyAsync("Choose a role below your highest role.");
                return;
            }

            var saved = await db.PersistentRoles.FindAsync([guild.Id, role.Id], ct);

            if (saved is null)
                db.PersistentRoles.Add(new PersistentRole { GuildId = guild.Id, RoleId = role.Id });

            await db.SaveChangesAsync(ct);

            await ReplyAsync($"Persistence enabled for <@&{role.Id}>.");
        }

        private async Task RemoveRoleAsync(SocketRole role, BotDbContext db, CancellationToken ct)
        {
            var saved = await db.PersistentRoles.FindAsync([Context.Guild.Id, role.Id], ct);

            if (saved is not null)
                db.PersistentRoles.Remove(saved);

            await db.SaveChangesAsync(ct);

            await ReplyAsync("Persistence disabled and pending restorations cleared. Current memberships are unchanged.");
        }

        [SlashCommand("list", "List roles configured for persistence")]
        public async Task ListAsync()
        {
            await DeferAsync(ephemeral: false);
            await using var db = await contexts.CreateDbContextAsync(lifetime.ApplicationStopping);

            var ids = await db.PersistentRoles
               .Where(role => role.GuildId == Context.Guild.Id)
               .Select(role => role.RoleId)
               .OrderBy(id => id)
               .ToArrayAsync(lifetime.ApplicationStopping);

            if (ids.Length == 0)
            {
                await ReplyAsync("No roles configured for persistence.", ephemeral: false);
                return;
            }

            foreach (var page in ids.Chunk(15))
            {
                List<string> rows = [];

                foreach (var id in page)
                {
                    var role = Context.Guild.GetRole(id);

                    if (role is null)
                    {
                        rows.Add($"- Deleted role {id}");
                        continue;
                    }

                    var restorationBlocked = !PersistentRolePolicy.IsSafe(Context.Guild, role)
                        || !Context.Guild.CurrentUser.GuildPermissions.ManageRoles
                        || Context.Guild.CurrentUser.Hierarchy <= role.Position;

                    var status = restorationBlocked
                        ? " - restoration blocked"
                        : string.Empty;

                    rows.Add($"- <@&{id}>{status}");
                }

                await ReplyAsync(string.Join('\n', rows), ephemeral: false);
            }
        }

        private Task<IUserMessage> ReplyAsync(string text, bool ephemeral = true) =>
            FollowupAsync(text, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
    }
}