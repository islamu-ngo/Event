using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using ISLAMU.Wire.Contracts.Admissions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionCheckInCredentialDigestService(
    ISecretResolver secretResolver,
    IOptions<AdmissionCredentialOptions> options) : IAdmissionCheckInCredentialDigestService
{
    private const int MinimumKeyByteLength = 32;
    private const string CredentialPurpose = "AdmissionTicket";

    public async Task<AdmissionCheckInCredentialDigest> DigestAsync(
        AdmissionCheckInCredentialDigestRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.TenantId == Guid.Empty ||
            !AdmissionCredentialBearer.TryCreate(request.Credential, out _))
        {
            return new AdmissionCheckInCredentialDigest([]);
        }

        int[] versions = options.Value.GetDigestKeyVersions();
        var candidates = new List<AdmissionCheckInCredentialDigestCandidate>(versions.Length);
        foreach (int version in versions)
        {
            byte[] key = await ResolveKeyAsync(version, cancellationToken);
            try
            {
                string signed = string.Create(
                    CultureInfo.InvariantCulture,
                    $"admission:v1:{request.TenantId:N}:{CredentialPurpose}:{request.Credential}");
                candidates.Add(new AdmissionCheckInCredentialDigestCandidate(
                    Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed))),
                    version));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        return new AdmissionCheckInCredentialDigest(candidates);
    }

    private async Task<byte[]> ResolveKeyAsync(int version, CancellationToken cancellationToken)
    {
        string settingKey = SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey;
        SecretResolutionResult resolved = await secretResolver.ResolveQualifiedAsync(
            settingKey,
            SecretScope.Instance,
            null,
            $"v{version}",
            cancellationToken);
        if (resolved.Status == SecretResolutionStatus.Unconfigured && version == options.Value.ActiveKeyVersion)
            resolved = await secretResolver.ResolveAsync(settingKey, null, cancellationToken);
        if (!resolved.IsResolved)
            throw new InvalidOperationException("Admission credential lookup key is unavailable.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(resolved.Value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Admission credential lookup key is unavailable.");
        }
        if (key.Length < MinimumKeyByteLength)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("Admission credential lookup key is unavailable.");
        }
        return key;
    }
}
