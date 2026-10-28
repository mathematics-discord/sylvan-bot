using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Application.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailConfiguration(IDbContextFactory<BotDbContext> contexts, ISylvanMailForum forum)
{
    public async Task ConfigureAsync(ulong guildId, ulong forumId, ulong roleId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await SylvanMailSerialisation.EnterAsync(db: db, ct: ct);

        try
        {
            await forum.ValidateForumAsync(guildId: guildId, forumId: forumId, roleId: roleId, ct: ct);

            SylvanMailSettings[] settings = await db.SylvanMailSettings
                .Take(2)
                .ToArrayAsync(ct);

            var conflicting = settings
                .Any(item => item.GuildId != guildId);

            if (settings.Length > 1 || conflicting)
                throw new BotCommandException("SylvanMail already has a different guild destination. No settings changed.");

            var saved = settings.SingleOrDefault();

            if (saved is null)
            {
                saved = new SylvanMailSettings { GuildId = guildId };
                db.SylvanMailSettings.Add(saved);
            }

            saved.ForumId = forumId;
            saved.RoleId = roleId;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            await SylvanMailSerialisation.ExitAsync(db);
        }
    }
}