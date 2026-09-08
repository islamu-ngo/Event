using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ISLAMU.Wire.Contracts.Admissions;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionRecoveryCapabilityService(
    ISecretResolver secretResolver,
    IOptions<AdmissionRecoveryOptions> options,
    TimeProvider timeProvider) : IAdmissionRecoveryCapabilityService
{
    private const int MinimumKeyByteLength = 32;

    public async Task<AdmissionRecoveryCapabilityMaterial> IssueAsync(
        AdmissionRecoveryCapabilityIssueRequest request,
        CancellationToken cancellationToken)
    {
        ValidateLineage(request.TenantId, request.RecoveryRequestId, request.AdmissionTicketId);
        if (request.Purpose != AdmissionRecoveryPurpose.TicketRecovery)
        {
            throw new ArgumentException("Recovery issuance requires the ticket recovery purpose.", nameof(request));
        }

        int keyVersion = request.KeyVersion > 0
            ? request.KeyVersion
            : options.Value.ActiveKeyVersion;
        byte[] key = await ResolveKeyAsync(keyVersion, cancellationToken);
        try
        {
            string capability = CreateCapability();
            string digest = ComputeDigest(
                key,
                request.TenantId,
                request.RecoveryRequestId,
                request.AdmissionTicketId,
                request.Purpose,
                capability);
            string locatorDigest = ComputeLocatorDigest(key, capability);
            return new AdmissionRecoveryCapabilityMaterial(
                capability,
                digest,
                keyVersion,
                request.Purpose,
                timeProvider.GetUtcNow().AddMinutes(options.Value.CapabilityLifetimeMinutes),
                locatorDigest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<IReadOnlyList<AdmissionRecoveryLocatorDigest>> DigestLocatorsAsync(
        string capability,
        CancellationToken cancellationToken)
    {
        if (!AdmissionCredentialBearer.TryCreate(capability, out _))
        {
            throw new ArgumentException("Recovery capability material is invalid.", nameof(capability));
        }

        int[] versions = options.Value.RetainedKeyVersions
            .Append(options.Value.ActiveKeyVersion)
            .Distinct()
            .OrderDescending()
            .ToArray();
        var result = new List<AdmissionRecoveryLocatorDigest>(versions.Length);
        foreach (int version in versions)
        {
            byte[] key = await ResolveKeyAsync(version, cancellationToken);
            try
            {
                result.Add(new AdmissionRecoveryLocatorDigest(
                    ComputeLocatorDigest(key, capability),
                    version));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        return result;
    }

    public async Task<AdmissionRecoveryCapabilityDigest> DigestAsync(
        AdmissionRecoveryCapabilityDigestRequest request,
        CancellationToken cancellationToken)
    {
        ValidateLineage(request.TenantId, request.RecoveryRequestId, request.AdmissionTicketId);
        if (string.IsNullOrWhiteSpace(request.Capability))
        {
            throw new ArgumentException("Recovery capability material is required.", nameof(request));
        }

        int keyVersion = request.KeyVersion > 0
            ? request.KeyVersion
            : options.Value.ActiveKeyVersion;
        byte[] key = await ResolveKeyAsync(keyVersion, cancellationToken);
        try
        {
            return new AdmissionRecoveryCapabilityDigest(
                ComputeDigest(
                    key,
                    request.TenantId,
                    request.RecoveryRequestId,
                    request.AdmissionTicketId,
                    request.Purpose,
                    request.Capability),
                keyVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static bool FixedTimeMatches(string candidateDigest, string expectedDigest)
    {
        try
        {
            byte[] candidate = Convert.FromBase64String(candidateDigest);
            byte[] expected = Convert.FromBase64String(expectedDigest);
            return candidate.Length == 32 &&
                expected.Length == 32 &&
                CryptographicOperations.FixedTimeEquals(candidate, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<byte[]> ResolveKeyAsync(int keyVersion, CancellationToken cancellationToken)
    {
        if (keyVersion <= 0)
        {
            throw new InvalidOperationException("Admission recovery capability key is unavailable.");
        }

        string settingKey = SecretDefinitionRegistry.Keys.Admissions.RecoveryCapabilityHmacKey;
        SecretResolutionResult resolved = await secretResolver.ResolveQualifiedAsync(
            settingKey,
            SecretScope.Instance,
            null,
            $"v{keyVersion}",
            cancellationToken);
        if (resolved.Status == SecretResolutionStatus.Unconfigured && keyVersion == options.Value.ActiveKeyVersion)
        {
            resolved = await secretResolver.ResolveAsync(settingKey, null, cancellationToken);
        }

        if (!resolved.IsResolved)
        {
            throw new InvalidOperationException("Admission recovery capability key is unavailable.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(resolved.Value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Admission recovery capability key has invalid encoding.",
                exception);
        }

        if (key.Length < MinimumKeyByteLength)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("Admission recovery capability key is invalid.");
        }

        return key;
    }

    private static string CreateCapability()
    {
        Span<byte> bytes = stackalloc byte[AdmissionCredentialBearer.ByteLength];
        RandomNumberGenerator.Fill(bytes);
        return AdmissionCredentialBearer.FromBytes(bytes).Value;
    }

    private static string ComputeDigest(
        byte[] key,
        Guid tenantId,
        Guid recoveryRequestId,
        Guid admissionTicketId,
        AdmissionRecoveryPurpose purpose,
        string capability)
    {
        string signed = string.Create(
            CultureInfo.InvariantCulture,
            $"admission-recovery:v1:{tenantId:N}:{recoveryRequestId:N}:{admissionTicketId:N}:{purpose}:{capability}");
        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));
    }

    private static string ComputeLocatorDigest(byte[] key, string capability)
    {
        string signed = $"admission-recovery-locator:v1:{capability}";
        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));
    }

    private static void ValidateLineage(Guid tenantId, Guid recoveryRequestId, Guid admissionTicketId)
    {
        if (tenantId == Guid.Empty || recoveryRequestId == Guid.Empty || admissionTicketId == Guid.Empty)
        {
            throw new ArgumentException("Complete admission recovery lineage is required.");
        }
    }
}
