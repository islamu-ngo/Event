using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using ISLAMU.Wire.Contracts.Admissions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionScannerCapabilityMaterialService(
    ISecretResolver secretResolver,
    IOptions<AdmissionScannerCapabilityDigestOptions> options)
    : IAdmissionScannerCapabilityMaterialService
{
    public const string SecretSettingKey = "admissions.scanner_capability_hmac_key";
    private const int MinimumKeyByteLength = 32;
    private const string KeyUnavailableMessage = "Admission scanner capability key is unavailable.";

    public async Task<AdmissionScannerCapabilityMaterial> IssueAsync(
        AdmissionScannerCapabilityMaterialRequest request,
        CancellationToken cancellationToken)
    {
        if (request.IssueRequestId == Guid.Empty || request.TenantId == Guid.Empty ||
            request.ScannerCapabilityId == Guid.Empty)
        {
            throw new ArgumentException("Complete scanner capability lineage is required.", nameof(request));
        }

        int version = GetKeyVersions()[0];
        byte[] key = await ResolveKeyAsync(version, cancellationToken);
        try
        {
            string capability = CreateCapability();
            return new AdmissionScannerCapabilityMaterial(
                capability,
                Digest(key, capability),
                version);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<AdmissionScannerCapabilityDigestCandidates> DigestCandidatesAsync(
        AdmissionScannerCapabilityDigestCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || !AdmissionCredentialBearer.TryCreate(request.Capability, out _))
            return new AdmissionScannerCapabilityDigestCandidates([]);

        int[] versions = GetKeyVersions();
        var candidates = new List<AdmissionScannerCapabilityDigestCandidate>(versions.Length);
        foreach (int version in versions)
        {
            byte[] key = await ResolveKeyAsync(version, cancellationToken);
            try
            {
                candidates.Add(new AdmissionScannerCapabilityDigestCandidate(
                    version,
                    Digest(key, request.Capability)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        return new AdmissionScannerCapabilityDigestCandidates(candidates);
    }

    private async Task<byte[]> ResolveKeyAsync(int version, CancellationToken cancellationToken)
    {
        const string settingKey = SecretSettingKey;
        SecretResolutionResult resolved = await secretResolver.ResolveQualifiedAsync(
            settingKey,
            SecretScope.Instance,
            null,
            $"v{version}",
            cancellationToken);
        if (resolved.Status == SecretResolutionStatus.Unconfigured && version == options.Value.ActiveKeyVersion)
            resolved = await secretResolver.ResolveAsync(settingKey, null, cancellationToken);
        if (!resolved.IsResolved)
            throw new InvalidOperationException(KeyUnavailableMessage);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(resolved.Value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(KeyUnavailableMessage);
        }
        if (key.Length < MinimumKeyByteLength)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(KeyUnavailableMessage);
        }
        return key;
    }

    private int[] GetKeyVersions()
    {
        int[] versions = options.Value.RetainedKeyVersions
            .Prepend(options.Value.ActiveKeyVersion)
            .Distinct()
            .ToArray();
        if (versions.Any(version => version < 1) ||
            versions.Length > AdmissionScannerCapabilityDigestOptions.MaximumKeyVersions)
        {
            throw new InvalidOperationException("Admission scanner capability key configuration is invalid.");
        }

        return versions;
    }

    private static string CreateCapability()
    {
        Span<byte> bytes = stackalloc byte[AdmissionCredentialBearer.ByteLength];
        RandomNumberGenerator.Fill(bytes);
        return AdmissionCredentialBearer.FromBytes(bytes).Value;
    }

    private static string Digest(byte[] key, string capability) => Convert.ToBase64String(
        HMACSHA256.HashData(
            key,
            Encoding.UTF8.GetBytes($"{AdmissionScannerCapabilityDigestDomain.Purpose}:{capability}")));
}
