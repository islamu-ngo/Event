using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationNoticeStatusMetadataTests
{
    [Test]
    public async Task Metadata_tampering_cannot_retain_its_original_capture_proof()
    {
        var metadata = Metadata();
        var original = JsonSerializer.Serialize(metadata);
        var altered = JsonSerializer.Serialize(metadata with { ContestationCalendarMonths = 1 });
        var originalHash = Hash(original);
        await Assert.That(ModerationNoticeSource.StatusMetadata.Read(original, originalHash)).IsEqualTo(metadata);
        await Assert.That(ModerationNoticeSource.StatusMetadata.Read(altered, originalHash)).IsNull();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("null")]
    [Arguments("{\"HostBindingId\":null}")]
    public async Task Unsupported_or_missing_metadata_is_unavailable_not_original_proof(string json)
    {
        await Assert.That(ModerationNoticeSource.StatusMetadata.Read(json, Hash(json))).IsNull();
    }

    [Test]
    public async Task Unsupported_capture_has_no_status_proof_and_no_added_lifetime()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var effect = ModerationEffectIntent.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), 1, null, null, Guid.CreateVersion7(), 3, Guid.CreateVersion7(),
            1, Guid.CreateVersion7(), 1, 1, 1, now, null, null);
        var source = ModerationNoticeSource.Capture(effect, null, "{\"Version\":1}", now.AddMonths(7));
        await Assert.That(source.StatusMetadataJson).IsNull();
        await Assert.That(source.StatusMetadataHash).IsNull();
        await Assert.That(source.RetainUntil).IsEqualTo(new DateTimeOffset(2027, 5, 8, 12, 0, 0, TimeSpan.Zero));
        await Assert.That(source.Matches(effect)).IsTrue();
    }

    private static ModerationNoticeSource.StatusMetadata Metadata() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 6,
            new string('a', 64), new string('b', 64), new string('c', 64), new string('d', 64));

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
