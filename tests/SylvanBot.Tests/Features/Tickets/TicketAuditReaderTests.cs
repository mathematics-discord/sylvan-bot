namespace SylvanBot.Tests.Features.Tickets;

using System.Text.Json;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

public sealed class TicketAuditReaderTests
{
    [Test]
    public void Read_UncachedUsers_PreservesRawIdentities()
    {
        // Arrange
        using var document = JsonDocument.Parse("""
            {"id":"1430000000000000000","action_type":22,"target_id":"123","user_id":"456"}
            """);

        // Act
        DotNext.Optional<TicketObservation> result = TicketAuditReader.Read(1, document.RootElement, new HashSet<ulong>(), 999);

        // Assert
        result.HasValue.ShouldBeTrue();
        result.Value.SubjectUserId.ShouldBe(123UL);
        result.Value.ModeratorUserId.ShouldBe(456UL);
        result.Value.Reason.ShouldBeEmpty();
    }

    [TestCase("target_id")]
    [TestCase("user_id")]
    public void Read_MissingIdentity_ThrowsWithFieldName(string field)
    {
        // Arrange
        Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = "1430000000000000000",
            ["action_type"] = 22,
            ["target_id"] = "123",
            ["user_id"] = "456",
        };
        values.Remove(field);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));

        // Act
        void Act() => TicketAuditReader.Read(1, document.RootElement, new HashSet<ulong>(), 999);

        // Assert
        Should.Throw<InvalidDataException>(Act).Message.ShouldContain(field);
    }

    [Test]
    public void Read_RemovedTimeout_UsesExplicitNoTimeoutState()
    {
        // Arrange
        using var document = JsonDocument.Parse("""
            {"id":"1430000000000000000","action_type":24,"target_id":"123","user_id":"456",
             "changes":[{"key":"communication_disabled_until","old_value":"2026-11-01T00:00:00Z","new_value":null}]}
            """);

        // Act
        DotNext.Optional<TicketObservation> result = TicketAuditReader.Read(1, document.RootElement, new HashSet<ulong>(), 999);

        // Assert
        result.Value.Removed.ShouldBeTrue();
        (result.Value.Timeout is TicketTimeout.None).ShouldBeTrue();
        result.Value.CreatesTicket.ShouldBeFalse();
    }
}
