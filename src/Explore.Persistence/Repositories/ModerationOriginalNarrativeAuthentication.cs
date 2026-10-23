using System.Security.Cryptography;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Authenticates original allegation material under its exact immutable custody binding.
/// The calling native owner must already hold current access, Human, material and policy
/// fences on its primary transaction. This primitive supplies neither access authority
/// nor incident responsibility. Returned plaintext is owned and cleared by that caller.
/// </summary>
internal static class ModerationOriginalNarrativeAuthentication
{
    internal static byte[]? Authenticate(IDataProtectionProvider protection,
        ModerationCaseNarrative row, ModerationCaseTargetSource material,
        ModerationCaseNarrativePolicySource policy, ModerationCaseNarrativeAdoption adoption,
        AuthorityScope scope, DateTimeOffset now)
    {
        row.ValidatePersistedState();
        policy.ValidatePersistedState();
        adoption.ValidatePersistedState();
        if (scope.Kind != ModerationScopeKind.Tenant
            || policy.Purpose != ModerationCaseNarrativePurpose.ConfidentialAllegation
            || policy.TargetKind != material.Kind || !adoption.MatchesCurrent(policy, now)
            || now < policy.RecordedAt || now >= policy.ValidUntil
            || now < row.CapturedAt || now >= row.PurgeAt
            || row.HostBindingId != policy.HostBindingId || row.TrustDomainId != policy.TrustDomainId
            || row.TenantId != policy.TenantId || row.TenantId != scope.Id
            || row.TrustDomainId != scope.TrustDomainId
            || row.SourceId != material.Id || row.SourceVersion != material.Version
            || row.MaterialFactId != material.MaterialFactId || material.TenantId != row.TenantId
            || material.TrustDomainId != row.TrustDomainId
            || row.PolicySourceId != policy.Id || row.AdoptionId != adoption.Id
            || row.PolicyWriterGeneration != policy.WriterGeneration
            || row.AdoptionWriterGeneration != adoption.WriterGeneration
            || row.PurgeAt > adoption.ValidUntil
            || row.PurgeAt.UtcTicks - row.CapturedAt.UtcTicks > policy.MaximumRetentionTicks)
            return null;

        var expectedDigest = Convert.FromHexString(material.MaterialDigest);
        var ciphertext = row.ProtectedMaterial.ToArray();
        try
        {
            var plaintext = Protector(protection, row.Id, row.CaseId, row.TenantId, row.TrustDomainId,
                row.SourceId, row.SourceVersion, row.MaterialFactId, row.PolicySourceId, row.AdoptionId,
                row.CapturedAt, row.PurgeAt, row.HostBindingId, row.ReporterUserId, row.ReceiptId,
                row.QualificationEpochId, row.QualificationRevision, row.PolicyWriterGeneration,
                row.AdoptionWriterGeneration).Unprotect(ciphertext);
            if (plaintext.Length is > 0 && plaintext.Length <= policy.MaximumMaterialBytes
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(plaintext), expectedDigest))
                return plaintext;
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        catch (CryptographicException) { return null; }
        finally { CryptographicOperations.ZeroMemory(ciphertext); }
    }

    internal static IDataProtector Protector(IDataProtectionProvider protection,
        Guid id, Guid caseId, Guid tenantId, Guid trustId,
        Guid sourceId, Guid sourceVersion, Guid materialId, Guid policyId, Guid adoptionId,
        DateTimeOffset capturedAt, DateTimeOffset purgeAt, Guid hostId, Guid reporterId, Guid receiptId,
        Guid epochId, long epochRevision, long policyGeneration, long adoptionGeneration) =>
        protection.CreateProtector("ISLAMU.Moderation.CaseNarrative.v1").CreateProtector(FormattableString.Invariant(
            $"{id:N}|{caseId:N}|{tenantId:N}|{trustId:N}|{sourceId:N}|{sourceVersion:N}|{materialId:N}|{policyId:N}|{adoptionId:N}|{capturedAt.UtcTicks}|{purgeAt.UtcTicks}|{hostId:N}|{reporterId:N}|{receiptId:N}|{epochId:N}|{epochRevision}|{policyGeneration}|{adoptionGeneration}"));
}
