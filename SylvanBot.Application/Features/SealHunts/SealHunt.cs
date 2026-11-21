using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SylvanBot.Application.Features.SealHunts;

[Table("seal_hunts")]
[PrimaryKey(nameof(GuildId))]
public sealed class SealHunt
{
    [Column("guild_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long GuildId { get; set; }

    [Column("next_at")]
    public DateTimeOffset NextAt { get; set; }

    [Column("role_id")]
    public long? RoleId { get; set; }

    [Column("channel_id")]
    public long? ChannelId { get; set; }

    [Column("message_id")]
    public long? MessageId { get; set; }

    [Column("started_at")]
    public DateTimeOffset? StartedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [Column("abandoned")]
    public long[] Abandoned { get; set; } = [];
}
