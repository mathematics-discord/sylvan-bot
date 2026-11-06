#pragma warning disable MA0048
namespace SylvanBot.Application.Features.SylvanMail;

public sealed record SylvanMailDestination(SylvanMailDestinationKind Kind, ulong ChannelId);

public enum SylvanMailDestinationKind
{
    MemberDm,
    StaffThread,
}
