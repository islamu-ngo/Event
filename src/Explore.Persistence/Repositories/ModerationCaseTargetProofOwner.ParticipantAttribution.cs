using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private async Task FenceInvestigationAdministrativeRowsAsync(Guid[] humans, CancellationToken token)
    {
        foreach (var grant in await context.PlatformUserRoles.AsNoTracking()
            .Where(row => humans.Contains(row.UserId)).OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token))
        {
            await FenceAsync<PlatformUserRole>(grant.Id, true, token);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [grant.RoleId], token);
        }
        var members = await context.TenantUsers.AsNoTracking()
            .Where(row => humans.Contains(row.UserId) && row.TenantId == context.TenantFilterTenantId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var member in members) await FenceAsync<TenantUser>(member, true, token);
        foreach (var grant in await context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => members.Contains(row.TenantUserId)).OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token))
        {
            await FenceAsync<TenantUserRoleGrant>(grant.Id, true, token);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [grant.RoleId], token);
        }
    }

    private async Task<ModerationParticipantIncidentResponsibilitySource?> ParticipantResponsibilityAsync(
        ModerationCaseAttributionRequest request, ModerationCaseTargetSource material, NativeTarget native,
        NativeEnvironment environment, ModerationCase occurrence, ModerationCaseTargetPolicy policy,
        bool acquire, CancellationToken token)
    {
        if (investigationProtection is null || request.InvestigationSourceId is not { } id
            || request.InvestigationSourceRevision != 1 || request.SubjectKind != ModerationSubjectKind.Human
            || material.Kind != ModerationCaseTargetKind.ParticipantIncident
            || material.NativeKind != ModerationCaseTargetKind.ParticipantIncident
            || !await FenceAsync<ModerationParticipantIncidentResponsibilitySource>(id, acquire, token))
            return null;
        var row = await context.ModerationParticipantIncidentResponsibilitySources.AsNoTracking()
            .SingleOrDefaultAsync(source => source.Id == id, token);
        if (row is null) return null;
        row.ValidatePersistedState();
        var now = clock.GetUtcNow();
        if (!row.IsCurrentAt(now) || row.Revision != request.InvestigationSourceRevision
            || row.HostBindingId != environment.HostId || row.TrustDomainId != environment.TrustId
            || row.TenantId != environment.TenantId || row.SubjectId != request.SubjectId
            || row.OccurrenceCaseId != occurrence.Id || row.OccurrenceId != occurrence.OccurrenceId
            || row.OccurrenceCaseRevision != occurrence.Revision || row.EvidenceCaseRevision != request.EvidenceCaseRevision
            || row.EvidenceSourceId != material.Id || row.EvidenceSourceVersion != material.Version
            || row.MaterialFactId != material.MaterialFactId || row.EventId != material.EventId
            || row.EventVersion != native.EventVersion || row.ParticipantId != material.NativeTargetId
            || row.ParticipantGeneration != native.TargetVersion || row.OrderId != native.ParentId
            || row.OrderGeneration != native.ParentVersion || request.ValidUntil > row.ValidUntil
            || row.QualificationPolicyId != policy.Id || row.QualificationPolicyRevision != policy.Revision
            || row.MaximumQualificationLifetimeTicks != policy.ReadLifetimeTicks
            || !await AccountAsync(row.SubjectId, false, token)
            || !await AccountAsync(row.AssessorHumanId, false, token)
            || !await AccountAsync(row.ReadIssuerHumanId, false, token)
            || !await FenceAsync<ModerationCaseReadGrant>(row.ReadGrantId, acquire, token))
            return null;
        var assessor = await AdministratorAsync(environment, row.AssessorHumanId,
            row.AssessorRoleGrantId, null, false, token);
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(source => source.Id == row.ReadGrantId, token);
        if (assessor is null || grant is null || grant.CallerUserId != row.AssessorHumanId
            || grant.IssuerUserId != row.ReadIssuerHumanId || grant.Revision != row.ReadGrantRevision
            || grant.CaseRevision != row.OccurrenceCaseRevision || grant.ExpiresAt != row.ReadGrantExpiresAt
            || grant.TrustDomainId != environment.TrustId || grant.TenantId != environment.TenantId
            || !grant.IsCurrentAt(now)) return null;
        var relationship = await RelationshipAsync(ModerationCaseRelationshipKind.CaseReadGrant, grant.Id,
            material, native, environment with { CallerId = grant.CallerUserId }, occurrence.Id, acquire, now, token);
        if (relationship is null || !await FenceAsync<EventSession>(row.SessionId, acquire, token)
            || !await FenceAsync<ModerationCaseNarrative>(row.OriginalNarrativeId, acquire, token))
            return null;
        var session = await context.EventSessions.AsNoTracking()
            .SingleOrDefaultAsync(source => source.Id == row.SessionId && source.TenantId == environment.TenantId
                && source.EventId == row.EventId && !source.IsDeleted, token);
        var original = await context.ModerationCaseNarratives.AsNoTracking()
            .SingleOrDefaultAsync(source => source.Id == row.OriginalNarrativeId, token);
        if (session is null || session.ConcurrencyStamp != row.SessionGeneration
            || session.StartTime is not { } start || session.EndTime is not { } end
            || start > row.IncidentWindowFrom || end < row.IncidentWindowUntil
            || original is null || original.CaseId != occurrence.Id || original.PurgeAt != row.OriginalNarrativePurgeAt
            || original.ReporterUserId == row.AssessorHumanId
            || !await AccountAsync(original.ReporterUserId, false, token))
            return null;
        // Actual native facts construct this internal custody observation. There is no
        // impersonated caller, public grant, claimed fence or payload disclosure.
        var humans = _fences.Where(fence => fence.Type == typeof(User)).Select(fence => fence.Id).ToArray();
        var administrators = new List<Guid>();
        foreach (var human in humans)
            if (await AdministratorAsync(environment, human, null, null, false, token) is not null)
                administrators.Add(human);
        var host = await context.ModerationHostBindings.AsNoTracking()
            .SingleAsync(source => source.Id == environment.HostId, token);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleAsync(source => source.Id == environment.EpochId, token);
        var currentCaller = await AdministratorAsync(environment, environment.CallerId, null, null, false, token);
        var authority = new ModerationSourceAuthority(environment.CallerId, host, epoch,
            environment.Scope.Parent!, environment.Scope, currentCaller?.Platform == true, humans, administrators);
        var custody = new ModerationCaseNarrativePolicyReader(context, clock);
        var allegation = await custody.ReadAsync(authority, material.Kind, token,
            ModerationCaseNarrativePurpose.ConfidentialAllegation);
        var investigation = await custody.ReadAsync(authority, material.Kind, token,
            ModerationCaseNarrativePurpose.ParticipantInvestigation);
        if (allegation is null || investigation is null
            || row.CustodySourceId != investigation.Value.Source.Id
            || row.CustodySourceRevision != investigation.Value.Source.Revision
            || row.CustodySourceWriterGeneration != investigation.Value.Source.WriterGeneration
            || row.CustodySourceValidUntil != investigation.Value.Source.ValidUntil
            || row.MaximumRetentionTicks != investigation.Value.Source.MaximumRetentionTicks
            || row.CustodyAdoptionId != investigation.Value.Adoption.Id
            || row.CustodyAdoptionRevision != investigation.Value.Adoption.Revision
            || row.CustodyAdoptionWriterGeneration != investigation.Value.Adoption.WriterGeneration
            || row.CustodyAdoptionValidUntil != investigation.Value.Adoption.ValidUntil)
            return null;
        byte[]? originalPlaintext = null;
        byte[]? originalCiphertext = null;
        byte[]? ciphertext = null;
        byte[]? plaintext = null;
        byte[]? capturedRequestBytes = null;
        try
        {
            originalPlaintext = ModerationOriginalNarrativeAuthentication.Authenticate(investigationProtection,
                original, material, allegation.Value.Source, allegation.Value.Adoption, environment.Scope, now);
            if (originalPlaintext is null) return null;
            originalCiphertext = original.ProtectedMaterial.ToArray();
            var authorityDigest = ParticipantInvestigationAuthorityDigest(native, assessor, relationship,
                grant, session, original, Digest(originalCiphertext));
            ciphertext = row.ProtectedAssessment.ToArray();
            plaintext = ModerationParticipantInvestigationAuthentication.Protector(
                investigationProtection, row, authorityDigest).Unprotect(ciphertext);
            if (plaintext.Length == 0 || plaintext.Length > investigation.Value.Source.MaximumMaterialBytes
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(plaintext),
                    Convert.FromHexString(row.AssessmentDigest))) return null;
            var grounds = JsonSerializer.Deserialize<ModerationParticipantInvestigationGrounds>(plaintext);
            if (grounds is null || !grounds.IsValid()) return null;
            // A storage factory is not the native producer. Authenticate its actual
            // committed request/principal/epoch receipt, not merely plausible ciphertext.
            var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
                .SingleOrDefaultAsync(source => source.SourceId == row.Id
                    && source.SourceKind == ModerationQualificationSourceKind.ParticipantIncidentResponsibilitySource
                    && source.ResultRevision == 1, token);
            if (receipt is null || !await FenceAsync<ModerationQualificationMutationReceipt>(receipt.Id, acquire, token))
                return null;
            receipt.ValidatePersistedState();
            var captured = new RecordModerationParticipantIncidentResponsibilitySource
            {
                OperationId = receipt.Id, Target = environment.Scope,
                ExpectedQualificationRevision = receipt.QualificationRevision - 1,
                SourceId = row.Id, OccurrenceCaseId = row.OccurrenceCaseId,
                ExpectedCaseRevision = row.OccurrenceCaseRevision, EvidenceCaseRevision = row.EvidenceCaseRevision,
                ReadGrantId = row.ReadGrantId, EvidenceSourceId = row.EvidenceSourceId,
                EvidenceSourceVersion = row.EvidenceSourceVersion, SubjectHumanId = row.SubjectId,
                SessionId = row.SessionId, ExpectedSessionGeneration = row.SessionGeneration,
                IncidentWindowFrom = row.IncidentWindowFrom, IncidentWindowUntil = row.IncidentWindowUntil,
                ValidUntil = row.ValidUntil, PurgeAt = row.PurgeAt, Grounds = grounds
            };
            capturedRequestBytes = JsonSerializer.SerializeToUtf8Bytes(captured, captured.GetType());
            using var requestHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            requestHash.AppendData(Encoding.UTF8.GetBytes(captured.GetType().FullName + "\n"));
            requestHash.AppendData(capturedRequestBytes);
            var digest = Convert.ToHexString(requestHash.GetHashAndReset());
            return receipt.RequestDigest == digest && receipt.PrincipalId == row.AssessorHumanId
                && receipt.HostBindingId == environment.HostId && receipt.TrustDomainId == environment.TrustId
                && receipt.TenantId == environment.TenantId && receipt.QualificationRevision <= environment.EpochRevision
                && PersistedTime(receipt.CompletedAt) == row.QualifiedAt
                && ModerationParticipantInvestigationQualification.IsValid(captured)
                && clock.GetUtcNow() >= now && clock.GetUtcNow() < row.ValidUntil ? row : null;
        }
        catch (CryptographicException) { return null; }
        catch (ArgumentException) { return null; }
        catch (JsonException) { return null; }
        finally
        {
            if (originalPlaintext is not null) CryptographicOperations.ZeroMemory(originalPlaintext);
            if (originalCiphertext is not null) CryptographicOperations.ZeroMemory(originalCiphertext);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (capturedRequestBytes is not null) CryptographicOperations.ZeroMemory(capturedRequestBytes);
        }
    }
}
