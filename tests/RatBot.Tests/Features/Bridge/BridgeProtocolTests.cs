namespace RatBot.Tests.Features.Bridge;

using System.Text;
using System.Text.Json;
using RatBot.Features.Bridge;

public sealed class BridgeProtocolTests
{
    [TestCase("typing", "")]
    [TestCase("reply", ",\"text\":\"Hello\"")]
    public void Parse_ValidAction_PreservesCorrelationAndSnowflake(string type, string extra)
    {
        // Arrange
        var json = Encoding.UTF8.GetBytes(
            "{\"version\":1,\"type\":\"" + type + "\",\"requestId\":\"r1\",\"messageId\":\"18446744073709551615\"" + extra + "}");

        // Act
        var parsed = BridgeProtocol.Parse(json);

        // Assert
        parsed.Error.ShouldBeNull();
        parsed.Action.ShouldNotBeNull();
        parsed.Action.RequestId.ShouldBe("r1");
        parsed.Action.MessageId.ShouldBe(ulong.MaxValue);
    }

    [TestCase("{broken")]
    [TestCase("[]")]
    [TestCase("{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":123}")]
    [TestCase("{\"version\":2,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"123\"}")]
    [TestCase("{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"0\"}")]
    [TestCase("{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"0123\"}")]
    [TestCase("{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"123\",\"channelId\":\"9\"}")]
    [TestCase("{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"123\",\"messageId\":\"456\"}")]
    [TestCase("{\"version\":1,\"type\":\"typing\",\"requestId\":\"r1\",\"messageId\":\"123\",\"text\":null}")]
    [TestCase("{\"version\":1,\"type\":\"reply\",\"requestId\":\"r1\",\"messageId\":\"123\",\"text\":\"  \"}")]
    [TestCase("{\"version\":1,\"type\":\"delete\",\"requestId\":\"r1\",\"messageId\":\"123\"}")]
    public void Parse_InvalidOrUnscopedAction_RejectsFrame(string json)
    {
        // Act
        var parsed = BridgeProtocol.Parse(Encoding.UTF8.GetBytes(json));

        // Assert
        parsed.Action.ShouldBeNull();
        parsed.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public void Parse_ReplyTooLong_RejectsFrame()
    {
        // Arrange
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1, type = "reply", requestId = "r1", messageId = "123",
            text = new string('a', BridgeProtocol.MaxReplyCharacters + 1)
        });

        // Act
        var parsed = BridgeProtocol.Parse(json);

        // Assert
        parsed.Action.ShouldBeNull();
        parsed.RequestId.ShouldBe("r1");
    }

    [Test]
    public void Serialize_Message_UsesStringIdsAndReferenceIdsOnly()
    {
        // Arrange
        var message = new BridgeMessage("123", "456", "789", DateTimeOffset.UnixEpoch, "999", "Name", "Hello",
            new BridgeReference("100", "789", "456"), []);

        // Act
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(message, BridgeProtocol.JsonOptions));

        // Assert
        json.RootElement.GetProperty("messageId").GetString().ShouldBe("123");
        json.RootElement.GetProperty("replyReference").EnumerateObject().Count().ShouldBe(3);
    }
}