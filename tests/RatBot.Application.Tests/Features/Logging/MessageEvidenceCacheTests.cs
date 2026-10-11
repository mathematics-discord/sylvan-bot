using RatBot.Application.Features.Logging;
using Shouldly;

namespace RatBot.Application.Tests.Features.Logging;

[TestFixture]
public sealed class MessageEvidenceCacheTests
{
    [Test]
    public void TryGet_WhenEvidenceExpired_ReturnsFalse()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(Settings());
        DateTimeOffset now = DateTimeOffset.UtcNow;

        cache.Put(Evidence(1, 10), now, TimeSpan.FromSeconds(5));

        bool found = cache.TryGet(10, 1, now.AddSeconds(6), out MessageEvidence _);

        found.ShouldBeFalse();
        cache.Count.ShouldBe(0);
    }

    [Test]
    public void Put_EnforcesMessageCountAndAttachmentLimits()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(
            Settings(2, 1, 10, 100)
        );
        DateTimeOffset now = DateTimeOffset.UtcNow;

        cache.Put(Evidence(1, 10), now, TimeSpan.FromMinutes(5));
        cache.Put(Evidence(2, 10), now.AddSeconds(1), TimeSpan.FromMinutes(5));
        cache.Put(Evidence(3, 20), now.AddSeconds(2), TimeSpan.FromMinutes(5));

        cache.TryGet(10, 1, now.AddSeconds(2), out MessageEvidence _).ShouldBeFalse();
        cache.TryGet(10, 2, now.AddSeconds(2), out MessageEvidence second).ShouldBeTrue();
        cache.TryGet(10, 3, now.AddSeconds(2), out MessageEvidence third).ShouldBeTrue();
        second.Attachments.Count.ShouldBe(1);
        third.Attachments.Count.ShouldBe(0);
    }

    [Test]
    public void Put_WhenEntryIsRefreshed_DoesNotEvictItFromStaleQueueEntry()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(Settings(2));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        cache.Put(Evidence(1, 10), now, TimeSpan.FromMinutes(5));
        cache.Put(Evidence(2, 10), now.AddSeconds(1), TimeSpan.FromMinutes(5));
        cache.Put(Evidence(1, 10), now.AddSeconds(2), TimeSpan.FromMinutes(5));
        cache.Put(Evidence(3, 10), now.AddSeconds(3), TimeSpan.FromMinutes(5));

        cache.TryGet(10, 1, now.AddSeconds(3), out MessageEvidence _).ShouldBeTrue();
        cache.TryGet(10, 2, now.AddSeconds(3), out MessageEvidence _).ShouldBeFalse();
        cache.TryGet(10, 3, now.AddSeconds(3), out MessageEvidence _).ShouldBeTrue();
    }

    [Test]
    public void Put_EnforcesTotalAttachmentBytes()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(
            Settings(10, 1, 100, 15)
        );
        DateTimeOffset now = DateTimeOffset.UtcNow;

        cache.Put(Evidence(1, 10), now, TimeSpan.FromMinutes(5));
        cache.Put(Evidence(2, 10), now.AddSeconds(1), TimeSpan.FromMinutes(5));

        cache.TryGet(10, 1, now.AddSeconds(1), out MessageEvidence _).ShouldBeFalse();
        cache.TryGet(10, 2, now.AddSeconds(1), out MessageEvidence _).ShouldBeTrue();
    }

    [Test]
    public void Put_WhenCapacityIsSustained_EvictsOldestCurrentEntries()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(Settings(3));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (ulong messageId = 1; messageId <= 10; messageId++)
            cache.Put(Evidence(messageId, 10), now.AddSeconds(messageId), TimeSpan.FromMinutes(5));

        cache.Count.ShouldBe(3);

        for (ulong messageId = 1; messageId <= 7; messageId++)
            cache.TryGet(10, messageId, now.AddSeconds(10), out MessageEvidence _).ShouldBeFalse();

        for (ulong messageId = 8; messageId <= 10; messageId++)
            cache.TryGet(10, messageId, now.AddSeconds(10), out MessageEvidence _).ShouldBeTrue();
    }

    [Test]
    public void Put_UsesRetentionPeriodFromInsertion()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(Settings());
        DateTimeOffset now = DateTimeOffset.UtcNow;

        cache.Put(Evidence(1, 10), now, TimeSpan.FromSeconds(5));
        cache.Put(Evidence(2, 10), now, TimeSpan.FromSeconds(20));

        cache.TryGet(10, 1, now.AddSeconds(6), out MessageEvidence _).ShouldBeFalse();
        cache.TryGet(10, 2, now.AddSeconds(6), out MessageEvidence _).ShouldBeTrue();
    }

    [Test]
    public void Put_KeysEvidenceByGuildAndMessage()
    {
        MessageEvidenceCache cache = new MessageEvidenceCache(Settings());
        DateTimeOffset now = DateTimeOffset.UtcNow;

        cache.Put(Evidence(1, 10), now, TimeSpan.FromMinutes(5));

        cache.TryGet(10, 1, now, out MessageEvidence sameGuildEvidence).ShouldBeTrue();
        cache.TryGet(11, 1, now, out MessageEvidence otherGuildEvidence).ShouldBeFalse();
        sameGuildEvidence.GuildId.ShouldBe(10UL);
        otherGuildEvidence.GuildId.ShouldBe(11UL);
    }

    private static MessageEvidence Evidence(ulong messageId, int attachmentSize)
    {
        return new MessageEvidence(
            10,
            20,
            messageId,
            30,
            DateTimeOffset.UtcNow,
            "content",
            new[] { new CachedAttachmentEvidence(1, new byte[attachmentSize], "application/octet-stream") }
        );
    }

    private static EvidenceCacheSettings Settings(
        int maxCachedMessageCount = 10,
        int maxAttachmentCountPerMessage = 2,
        long maxBytesPerAttachment = 1024,
        long maxTotalCachedAttachmentBytes = 1024
    )
    {
        return new EvidenceCacheSettings(maxCachedMessageCount, maxAttachmentCountPerMessage, maxBytesPerAttachment,
            maxTotalCachedAttachmentBytes);
    }
}