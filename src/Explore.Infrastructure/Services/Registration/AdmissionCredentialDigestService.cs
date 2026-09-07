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

public sealed class AdmissionCredentialDigestService(
    ISecretResolver secretResolver,
    IOptions<AdmissionCredentialOptions> options) : IAdmissionCredentialDigestService
{
    private const int MinimumKeyByteLength = 32;

    public async Task<AdmissionCredentialMaterial> CreateAsync(
        AdmissionCredentialCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.AdmissionTicketId == Guid.Empty ||
            request.AdmissionCredentialId == Guid.Empty || string.IsNullOrWhiteSpace(request.Purpose) ||
            request.CredentialVersion < 1)
        {
            throw new ArgumentException("Complete admission credential lineage is required.", nameof(request));
        }

        int keyVersion = options.Value.ActiveKeyVersion;
        byte[] hmacKey = await ResolveKeyAsync(keyVersion, cancellationToken);
        try
        {
            string bearer = CreateBearer();
            string digest = ComputeDigest(hmacKey, request.TenantId, request.Purpose, bearer);
            return new AdmissionCredentialMaterial(bearer, digest, keyVersion, request.CredentialVersion);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    public async Task<AdmissionCredentialVerificationOutcome> VerifyAsync(
        AdmissionCredentialVerificationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.PersistedKeyVersion < 1 ||
            string.IsNullOrWhiteSpace(request.Purpose) || string.IsNullOrWhiteSpace(request.PlaintextCredential) ||
            string.IsNullOrWhiteSpace(request.ExpectedDigest))
        {
            return AdmissionCredentialVerificationOutcome.InvalidRequest;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(request.ExpectedDigest);
        }
        catch (FormatException)
        {
            return AdmissionCredentialVerificationOutcome.MalformedDigest;
        }

        if (expected.Length != 32)
        {
            return AdmissionCredentialVerificationOutcome.MalformedDigest;
        }

        byte[] key;
        try
        {
            key = await ResolveKeyAsync(request.PersistedKeyVersion, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return AdmissionCredentialVerificationOutcome.KeyUnavailable;
        }

        byte[] computed;
        try
        {
            computed = Convert.FromBase64String(ComputeDigest(
                key, request.TenantId, request.Purpose, request.PlaintextCredential));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, computed)
                ? AdmissionCredentialVerificationOutcome.Match
                : AdmissionCredentialVerificationOutcome.Mismatch;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computed);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    public static bool Matches(string candidateDigest, string expectedDigest)
    {
        try
        {
            byte[] candidate = Convert.FromBase64String(candidateDigest);
            byte[] expected = Convert.FromBase64String(expectedDigest);
            return candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<byte[]> ResolveKeyAsync(int keyVersion, CancellationToken cancellationToken)
    {
        string settingKey = SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey;
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
            throw new InvalidOperationException($"Admission credential HMAC key version {keyVersion} is unavailable.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(resolved.Value);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Admission credential HMAC key must be Base64-encoded.", exception);
        }

        if (key.Length < MinimumKeyByteLength)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("Admission credential HMAC key must contain at least 32 bytes.");
        }

        return key;
    }

    private static string CreateBearer()
    {
        Span<byte> bytes = stackalloc byte[AdmissionCredentialBearer.ByteLength];
        RandomNumberGenerator.Fill(bytes);
        return AdmissionCredentialBearer.FromBytes(bytes).Value;
    }

    private static string ComputeDigest(byte[] key, Guid tenantId, string purpose, string bearer)
    {
        string signed = string.Create(CultureInfo.InvariantCulture,
            $"admission:v1:{tenantId:N}:{purpose.Trim()}:{bearer}");
        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));
    }
}
