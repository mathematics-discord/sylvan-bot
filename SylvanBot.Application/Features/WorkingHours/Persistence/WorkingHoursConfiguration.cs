using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.WorkingHours.Persistence;

public sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHoursPreference>
{
    public void Configure(EntityTypeBuilder<WorkingHoursPreference> builder)
    {
        builder.ToTable(
            "working_hours",
            table =>
            {
                table.HasCheckConstraint("CK_working_hours_identity", "\"GuildId\" <> 0 AND \"MemberId\" <> 0");

                const string windowConstraint = "(\"StartTime\" IS NULL AND \"EndTime\" IS NULL AND \"OffsetMinutes\" IS NULL AND \"ZoneId\" IS NULL AND NOT \"Enabled\") OR "
                    + "(\"StartTime\" IS NOT NULL AND \"EndTime\" IS NOT NULL AND "
                    + "((\"OffsetMinutes\" IS NOT NULL AND \"OffsetMinutes\" BETWEEN -1439 AND 1439 AND \"ZoneId\" IS NULL) OR "
                    + "(\"OffsetMinutes\" IS NULL AND \"ZoneId\" IS NOT NULL AND length(\"ZoneId\") > 0)))";
                table.HasCheckConstraint("CK_working_hours_window", sql: windowConstraint);
            });

        builder.HasKey(p => new { p.GuildId, p.MemberId });

        builder.Property(p => p.GuildId)
           .ValueGeneratedNever();

        builder.Property(p => p.MemberId)
           .ValueGeneratedNever();

        builder.Property(p => p.StartTime)
           .HasColumnType("text");

        builder.Property(p => p.EndTime)
           .HasColumnType("text");

        builder.Property(p => p.ZoneId)
           .HasColumnType("text");
        builder.Ignore(p => p.Window);
    }
}