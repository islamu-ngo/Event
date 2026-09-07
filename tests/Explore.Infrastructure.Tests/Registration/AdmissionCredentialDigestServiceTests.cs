using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionCredentialDigestServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000301");
    private static readonly Guid TicketId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000302");
    private static readonly Guid CredentialId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000303");

    [Test]
    public async Task CreateAsyncIssuesThirtyTwoByteBase64UrlBearerAndCanonicalKeyedDigest()
    {
        byte[] key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        AdmissionCredentialDigestService service = Service(7, key);

        AdmissionCredentialMaterial material = await service.CreateAsync(Request("AdmissionTicket"), CancellationToken.None);
        byte[] bearerBytes = Convert.FromBase64String(Pad(material.PlaintextCredential.Replace('-', '+').Replace('_', '/')));
        string signed = string.Create(CultureInfo.InvariantCulture,
            $"admission:v1:{TenantId:N}:AdmissionTicket:{material.PlaintextCredential}");
        string expected = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));

        await Assert.That(bearerBytes.Length).IsEqualTo(32);
        await Assert.That(material.PlaintextCredential.Length).IsEqualTo(43);
        await Assert.That(material.PlaintextCredential.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_')).IsTrue();
        await Assert.That(material.KeyVersion).IsEqualTo(7);
        await Assert.That(material.LookupDigest).IsEqualTo(expected);
        await Assert.That(Convert.FromBase64String(material.LookupDigest).Length).IsEqualTo(32);
        await Assert.That(AdmissionCredentialDigestService.Matches(material.LookupDigest, expected)).IsTrue();
    }

    [Test]
    public async Task PersistedKeyVersionRestoresAgainstSameVersionAndRotationUsesNewVersion()
    {
        byte[] oldKey = Enumerable.Repeat((byte)11, 32).ToArray();
        byte[] newKey = Enumerable.Repeat((byte)12, 32).ToArray();
        AdmissionCredentialMaterial oldMaterial = await Service(2, oldKey).CreateAsync(Request("AdmissionTicket"), CancellationToken.None);
        AdmissionCredentialMaterial newMaterial = await Service(3, newKey).CreateAsync(Request("AdmissionTicket"), CancellationToken.None);

        AdmissionCredentialVerificationOutcome restored = await Service(3, (2, oldKey), (3, newKey)).VerifyAsync(
            new AdmissionCredentialVerificationRequest(
                TenantId, 2, "AdmissionTicket", oldMaterial.PlaintextCredential, oldMaterial.LookupDigest),
            CancellationToken.None);
        AdmissionCredentialVerificationOutcome retired = await Service(3, (3, newKey)).VerifyAsync(
            new AdmissionCredentialVerificationRequest(
                TenantId, 2, "AdmissionTicket", oldMaterial.PlaintextCredential, oldMaterial.LookupDigest),
            CancellationToken.None);
        AdmissionCredentialVerificationOutcome malformed = await Service(3, (2, oldKey), (3, newKey)).VerifyAsync(
            new AdmissionCredentialVerificationRequest(
                TenantId, 2, "AdmissionTicket", oldMaterial.PlaintextCredential, "not-base64"),
            CancellationToken.None);

        await Assert.That(oldMaterial.KeyVersion).IsEqualTo(2);
        await Assert.That(newMaterial.KeyVersion).IsEqualTo(3);
        await Assert.That(oldMaterial.LookupDigest).IsNotEqualTo(newMaterial.LookupDigest);
        await Assert.That(restored).IsEqualTo(AdmissionCredentialVerificationOutcome.Match);
        await Assert.That(retired).IsEqualTo(AdmissionCredentialVerificationOutcome.KeyUnavailable);
        await Assert.That(malformed).IsEqualTo(AdmissionCredentialVerificationOutcome.MalformedDigest);
    }

    [Test]
    public async Task ActiveVersionCanResolveDedicatedAdmissionSecretDirectlyFromEnvironmentSource()
    {
        byte[] key = Enumerable.Repeat((byte)31, 32).ToArray();
        var service = new AdmissionCredentialDigestService(
            new DirectSecretResolver(Convert.ToBase64String(key)),
            Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = 5 }));

        AdmissionCredentialMaterial material = await service.CreateAsync(Request("AdmissionTicket"), CancellationToken.None);

        await Assert.That(material.KeyVersion).IsEqualTo(5);
        await Assert.That(Convert.FromBase64String(material.LookupDigest).Length).IsEqualTo(32);
    }

    [Test]
    public async Task TenantAndPurposeAreSignedAndPublicRepresentationsRedactBearer()
    {
        byte[] key = Enumerable.Repeat((byte)21, 32).ToArray();
        AdmissionCredentialMaterial material = await Service(4, key).CreateAsync(Request("AdmissionTicket"), CancellationToken.None);
        string wrongPurpose = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(
            $"admission:v1:{TenantId:N}:Recovery:{material.PlaintextCredential}")));
        string wrongTenant = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(
            $"admission:v1:{Guid.CreateVersion7():N}:AdmissionTicket:{material.PlaintextCredential}")));
        var handoff = new AdmissionOneTimeCredential(TicketId, material.PlaintextCredential);

        await Assert.That(material.LookupDigest).IsNotEqualTo(wrongPurpose);
        await Assert.That(material.LookupDigest).IsNotEqualTo(wrongTenant);
        await Assert.That(material.ToString()).DoesNotContain(material.PlaintextCredential);
        await Assert.That(handoff.ToString()).DoesNotContain(material.PlaintextCredential);
    }

    private static AdmissionCredentialCreateRequest Request(string purpose) =>
        new(TenantId, TicketId, CredentialId, purpose, 1);

    private static AdmissionCredentialDigestService Service(int version, byte[] key) =>
        Service(version, (version, key));

    private static AdmissionCredentialDigestService Service(int activeVersion, params (int Version, byte[] Key)[] keys) =>
        new(new SecretResolver(keys.ToDictionary(
                key => key.Version,
                key => Convert.ToBase64String(key.Key))),
            Options.Create(new AdmissionCredentialOptions { ActiveKeyVersion = activeVersion }));

    private static string Pad(string value) => value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');

    private sealed class DirectSecretResolver(string value) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(settingKey == SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, value, SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTime.UtcNow)) : SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId,
            string qualifier, CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SecretResolver(IReadOnlyDictionary<int, string> keys) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId,
            string qualifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(settingKey == SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey &&
                scope == SecretScope.Instance && scopeId is null &&
                qualifier.Length > 1 && int.TryParse(qualifier[1..], out int version) && keys.TryGetValue(version, out string? value) ? SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, value, SecretSourceType.EnvironmentVariable, scope, scopeId, DateTime.UtcNow)) : SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
