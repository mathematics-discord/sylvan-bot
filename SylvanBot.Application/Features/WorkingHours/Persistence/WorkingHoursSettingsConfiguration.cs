namespace SylvanBot.Application.Features.WorkingHours.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class WorkingHoursSettingsConfiguration :
    IEntityTypeConfiguration<WorkingHoursSettings>,
    IEntityTypeConfiguration<WorkingHoursEligibleRole>
{
    public void Configure(EntityTypeBuilder<WorkingHoursSettings> builder)
    {
        builder.ToTable("working_hours_settings", table =>
        {
            table.HasCheckConstraint("CK_working_hours_settings_identity", "\"GuildId\" <> 0");
            table.HasCheckConstraint(
                "CK_working_hours_settings_target",
                "\"PingRoleId\" IS NULL OR (\"PingRoleId\" <> 0 AND \"PingRoleId\" <> \"GuildId\")");
            table.HasCheckConstraint("CK_working_hours_settings_enabled", "NOT \"Enabled\" OR \"PingRoleId\" IS NOT NULL");
        });

        builder.HasKey(settings => settings.GuildId);
        builder.Property(settings => settings.GuildId)
            .ValueGeneratedNever();
        builder.Property(settings => settings.Enabled)
            .HasDefaultValue(false);
        builder.Ignore(settings => settings.IsComplete);
        builder.HasMany(settings => settings.EligibleRoles)
            .WithOne()
            .HasForeignKey(role => role.GuildId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<WorkingHoursEligibleRole> builder)
    {
        builder.ToTable("working_hours_eligible_roles", table =>
            table.HasCheckConstraint("CK_working_hours_eligible_roles_identity", "\"RoleId\" <> 0 AND \"RoleId\" <> \"GuildId\""));

        builder.HasKey(role => new { role.GuildId, role.RoleId });
        builder.Property(role => role.RoleId)
            .ValueGeneratedNever();
    }
}
