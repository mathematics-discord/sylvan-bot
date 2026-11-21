#pragma warning disable MA0048
namespace SylvanBot.Application.Features.SylvanMail;

public enum SylvanMailDestinationKind
{
    MemberDm,
    StaffThread,
}

public sealed record SylvanMailDestination(SylvanMailDestinationKind Kind, ulong ChannelId);