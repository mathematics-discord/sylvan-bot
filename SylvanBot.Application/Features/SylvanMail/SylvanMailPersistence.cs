using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailPersistence
    : IEntityTypeConfiguration<SylvanMailSettings>,
        IEntityTypeConfiguration<SylvanMailConversation>,
        IEntityTypeConfiguration<SylvanMailOperation>,
        IEntityTypeConfiguration<SylvanMailDelivery>
{
    public void Configure(EntityTypeBuilder<SylvanMailSettings> builder)
    {
        builder.ToTable("sylvan_mail_settings", table => table.HasCheckConstraint("CK_sylvan_mail_single_destination", "\"Slot\" = 1"));
        builder.HasKey(item => item.GuildId);

        builder.Property(item => item.GuildId).ValueGeneratedNever();

        builder.HasIndex(item => item.Slot).IsUnique();

        builder.HasIndex(item => item.ForumId).IsUnique();
    }

    public void Configure(EntityTypeBuilder<SylvanMailConversation> builder)
    {
        builder.ToTable("sylvan_mail_conversations");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Number).UseIdentityByDefaultColumn();

        builder.HasIndex(item => item.Number).IsUnique();

        builder
           .HasIndex(item => new
            {
                item.GuildId,
                item.ForumId,
                item.UserId,
            })
           .IsUnique()
           .HasFilter("NOT \"Retired\"");

        builder.HasIndex(item => item.ThreadId).IsUnique();
    }

    public void Configure(EntityTypeBuilder<SylvanMailOperation> builder)
    {
        builder.ToTable("sylvan_mail_operations");
        builder.HasKey(item => item.SourceMessageId);

        builder.Property(item => item.SourceMessageId).ValueGeneratedNever();

        builder.Property(item => item.State).HasMaxLength(24);

        builder.Property(item => item.Detail).HasMaxLength(500);

        builder.HasOne<SylvanMailConversation>().WithMany().HasForeignKey(item => item.ConversationId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(item => item.Deliveries).WithOne().HasForeignKey(item => item.SourceMessageId);
    }

    public void Configure(EntityTypeBuilder<SylvanMailDelivery> builder)
    {
        builder.ToTable("sylvan_mail_deliveries");
        builder.HasKey(item => new { item.SourceMessageId, item.Ordinal });
    }
}