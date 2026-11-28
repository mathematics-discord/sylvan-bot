using SylvanBot.Application.Features.SylvanMail;

namespace SylvanBot.Tests.Features.SylvanMail;

public sealed class SylvanMailDeliveryOutcomeTests
{
    [Test]
    public void FromFailure_UnconfirmedSendAfterAcceptedParts_RemainsUncertain()
    {
        // Arrange
        Optional<string> refusal = default;

        // Act
        SylvanMailDeliveryOutcome outcome = SylvanMailDeliveryOutcome.FromFailure(
            confirmedParts: 2,
            uncertainDelivery: true,
            uncertainPost: false,
            refusal: refusal);

        // Assert
        outcome.State.ShouldBe(SylvanMailDeliveryState.Uncertain);
        outcome.Detail.ShouldContain("2 confirmed parts");
    }

    [Test]
    public void FromFailure_DefiniteRefusalAfterAcceptedParts_IsPartial()
    {
        // Arrange
        Optional<string> refusal = new Optional<string>("Discord refused this part.");

        // Act
        SylvanMailDeliveryOutcome outcome = SylvanMailDeliveryOutcome.FromFailure(
            confirmedParts: 1,
            uncertainDelivery: true,
            uncertainPost: false,
            refusal: refusal);

        // Assert
        outcome.State.ShouldBe(SylvanMailDeliveryState.Partial);
        outcome.Detail.ShouldContain("Discord refused this part.");
    }

    [Test]
    public void FromFailure_UnknownPostCreation_PreservesReconciliationRequirement()
    {
        // Arrange
        Optional<string> refusal = default;

        // Act
        SylvanMailDeliveryOutcome outcome = SylvanMailDeliveryOutcome.FromFailure(
            confirmedParts: 0,
            uncertainDelivery: false,
            uncertainPost: true,
            refusal: refusal);

        // Assert
        outcome.State.ShouldBe(SylvanMailDeliveryState.Uncertain);
        outcome.Detail.ShouldContain("before any new post");
    }

    [Test]
    public void FromFailure_BeforeAnySend_IsUnpublished()
    {
        // Arrange
        Optional<string> refusal = default;

        // Act
        SylvanMailDeliveryOutcome outcome = SylvanMailDeliveryOutcome.FromFailure(
            confirmedParts: 0,
            uncertainDelivery: false,
            uncertainPost: false,
            refusal: refusal);

        // Assert
        outcome.State.ShouldBe(SylvanMailDeliveryState.Unpublished);
    }

    [Test]
    public void RecoverInterruptedAttempt_Saved_BecomesUnpublished()
    {
        SylvanMailOperation operation = new SylvanMailOperation();
        operation.State.ShouldBe(SylvanMailDeliveryState.Saved);

        var changed = operation.RecoverInterruptedAttempt();

        changed.ShouldBeTrue();
        operation.State.ShouldBe(SylvanMailDeliveryState.Unpublished);
        operation.Detail.ShouldBe("Interrupted before delivery started. No automatic resend.");
    }

    [Test]
    public void RecoverInterruptedAttempt_Sending_BecomesUncertain()
    {
        SylvanMailOperation operation = new SylvanMailOperation();
        operation.StartSendingPart(ordinal: 0);

        var changed = operation.RecoverInterruptedAttempt();

        changed.ShouldBeTrue();
        operation.State.ShouldBe(SylvanMailDeliveryState.Uncertain);
        operation.Detail.ShouldBe("Interrupted delivery: some content may have arrived. No automatic resend.");
    }

    [Test]
    public void RecoverInterruptedAttempt_Delivered_KeepsStateAndDetail()
    {
        SylvanMailOperation operation = new SylvanMailOperation();
        operation.MarkDelivered();
        var detail = operation.Detail;

        var changed = operation.RecoverInterruptedAttempt();

        changed.ShouldBeFalse();
        operation.State.ShouldBe(SylvanMailDeliveryState.Delivered);
        operation.Detail.ShouldBe(detail);
    }
}