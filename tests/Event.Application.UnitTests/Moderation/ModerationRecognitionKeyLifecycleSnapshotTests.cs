using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationRecognitionKeyLifecycleSnapshotTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactInitialOrStagedObservationPreservesOnlyTheCurrentWriter(bool stageCandidate)
    {
        var clock = new Clock();
        var trust = Guid.CreateVersion7();
        var original = RandomNumberGenerator.GetBytes(32);
        var candidate = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var key = new ModerationRecognitionKey(trust, "original", 1, original);
            using var next = new ModerationRecognitionKey(trust, "candidate", 2, candidate);
            var lifecycle = ModerationRecognitionKeyLifecycle.Initialize(trust, Guid.CreateVersion7(),
                "original", 1, key.VerificationTag, 1, clock.Now, 0, 0);
            var rows = new List<object>
            {
                new { kid = "original", generation = 1, k = Encode(original), status = "active" }
            };
            if (stageCandidate)
            {
                lifecycle.Stage("candidate", 2, next.VerificationTag, 2, clock.Now);
                rows.Add(new { kid = "candidate", generation = 2, k = Encode(candidate), status = "readable" });
            }
            using var ring = ModerationRecognitionKeyRing.Parse(new ResolvedSecret(
                ModerationRecognitionKeyRing.SecretSettingKey, JsonSerializer.Serialize(new
                {
                    version = 1, trustDomainId = trust.ToString("D"), ringGeneration = lifecycle.RingGeneration,
                    issuedAt = clock.Now.ToString("O", CultureInfo.InvariantCulture),
                    validUntil = clock.Now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture), keys = rows
                }), SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, clock.Now, "selected"),
                trust, clock);

            ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, ring, clock.Now);
            using var writing = ring.BorrowWritingKey(trust, "original", 1);
            await Assert.That(writing.VerificationTag).IsEqualTo(key.VerificationTag);
            if (stageCandidate)
                await Assert.That(() => ring.BorrowWritingKey(trust, "candidate", 2))
                    .Throws<InvalidOperationException>();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    [Test]
    [Arguments("different-bytes")]
    [Arguments("old-generation")]
    [Arguments("partial-overlap")]
    [Arguments("compromised")]
    [Arguments("expired")]
    public async Task SelectedObservationCannotBypassDurableLifecycle(string defect)
    {
        var clock = new Clock();
        var trust = Guid.CreateVersion7();
        var original = RandomNumberGenerator.GetBytes(32);
        var candidate = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var key = new ModerationRecognitionKey(trust, "original", 1, original);
            using var next = new ModerationRecognitionKey(trust, "candidate", 2, candidate);
            var lifecycle = ModerationRecognitionKeyLifecycle.Initialize(trust, Guid.CreateVersion7(),
                "original", 1, key.VerificationTag, 1, clock.Now, 0, 0);
            if (defect is "old-generation" or "partial-overlap")
                lifecycle.Stage("candidate", 2, next.VerificationTag, 2, clock.Now);
            if (defect == "compromised") lifecycle.Compromise("original", clock.Now);
            var rows = new[] { new { kid = "original", generation = 1,
                k = Encode(defect == "different-bytes" ? candidate : original), status = "active" } };
            using var ring = ModerationRecognitionKeyRing.Parse(new ResolvedSecret(
                ModerationRecognitionKeyRing.SecretSettingKey, JsonSerializer.Serialize(new
                {
                    version = 1, trustDomainId = trust.ToString("D"),
                    ringGeneration = defect == "partial-overlap" ? 2 : 1,
                    issuedAt = clock.Now.ToString("O", CultureInfo.InvariantCulture),
                    validUntil = clock.Now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture), keys = rows
                }), SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, clock.Now, "selected"),
                trust, clock);
            if (defect == "expired") clock.Now = clock.Now.AddMinutes(1);
            await Assert.That(() => ModerationRecognitionKeyLifecycleSnapshot.RequireMatching(lifecycle, ring, clock.Now))
                .Throws<InvalidOperationException>().WithMessage("moderation_recognition_lifecycle_unavailable");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            CryptographicOperations.ZeroMemory(candidate);
        }
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
