using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Configuration;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class ModerationRecognitionKeyRingProviderTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    }

    [Test]
    public async Task LifecycleObservationResolvesActualMaterialOnlyThroughExactInstanceSecretContract()
    {
        var resolver = Substitute.For<ISecretResolver>();
        var trustDomain = Guid.CreateVersion7();
        var material = RandomNumberGenerator.GetBytes(32);
        var clock = new Clock();
        var now = clock.GetUtcNow();
        await using var guard = new NativeGuard(clock);
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                version = 1,
                trustDomainId = trustDomain.ToString("D"),
                ringGeneration = 1,
                issuedAt = now.AddMinutes(-1).ToString("O", CultureInfo.InvariantCulture),
                validUntil = now.AddMinutes(1).ToString("O", CultureInfo.InvariantCulture),
                keys = new[] { new { kid = "selected", generation = 1, k = Convert.ToBase64String(material)
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_'), status = "active" } }
            });
            resolver.ResolveAsync(ModerationRecognitionKeyRing.SecretSettingKey, null, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    ModerationRecognitionKeyRing.SecretSettingKey, payload, SecretSourceType.Infisical,
                    SecretScope.Instance, null, now, Guid.CreateVersion7().ToString("N"))));
            var provider = new ModerationRecognitionKeyRingProvider(resolver, clock, guard.Repository);
            using var ring = await provider.ObserveAsync(trustDomain, CancellationToken.None);
            using var borrowed = ring.BorrowWritingKey(trustDomain, "selected", 1);
            using var expected = new ModerationRecognitionKey(trustDomain, "selected", 1, material);
            await Assert.That(borrowed.VerificationTag).IsEqualTo(expected.VerificationTag);
            await Assert.That(ring.Source).IsEqualTo(SecretSourceType.Infisical);
            // Raw lifecycle observation is not recognition authority: the genuine repository
            // rejects a provider use without its original transaction and durable lifecycle.
            await Assert.That(async () => { using var refused = await provider.ResolveAsync(trustDomain, CancellationToken.None); })
                .Throws<InvalidOperationException>().WithMessage("moderation_recognition_lifecycle_unavailable");
        }
        finally { CryptographicOperations.ZeroMemory(material); }
    }

    [Test]
    [Arguments(SecretResolutionStatus.Unconfigured)]
    [Arguments(SecretResolutionStatus.Unavailable)]
    [Arguments(SecretResolutionStatus.Unauthorized)]
    [Arguments(SecretResolutionStatus.Invalid)]
    public async Task AuthorityFailureNeverProducesRandomMaterialOrFallback(SecretResolutionStatus status)
    {
        var resolver = Substitute.For<ISecretResolver>();
        var result = status switch
        {
            SecretResolutionStatus.Unconfigured => SecretResolutionResult.Unconfigured,
            SecretResolutionStatus.Unavailable => SecretResolutionResult.Unavailable,
            SecretResolutionStatus.Unauthorized => SecretResolutionResult.Unauthorized,
            _ => SecretResolutionResult.Invalid
        };
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(result);
        await using var guard = new NativeGuard(new Clock());
        var provider = new ModerationRecognitionKeyRingProvider(resolver, new Clock(), guard.Repository);
        await Assert.That(async () => { using var ring = await provider.ResolveAsync(Guid.CreateVersion7(), CancellationToken.None); })
            .Throws<InvalidOperationException>().WithMessage("moderation_recognition_ring_unavailable");
    }

    [Test]
    public async Task ProviderCancellationIsPreservedWithoutAnotherAuthorityAttempt()
    {
        var resolver = Substitute.For<ISecretResolver>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var guard = new NativeGuard(new Clock());
        var provider = new ModerationRecognitionKeyRingProvider(resolver, new Clock(), guard.Repository);
        await Assert.That(async () => { using var ring = await provider.ResolveAsync(Guid.CreateVersion7(), cancellation.Token); })
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task InvalidPayloadDoesNotExposePayloadOrIdentifiersAsExceptionDetail()
    {
        var resolver = Substitute.For<ISecretResolver>();
        var payload = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                ModerationRecognitionKeyRing.SecretSettingKey, payload, SecretSourceType.EnvironmentVariable,
                SecretScope.Instance, null, new Clock().GetUtcNow(), Guid.CreateVersion7().ToString("N"))));
        await using var guard = new NativeGuard(new Clock());
        var provider = new ModerationRecognitionKeyRingProvider(resolver, new Clock(), guard.Repository);
        await Assert.That(async () => { using var ring = await provider.ResolveAsync(Guid.CreateVersion7(), CancellationToken.None); })
            .Throws<InvalidOperationException>().WithMessage("moderation_recognition_ring_invalid");
    }

    // These source-boundary cases do not need a database. Use the real native guard rather than
    // a permissive fake; successful lifecycle/transaction behavior has genuine SQLite witnesses.
    private sealed class NativeGuard : IAsyncDisposable
    {
        private readonly ExploreDbContext context = new(new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        private readonly ModerationRecognitionKeyLifecycleReplicaOwner replica = new();
        public ModerationRecognitionKeyLifecycleRepository Repository { get; }
        public NativeGuard(TimeProvider clock)
        {
            var authority = new EmbeddedPrivacyErasureAuthorityRepository(new AuthorityFactory(),
                clock, Options.Create(new PrivacyErasureOptions()));
            Repository = new(context, authority, authority, replica, clock);
        }
        public async ValueTask DisposeAsync()
        {
            replica.Dispose();
            await context.DisposeAsync();
        }
    }

    private sealed class AuthorityFactory : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
    }
}
