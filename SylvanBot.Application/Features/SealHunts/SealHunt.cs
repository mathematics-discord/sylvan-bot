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

    [Column("role_id")]
    public long? RoleId { get; set; }
}