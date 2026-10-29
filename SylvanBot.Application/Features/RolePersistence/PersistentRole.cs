using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.RolePersistence;

public sealed class PersistentRole
{
    public ulong GuildId { get; set; }
    public ulong RoleId { get; set; }
}

public sealed class PendingRole
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public ulong RoleId { get; set; }
}

public sealed class PersistentRoleConfiguration : IEntityTypeConfiguration<PersistentRole>
{
    public void Configure(EntityTypeBuilder<PersistentRole> builder)
    {
        builder.HasKey(x => new { x.GuildId, x.RoleId });
    }
}

public sealed class PendingRoleConfiguration : IEntityTypeConfiguration<PendingRole>
{
    public void Configure(EntityTypeBuilder<PendingRole> builder)
    {
        builder.HasKey(x => new
        {
            x.GuildId,
            x.UserId,
            x.RoleId,
        });
        builder.HasOne<PersistentRole>().WithMany().HasForeignKey(x => new { x.GuildId, x.RoleId }).OnDelete(DeleteBehavior.Cascade);
    }
}
