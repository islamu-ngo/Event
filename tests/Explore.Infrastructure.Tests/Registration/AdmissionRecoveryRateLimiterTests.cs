using System.Reflection;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionRecoveryRateLimiterTests
{
    private static readonly Guid TenantId =
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000451");
    private static readonly DateTimeOffset WindowStart =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NormalizedIdentityExhaustsExactBucketAndResetsAtWindowBoundary()
    {
        using var limiter = CreateLimiter();

        AdmissionRecoveryRateLimitDecision first = limiter.TryAcquire(
            TenantId,
            "PERSON@EXAMPLE.TEST",
            WindowStart);
        AdmissionRecoveryRateLimitDecision second = limiter.TryAcquire(
            TenantId,
            "PERSON@EXAMPLE.TEST",
            WindowStart.AddSeconds(1));
        AdmissionRecoveryRateLimitDecision denied = limiter.TryAcquire(
            TenantId,
            "PERSON@EXAMPLE.TEST",
            WindowStart.AddSeconds(2));
        AdmissionRecoveryRateLimitDecision reset = limiter.TryAcquire(
            TenantId,
            "PERSON@EXAMPLE.TEST",
            WindowStart.AddSeconds(60));

        await Assert.That(first.Allowed).IsTrue();
        await Assert.That(second.Allowed).IsTrue();
        await Assert.That(denied).IsEqualTo(new AdmissionRecoveryRateLimitDecision(false, 58));
        await Assert.That(reset.Allowed).IsTrue();
    }

    [Test]
    public async Task PartitionStorageIsFixedAndUnnormalizedIdentityFailsClosed()
    {
        using var limiter = CreateLimiter();
        Array slots = (Array)typeof(AdmissionRecoveryRateLimiter)
            .GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(limiter)!;

        await Assert.That(slots.Length).IsEqualTo(64);
        await Assert.That(() => limiter.TryAcquire(
                TenantId,
                "person@example.test",
                WindowStart))
            .Throws<ArgumentException>();
    }

    private static AdmissionRecoveryRateLimiter CreateLimiter() =>
        new(Options.Create(new AdmissionRecoveryOptions
        {
            RateLimitBucketCount = 64,
            RateLimitPermitCount = 2,
            RateLimitWindowSeconds = 60
        }));
}
