using System.Security.Cryptography;
using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class PrivacyIdentityFenceOperationTests
{
    [Test]
    public async Task RetainedExternalIdentityNeverReachesEnrollmentMutation()
    {
        var keys = new FenceTestKeyProvider();
        using PrivacyIdentityFenceKey key = await keys.ResolveAsync(CancellationToken.None);
        var account = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:erased");
        var authority = new FenceTestAuthority();
        DateTime now = DateTime.UtcNow;
        authority.Retained = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1,
            PrivacyErasureSubjectKind.User, Guid.CreateVersion7(), PrivacyErasureReasonCode.AccountDeletion,
            1, now, now, now.AddDays(90), [key.Fingerprint(account)]);
        var operation = Create(authority, keys);
        bool written = false;

        await Assert.That(() => operation.ExecuteEnrollmentAsync(account, _ =>
        {
            written = true;
            return Task.FromResult(Guid.CreateVersion7());
        }, CancellationToken.None)).Throws<InvalidOperationException>();

        await Assert.That(written).IsFalse();
    }

    [Test]
    public async Task UnavailableKeyFailsBeforeIdentityLookupOrMutation()
    {
        var keys = Substitute.For<IPrivacyIdentityFenceKeyProvider>();
        keys.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns< Task<PrivacyIdentityFenceKey>>(_ => throw new InvalidOperationException("key_unavailable"));
        var authority = new FenceTestAuthority();
        var operation = Create(authority, keys);
        bool written = false;
        await Assert.That(() => operation.ExecuteEnrollmentAsync(
            new ProviderAccountKey(AuthenticationProviderKind.Google, "canonical-account"), _ =>
            {
                written = true;
                return Task.FromResult(true);
            }, CancellationToken.None)).Throws<InvalidOperationException>();
        await Assert.That(written).IsFalse();
        await Assert.That(authority.Counter.IdentityKeyId).IsNull();
    }

    private static PrivacyIdentityFenceOperation Create(
        IPrivacyIdentityFenceAuthority authority, IPrivacyIdentityFenceKeyProvider keys) =>
        new(authority, Substitute.For<IPrivacyErasureAuthority>(), keys,
            Substitute.For<IPrivacyIdentityBindingReader>());
}

internal sealed class FenceTestKeyProvider : IPrivacyIdentityFenceKeyProvider
{
    private readonly byte[] _material = RandomNumberGenerator.GetBytes(32);
    public Task<PrivacyIdentityFenceKey> ResolveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrivacyIdentityFenceKey("unit-key", _material));
}

internal sealed class FenceTestAuthority : IPrivacyIdentityFenceAuthority
{
    public PrivacyErasureCounter Counter { get; } = PrivacyErasureCounter.Start();
    public PrivacyErasureIntent? Retained { get; set; }
    public Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken) => operation(cancellationToken);
    public Task ValidateKeyAsync(string keyId, string verificationTag, CancellationToken cancellationToken)
    {
        Counter.BindIdentityKey(keyId, verificationTag);
        return Task.CompletedTask;
    }
    public Task<PrivacyErasureIntent?> FindAsync(PrivacyIdentityFingerprint fingerprint,
        CancellationToken cancellationToken) => Task.FromResult(
        Retained?.IdentityFences.Any(fence => fence.GetFingerprint() == fingerprint) == true ? Retained : null);
    public Task<bool> IsSubjectFencedAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Retained is { IsLegalHoldPseudonymized: false } && Retained.SubjectId == userId);
}
