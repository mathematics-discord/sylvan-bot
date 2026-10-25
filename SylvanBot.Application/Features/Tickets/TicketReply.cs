namespace SylvanBot.Application.Features.Tickets;

public sealed record TicketReply(ulong ChannelId, ulong MessageId, ulong? PromptMessageId, int TicketVersion);
