using SylvanBot.Application.Common;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed record SylvanMailDeliveryOutcome
{
    public required string State { get; init; }

    public required string Detail { get; init; }

    public static SylvanMailDeliveryOutcome FromFailure(int confirmedParts, bool uncertainDelivery, bool uncertainPost, Option<string> refusal)
    {
        var refused = refusal.Value is Some<string>;
        var ambiguous = (uncertainDelivery || uncertainPost) && !refused;

        if (ambiguous)
        {
            var detail = uncertainPost
                ? "Saved but message unpublished: post creation is uncertain. The saved attempt will be reconciled before any new post; no automatic resend."
                : $"Uncertain delivery ({confirmedParts} confirmed parts). Further content was stopped; no automatic resend.";

            return new SylvanMailDeliveryOutcome { State = "Uncertain", Detail = detail };
        }

        if (confirmedParts > 0)
        {
            var detail = refusal.Value is Some<string>(var reason)
                ? $"Partial delivery ({confirmedParts} confirmed parts). {reason}"
                : $"Partial delivery ({confirmedParts} confirmed parts); remaining content was not sent.";

            return new SylvanMailDeliveryOutcome { State = "Partial", Detail = detail };
        }

        return refusal.Value is Some<string>(var rejection)
            ? new SylvanMailDeliveryOutcome { State = "Refused", Detail = rejection }
            : new SylvanMailDeliveryOutcome
            {
                State = "Unpublished",
                Detail = "Saved but unpublished: delivery could not start. No automatic resend.",
            };
    }
}