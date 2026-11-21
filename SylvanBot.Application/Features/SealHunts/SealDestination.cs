using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SylvanBot.Application.Features.SealHunts;

[Table("seal_destinations")]
[PrimaryKey(nameof(GuildId), nameof(DestinationId))]
public sealed class SealDestination
{
    [Column("guild_id")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long GuildId { get; set; }

    [Column("destination_id")]
    public long DestinationId { get; set; }
}
