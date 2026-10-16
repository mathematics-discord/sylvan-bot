namespace RatBot.Features.TicketCollection;

// A present change with a null AfterUtc represents a cleared timeout.
public sealed record TicketTimeoutChange(DateTimeOffset? BeforeUtc, DateTimeOffset? AfterUtc);