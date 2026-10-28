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
        var outcome = SylvanMailDeliveryOutcome.FromFailure(confirmedParts: 2, uncertainDelivery: true, uncertainPost: false, refusal: refusal);

        // Assert
        outcome.State.ShouldBe("Uncertain");
        outcome.Detail.ShouldContain("2 confirmed parts");
    }

    [Test]
    public void FromFailure_DefiniteRefusalAfterAcceptedParts_IsPartial()
    {
        // Arrange
        Optional<string> refusal = new Optional<string>("Discord refused this part.");

        // Act
        var outcome = SylvanMailDeliveryOutcome.FromFailure(confirmedParts: 1, uncertainDelivery: true, uncertainPost: false, refusal: refusal);

        // Assert
        outcome.State.ShouldBe("Partial");
        outcome.Detail.ShouldContain("Discord refused this part.");
    }

    [Test]
    public void FromFailure_UnknownPostCreation_PreservesReconciliationRequirement()
    {
        // Arrange
        Optional<string> refusal = default;

        // Act
        var outcome = SylvanMailDeliveryOutcome.FromFailure(confirmedParts: 0, uncertainDelivery: false, uncertainPost: true, refusal: refusal);

        // Assert
        outcome.State.ShouldBe("Uncertain");
        outcome.Detail.ShouldContain("before any new post");
    }

    [Test]
    public void FromFailure_BeforeAnySend_IsUnpublished()
    {
        // Arrange
        Optional<string> refusal = default;

        // Act
        var outcome = SylvanMailDeliveryOutcome.FromFailure(confirmedParts: 0, uncertainDelivery: false, uncertainPost: false, refusal: refusal);

        // Assert
        outcome.State.ShouldBe("Unpublished");
    }
}