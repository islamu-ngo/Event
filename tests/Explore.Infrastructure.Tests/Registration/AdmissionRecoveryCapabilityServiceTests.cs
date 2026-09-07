using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionRecoveryCapabilityServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000401");
    private static readonly Guid RequestId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000402");
    private static readonly Guid TicketId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000403");
    private static readonly DateTimeOffset UtcNow = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task IssueCreatesDistinctCanonicalMaterialWithFixedExpiry()
    {
        var service = Service(
            new RecoverySecretResolver(new Dictionary<int, string> { [7] = Key(7) }),
            activeVersion: 7);
        var request = new AdmissionRecoveryCapabilityIssueRequest(
            TenantId,
            RequestId,
            TicketId,
            AdmissionRecoveryPurpose.TicketRecovery);

        AdmissionRecoveryCapabilityMaterial first = await service.IssueAsync(request, CancellationToken.None);
        AdmissionRecoveryCapabilityMaterial second = await service.IssueAsync(request, CancellationToken.None);

        await Assert.That(first.Capability.Length).IsEqualTo(43);
        await Assert.That(first.Capability).DoesNotContain("=");
        await Assert.That(first.Capability).IsNotEqualTo(second.Capability);
        await Assert.That(first.LookupDigest).IsNotEqualTo(second.LookupDigest);
        await Assert.That(first.LocatorDigest).IsNotEqualTo(second.LocatorDigest);
        await Assert.That(Convert.FromBase64String(first.LookupDigest).Length).IsEqualTo(32);
        await Assert.That(Convert.FromBase64String(first.LocatorDigest).Length).IsEqualTo(32);
        await Assert.That(first.KeyVersion).IsEqualTo(7);
        await Assert.That(first.ExpiresAtUtc).IsEqualTo(UtcNow.AddMinutes(15));
        await Assert.That(first.ToString()).DoesNotContain(first.Capability);
        await Assert.That(first.ToString()).DoesNotContain(first.LookupDigest);
        await Assert.That(first.ToString()).DoesNotContain(first.LocatorDigest);
    }

    [Test]
    public async Task DigestSeparatesEveryRecoveryLineageDimension()
    {
        var service = Service(
            new RecoverySecretResolver(new Dictionary<int, string> { [3] = Key(3) }),
            activeVersion: 3);
        AdmissionRecoveryCapabilityMaterial material = await service.IssueAsync(
            new(TenantId, RequestId, TicketId, AdmissionRecoveryPurpose.TicketRecovery),
            CancellationToken.None);

        AdmissionRecoveryCapabilityDigest baseline = await service.DigestAsync(
            new(TenantId, RequestId, TicketId, AdmissionRecoveryPurpose.TicketRecovery, material.Capability, 3),
            CancellationToken.None);
        AdmissionRecoveryCapabilityDigest otherTenant = await service.DigestAsync(
            new(Guid.CreateVersion7(), RequestId, TicketId, AdmissionRecoveryPurpose.TicketRecovery, material.Capability, 3),
            CancellationToken.None);
        AdmissionRecoveryCapabilityDigest otherRequest = await service.DigestAsync(
            new(TenantId, Guid.CreateVersion7(), TicketId, AdmissionRecoveryPurpose.TicketRecovery, material.Capability, 3),
            CancellationToken.None);
        AdmissionRecoveryCapabilityDigest otherTicket = await service.DigestAsync(
            new(TenantId, RequestId, Guid.CreateVersion7(), AdmissionRecoveryPurpose.TicketRecovery, material.Capability, 3),
            CancellationToken.None);
        AdmissionRecoveryCapabilityDigest otherPurpose = await service.DigestAsync(
            new(TenantId, RequestId, TicketId, AdmissionRecoveryPurpose.TransferAcceptance, material.Capability, 3),
            CancellationToken.None);

        await Assert.That(baseline.LookupDigest).IsEqualTo(material.LookupDigest);
        await Assert.That(new[]
        {
            otherTenant.LookupDigest,
            otherRequest.LookupDigest,
            otherTicket.LookupDigest,
            otherPurpose.LookupDigest
        }).DoesNotContain(baseline.LookupDigest);
    }

    [Test]
    public async Task RetainedVersionRestoresDigestAndMissingVersionFailsClosed()
    {
        var resolver = new RecoverySecretResolver(new Dictionary<int, string>
        {
            [1] = Key(1),
            [2] = Key(2)
        });
        AdmissionRecoveryCapabilityService original = Service(resolver, activeVersion: 1);
        AdmissionRecoveryCapabilityMaterial material = await original.IssueAsync(
            new(TenantId, RequestId, TicketId, AdmissionRecoveryPurpose.TicketRecovery),
            CancellationToken.None);
        AdmissionRecoveryCapabilityService rotated = Service(
            resolver,
            activeVersion: 2,
            retainedVersions: [1]);

        AdmissionRecoveryCapabilityDigest restored = await rotated.DigestAsync(
            new(TenantId, RequestId, TicketId, AdmissionRecoveryPurpose.TicketRecovery, material.Capability, 1),
            CancellationToken.None);
        IReadOnlyList<AdmissionRecoveryLocatorDigest> locators =
            await rotated.DigestLocatorsAsync(material.Capability, CancellationToken.None);
        var missing = new AdmissionRecoveryCapabilityService(
            new RecoverySecretResolver(new Dictionary<int, string> { [2] = Key(2) }),
            Options.Create(new AdmissionRecoveryOptions { ActiveKeyVersion = 2, CapabilityLifetimeMinutes = 15 }),
            new FixedTimeProvider(UtcNow));

        await Assert.That(restored.LookupDigest).IsEqualTo(material.LookupDigest);
        await Assert.That(locators.Select(locator => locator.KeyVersion)).IsEquivalentTo([1, 2]);
        await Assert.That(locators.Single(locator => locator.KeyVersion == 1).LocatorDigest)
            .IsEqualTo(material.LocatorDigest);
        await Assert.That(async () => await missing.DigestAsync(
            new(TenantId, RequestId, TicketId, AdmissionRecoveryPurpose.TicketRecovery, material.Capability, 1),
            CancellationToken.None)).Throws<InvalidOperationException>();
    }

    private static AdmissionRecoveryCapabilityService Service(
        ISecretResolver resolver,
        int activeVersion,
        int[]? retainedVersions = null) =>
        new(
            resolver,
            Options.Create(new AdmissionRecoveryOptions
            {
                ActiveKeyVersion = activeVersion,
                RetainedKeyVersions = retainedVersions ?? [],
                CapabilityLifetimeMinutes = 15
            }),
            new FixedTimeProvider(UtcNow));

    private static string Key(int marker) =>
        Convert.ToBase64String(Enumerable.Repeat(checked((byte)marker), 32).ToArray());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecoverySecretResolver(IReadOnlyDictionary<int, string> keys) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(
            string settingKey,
            Guid? tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            string qualifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                settingKey == SecretDefinitionRegistry.Keys.Admissions.RecoveryCapabilityHmacKey &&
                scope == SecretScope.Instance &&
                scopeId is null &&
                qualifier.Length > 1 &&
                int.TryParse(qualifier[1..], out int version) &&
                keys.TryGetValue(version, out string? value) ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey,
                value,
                SecretSourceType.EnvironmentVariable,
                scope,
                scopeId,
                DateTime.UtcNow)) : SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(
            Guid tenantId,
            Guid bindingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(
            string settingKey,
            SecretScope scope,
            Guid? scopeId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
