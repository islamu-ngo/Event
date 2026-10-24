using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Current independent custody qualification on the native owner transaction, never receipt lifetime or recognition.</summary>
internal sealed class ModerationCaseNarrativePolicyReader(ExploreDbContext context, TimeProvider clock)
{
    internal async Task<ModerationCaseNarrativePolicySource?> ReadSourceAsync(
        ModerationSourceAuthority authority, ModerationCaseTargetKind kind, CancellationToken token,
        ModerationCaseNarrativePurpose purpose = ModerationCaseNarrativePurpose.ConfidentialAllegation)
    {
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context, Slot(authority, kind, purpose), token);
        var source = await context.Set<ModerationCaseNarrativePolicySource>().AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
                && row.TenantId == authority.Tenant.Id && row.TargetKind == kind
                && row.Purpose == purpose)
            .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
        if (source is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrativePolicySource>(context, source.Id, token);
        source.ValidatePersistedState();
        if (!await OwnersCurrentAsync(authority, source.Ownership, source.RecordedByHumanId, token)
            || !await ReceiptAsync(authority, RecordRequest(source, authority.Tenant),
                ModerationQualificationSourceKind.CaseNarrativePolicySource, source.Id, 1,
                source.RecordedByHumanId, source.WriterGeneration, source.RecordedAt, token)
            || clock.GetUtcNow() < source.RecordedAt || clock.GetUtcNow() >= source.ValidUntil)
            return null;
        return source;
    }

    internal async Task<(ModerationCaseNarrativePolicySource Source, ModerationCaseNarrativeAdoption Adoption)?> ReadAsync(
        ModerationSourceAuthority authority, ModerationCaseTargetKind kind, CancellationToken token,
        ModerationCaseNarrativePurpose purpose = ModerationCaseNarrativePurpose.ConfidentialAllegation)
    {
        var source = await ReadSourceAsync(authority, kind, token, purpose);
        if (source is null) return null;
        var adoption = await context.Set<ModerationCaseNarrativeAdoption>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.SourceId == source.Id && row.HostBindingId == authority.Binding.Id
            && row.TrustDomainId == authority.Binding.TrustDomainId && row.TenantId == authority.Tenant.Id, token);
        if (adoption is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrativeAdoption>(context, adoption.Id, token);
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(source, clock.GetUtcNow())
            || !await ReceiptAsync(authority, AdoptionRequest(adoption, authority.Tenant),
                ModerationQualificationSourceKind.CaseNarrativeAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token)
            || !await OwnersCurrentAsync(authority, source.Ownership, source.RecordedByHumanId, token)
            || !adoption.MatchesCurrent(source, clock.GetUtcNow()))
            return null;
        return (source, adoption);
    }

    internal async Task<bool> OwnersCurrentAsync(ModerationSourceAuthority authority,
        ModerationCaseNarrativeOwnership ownership, Guid recorder, CancellationToken token)
    {
        ownership.Validate(recorder);
        var humans = new[] { recorder, ownership.PrivacyOwnerHumanId,
            ownership.PrivacyControllerHumanId, ownership.GovernanceControllerHumanId }.Distinct().ToArray();
        if (humans.Any(id => !authority.FencedHumans.Contains(id))
            || await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Length
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token))
            return false;
        foreach (var owner in new[] { recorder, ownership.PrivacyOwnerHumanId })
        {
            if (!authority.Administrators.Contains(owner)) return false;
            if (!await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == owner && !row.User.IsDeleted
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token)
                && !await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
                    row.TenantUser.UserId == owner && row.TenantId == authority.Tenant.Id && row.RevokedAt == null
                    && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                    && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token))
                return false;
        }
        return true;
    }

    internal async Task<bool> ReceiptAsync(ModerationSourceAuthority authority, ModerationQualificationMutation template,
        ModerationQualificationSourceKind kind, Guid sourceId, long revision, Guid humanId,
        long generation, DateTimeOffset at, CancellationToken token)
    {
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.SourceId == sourceId && row.SourceKind == kind && row.ResultRevision == revision
            && row.HostBindingId == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
            && row.TenantId == authority.Tenant.Id, token);
        if (receipt is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receipt.Id, token);
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.RequestDigest == digest && receipt.PrincipalId == humanId
            && receipt.QualificationRevision == generation && receipt.CompletedAt == at
            && generation <= authority.Epoch.Revision;
    }

    internal static RecordModerationCaseNarrativePolicySource RecordRequest(
        ModerationCaseNarrativePolicySource source, AuthorityScope tenant) => new()
        {
            OperationId = Guid.Empty,
            Target = tenant,
            ExpectedQualificationRevision = source.WriterGeneration - 1,
            SourceId = source.Id,
            TargetKind = source.TargetKind,
            Terms = source.Terms,
            Ownership = source.Ownership,
            ValidUntil = source.ValidUntil
        };

    internal static AdoptModerationCaseNarrativePolicySource AdoptionRequest(
        ModerationCaseNarrativeAdoption adoption, AuthorityScope tenant) => new()
        {
            OperationId = Guid.Empty,
            Target = tenant,
            ExpectedQualificationRevision = adoption.WriterGeneration - 1,
            SourceId = adoption.SourceId,
            ExpectedSourceRevision = adoption.SourceRevision,
            ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration,
            AdoptionId = adoption.Id,
            ValidUntil = adoption.ValidUntil
        };

    internal static string Slot(ModerationSourceAuthority authority, ModerationCaseTargetKind kind,
        ModerationCaseNarrativePurpose purpose = ModerationCaseNarrativePurpose.ConfidentialAllegation) =>
        $"moderation.case-narrative-policy:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{(int)kind}:{(int)purpose}";
}
