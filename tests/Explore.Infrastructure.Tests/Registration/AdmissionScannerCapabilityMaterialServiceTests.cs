using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionScannerCapabilityMaterialServiceTests
{
    [Test]
    public async Task IssueUsesDedicatedDomainAndCandidatesAreCurrentFirst()
    {
        byte[] retainedKey = RandomNumberGenerator.GetBytes(32);
        byte[] activeKey = RandomNumberGenerator.GetBytes(32);
        var resolver = new ScannerSecretResolver(new Dictionary<int, string>
        {
            [4] = Convert.ToBase64String(retainedKey),
            [7] = Convert.ToBase64String(activeKey)
        });
        AdmissionScannerCapabilityMaterialService service = Service(resolver, 7, [4]);

        AdmissionScannerCapabilityMaterial material = await service.IssueAsync(
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()),
            CancellationToken.None);
        AdmissionScannerCapabilityDigestCandidates candidates = await service.DigestCandidatesAsync(
            new(material.PlaintextCapability), CancellationToken.None);
        string expected = Convert.ToBase64String(HMACSHA256.HashData(
            activeKey,
            Encoding.UTF8.GetBytes($"{AdmissionScannerCapabilityDigestDomain.Purpose}:{material.PlaintextCapability}")));

        await Assert.That(material.PlaintextCapability.Length).IsEqualTo(43);
        await Assert.That(Convert.FromBase64String(material.LookupDigest).Length).IsEqualTo(32);
        await Assert.That(material.LookupDigest).IsEqualTo(expected);
        await Assert.That(candidates.Candidates.Select(candidate => candidate.KeyVersion).ToArray())
            .IsEquivalentTo([7, 4]);
        await Assert.That(candidates.Candidates[0].LookupDigest).IsEqualTo(material.LookupDigest);
        await Assert.That(material.ToString()).DoesNotContain(material.PlaintextCapability);
        await Assert.That(material.ToString()).DoesNotContain(material.LookupDigest);
        await Assert.That(candidates.ToString()).DoesNotContain(material.PlaintextCapability);
        await Assert.That(candidates.ToString()).DoesNotContain(material.LookupDigest);
        await Assert.That(resolver.RequestedSettingKeys.Distinct().Single())
            .IsEqualTo(AdmissionScannerCapabilityMaterialService.SecretSettingKey);
        await Assert.That(resolver.RequestedSettingKeys).DoesNotContain("admissions.credential_lookup_hmac_key");
    }

    [Test]
    public async Task DigestIsGloballyComputableAndRotationIsBoundedAndDeduplicated()
    {
        byte[] activeKey = RandomNumberGenerator.GetBytes(32);
        byte[] retainedKey = RandomNumberGenerator.GetBytes(32);
        var resolver = new ScannerSecretResolver(new Dictionary<int, string>
        {
            [9] = Convert.ToBase64String(activeKey),
            [8] = Convert.ToBase64String(retainedKey)
        });
        AdmissionScannerCapabilityMaterialService service = Service(resolver, 9, [8, 9, 8]);
        AdmissionScannerCapabilityMaterial issued = await service.IssueAsync(
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()), CancellationToken.None);

        AdmissionScannerCapabilityDigestCandidates first = await service.DigestCandidatesAsync(
            new(issued.PlaintextCapability), CancellationToken.None);
        AdmissionScannerCapabilityDigestCandidates second = await service.DigestCandidatesAsync(
            new(issued.PlaintextCapability), CancellationToken.None);

        await Assert.That(first.Candidates.Select(candidate => candidate.KeyVersion).ToArray())
            .IsEquivalentTo([9, 8]);
        await Assert.That(first.Candidates.Select(candidate => candidate.LookupDigest).ToArray())
            .IsEquivalentTo(second.Candidates.Select(candidate => candidate.LookupDigest).ToArray());
        await Assert.That(first.Candidates.All(candidate => Convert.FromBase64String(candidate.LookupDigest).Length == 32))
            .IsTrue();
    }

    [Test]
    public async Task MissingMalformedAndShortKeysFailGenericallyWithoutLeakingValues()
    {
        string malformed = $"not-base64-{Guid.CreateVersion7():N}";
        string shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        AdmissionScannerCapabilityMaterialRequest request = new(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        Exception missing = await Assert.That(async () => await Service(
            new ScannerSecretResolver(new Dictionary<int, string>()), 3).IssueAsync(request, CancellationToken.None))
            .Throws<InvalidOperationException>();
        Exception invalidEncoding = await Assert.That(async () => await Service(
            new ScannerSecretResolver(new Dictionary<int, string> { [3] = malformed }), 3)
            .IssueAsync(request, CancellationToken.None)).Throws<InvalidOperationException>();
        Exception tooShort = await Assert.That(async () => await Service(
            new ScannerSecretResolver(new Dictionary<int, string> { [3] = shortKey }), 3)
            .IssueAsync(request, CancellationToken.None)).Throws<InvalidOperationException>();

        foreach (Exception exception in new[] { missing, invalidEncoding, tooShort })
        {
            await Assert.That(exception.Message).IsEqualTo("Admission scanner capability key is unavailable.");
            await Assert.That(exception.ToString()).DoesNotContain(malformed);
            await Assert.That(exception.ToString()).DoesNotContain(shortKey);
        }
    }

    [Test]
    public async Task CancellationPropagatesBeforeAnyDigestIsReturned()
    {
        var resolver = new ScannerSecretResolver(new Dictionary<int, string>(), cancelResolution: true);
        AdmissionScannerCapabilityMaterialService service = Service(resolver, 1);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.That(async () => await service.DigestCandidatesAsync(
            new("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), source.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task MoreThanEightUniqueVersionsFailsClosed()
    {
        int[] retained = Enumerable.Range(2, 8).ToArray();
        AdmissionScannerCapabilityMaterialService service = Service(
            new ScannerSecretResolver(new Dictionary<int, string>()), 1, retained);

        await Assert.That(async () => await service.DigestCandidatesAsync(
            new("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"), CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    private static AdmissionScannerCapabilityMaterialService Service(
        ISecretResolver resolver,
        int activeVersion,
        int[]? retainedVersions = null) =>
        new(resolver, Options.Create(new AdmissionScannerCapabilityDigestOptions
        {
            ActiveKeyVersion = activeVersion,
            RetainedKeyVersions = retainedVersions ?? []
        }));

    private sealed class ScannerSecretResolver(
        IReadOnlyDictionary<int, string> keys,
        bool cancelResolution = false) : ISecretResolver
    {
        public List<string> RequestedSettingKeys { get; } = [];

        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default)
        {
            RequestedSettingKeys.Add(settingKey);
            cancellationToken.ThrowIfCancellationRequested();
            if (cancelResolution)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(keys.TryGetValue(1, out string? value)
                ? Secret(settingKey, value)
                : SecretResolutionResult.Unconfigured);
        }

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default)
        {
            RequestedSettingKeys.Add(settingKey);
            cancellationToken.ThrowIfCancellationRequested();
            if (cancelResolution)
                throw new OperationCanceledException(cancellationToken);
            return Task.FromResult(scope == SecretScope.Instance && scopeId is null &&
                qualifier.Length > 1 && int.TryParse(qualifier[1..], out int version) &&
                keys.TryGetValue(version, out string? value)
                    ? Secret(settingKey, value)
                    : SecretResolutionResult.Unconfigured);
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

        private static SecretResolutionResult Secret(string key, string value) =>
            SecretResolutionResult.Resolved(new ResolvedSecret(
                key, value, SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTime.UtcNow));
    }
}
