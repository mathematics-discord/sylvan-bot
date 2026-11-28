namespace SylvanBot.Application.Features.SylvanMail;

// These names are stored in sylvan_mail_operations.State.
public enum SylvanMailDeliveryState
{
    Saved,
    Sending,
    Delivered,
    Partial,
    Refused,
    Unpublished,
    Uncertain,
}