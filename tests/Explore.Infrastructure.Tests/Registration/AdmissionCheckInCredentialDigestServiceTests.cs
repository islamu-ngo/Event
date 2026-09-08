using System.Security.Cryptography;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionCheckInCredentialDigestServiceTests
{
    [Test]
    public async Task IssuedBearerMatchesCurrentCheckInCandidateExactly()
    {
        Guid tenantId = Guid.CreateVersion7();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var resolver = new CredentialSecretResolver(
            new Dictionary<int, string> { [7] = Convert.ToBase64String(key) });
        var options = Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 7 });
        var issuer = new AdmissionCredentialDigestService(resolver, options);
        var service = new AdmissionCheckInCredentialDigestService(resolver, options);
        AdmissionCredentialMaterial issued = await issuer.CreateAsync(
            new(tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), "AdmissionTicket", 1),
            CancellationToken.None);

        AdmissionCheckInCredentialDigest digest = await service.DigestAsync(
            new(tenantId, issued.PlaintextCredential), CancellationToken.None);

        await Assert.That(digest.Candidates.Count).IsEqualTo(1);
        await Assert.That(digest.Candidates[0].KeyVersion).IsEqualTo(7);
        await Assert.That(digest.Candidates[0].LookupDigest).IsEqualTo(issued.LookupDigest);
        await Assert.That(digest.ToString()).DoesNotContain(issued.PlaintextCredential);
        await Assert.That(digest.ToString()).DoesNotContain(issued.LookupDigest);
        await Assert.That(digest.Candidates[0].ToString()).DoesNotContain(issued.LookupDigest);
    }

    [Test]
    public async Task RotationReturnsCurrentFirstAndDeduplicatesVersions()
    {
        var keys = Enumerable.Range(1, 8).ToDictionary(
            version => version,
            _ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        AdmissionCheckInCredentialDigestService service = Service(
            new CredentialSecretResolver(keys),
            8,
            [7, 8, 6, 7, 5, 4, 3, 2, 1]);

        AdmissionCheckInCredentialDigest digest = await service.DigestAsync(
            new(Guid.CreateVersion7(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            CancellationToken.None);

        await Assert.That(digest.Candidates.Select(candidate => candidate.KeyVersion).ToArray())
            .IsEquivalentTo([8, 7, 6, 5, 4, 3, 2, 1]);
        await Assert.That(digest.Candidates[0].KeyVersion).IsEqualTo(8);
        await Assert.That(digest.Candidates[1].KeyVersion).IsEqualTo(7);
        await Assert.That(digest.Candidates.Count).IsEqualTo(8);
        await Assert.That(digest.Candidates.All(candidate => Convert.FromBase64String(candidate.LookupDigest).Length == 32))
            .IsTrue();
    }

    [Test]
    public async Task MoreThanEightUniqueVersionsFailsBeforeResolvingSecrets()
    {
        var resolver = new CredentialSecretResolver(new Dictionary<int, string>());
        AdmissionCheckInCredentialDigestService service = Service(resolver, 1, Enumerable.Range(2, 8).ToArray());

        await Assert.That(async () => await service.DigestAsync(
            new(Guid.CreateVersion7(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(resolver.ResolutionCount).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationPropagates()
    {
        AdmissionCheckInCredentialDigestService service = Service(
            new CredentialSecretResolver(new Dictionary<int, string>(), cancelResolution: true), 1);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.That(async () => await service.DigestAsync(
            new(Guid.CreateVersion7(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), source.Token))
            .Throws<OperationCanceledException>();
    }

    private static AdmissionCheckInCredentialDigestService Service(
        ISecretResolver resolver,
        int activeVersion,
        int[]? retainedVersions = null) =>
        new(resolver, Options.Create(new AdmissionCredentialOptions
        {
            ActiveKeyVersion = activeVersion,
            RetainedKeyVersions = retainedVersions ?? []
        }));

    private sealed class CredentialSecretResolver(
        IReadOnlyDictionary<int, string> keys,
        bool cancelResolution = false) : ISecretResolver
    {
        public int ResolutionCount { get; private set; }

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            ResolutionCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (cancelResolution)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(SecretResolutionResult.Unconfigured);
        }

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default)
        {
            ResolutionCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (cancelResolution)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(settingKey == SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey &&
                scope == SecretScope.Instance && scopeId is null &&
                qualifier.Length > 1 && int.TryParse(qualifier[1..], out int version) &&
                keys.TryGetValue(version, out string? value) ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, value, SecretSourceType.EnvironmentVariable, scope, scopeId, DateTime.UtcNow)) : SecretResolutionResult.Unconfigured);
        }

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId,
            Guid bindingId,
            CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
