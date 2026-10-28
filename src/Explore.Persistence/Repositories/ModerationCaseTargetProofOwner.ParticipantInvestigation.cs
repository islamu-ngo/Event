using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    /// <summary>
    /// Borrows the qualification writer's already fenced Humans and administrators on this
    /// exact primary context. Only subordinate native rows are acquired here. Original
    /// allegation authentication and separate investigator custody remain on that transaction.
    /// </summary>
    internal async Task<ParticipantInvestigationFacts?> AcquireParticipantInvestigationAsync(
        ExploreDbContext primary, RecordModerationParticipantIncidentResponsibilitySource request,
        ModerationSourceAuthority authority, IDataProtectionProvider protection,
        DateTimeOffset qualifiedAt, CancellationToken token)
    {
        ResetExpiredTransactionState();
        if (!ReferenceEquals(primary, context) || context.Database.CurrentTransaction is null
            || !ModerationParticipantInvestigationQualification.IsValid(request)
            || request.Target != authority.Tenant || caller.UserId != authority.UserId
            || !caller.IsAuthenticated || machine.IsMachineCaller
            || context.TenantFilterTenantId != authority.Tenant.Id || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId)
            return null;

        // The writer authority comes only from ModerationQualificationAuthority, which fences
        // these rows before host/tenant/epoch. Mirror its borrowed custody, never lock Humans late.
        var humans = authority.FencedHumans.ToArray();
        if (!humans.Contains(authority.UserId) || !humans.Contains(request.SubjectHumanId)) return null;
        foreach (var id in humans) _fences.Add((typeof(User), id));
        foreach (var id in await context.PlatformUserRoles.AsNoTracking()
            .Where(row => humans.Contains(row.UserId)).Select(row => row.Id).ToArrayAsync(token))
            _fences.Add((typeof(PlatformUserRole), id));
        var members = await context.TenantUsers.AsNoTracking()
            .Where(row => row.TenantId == authority.Tenant.Id && humans.Contains(row.UserId))
            .Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in members) _fences.Add((typeof(TenantUser), id));
        foreach (var id in await context.TenantUserRoleGrants.AsNoTracking()
            .Where(row => members.Contains(row.TenantUserId)).Select(row => row.Id).ToArrayAsync(token))
            _fences.Add((typeof(TenantUserRoleGrant), id));
        _fences.Add((typeof(Tenant), authority.Tenant.Id));
        _fences.Add((typeof(ModerationHostBinding), authority.Binding.Id));
        _fences.Add((typeof(ServicePrincipal), authority.Binding.MaintenanceServicePrincipalId));
        _fences.Add((typeof(ModerationQualificationEpoch), authority.Epoch.Id));
        var environment = await EnvironmentAsync(false, token);
        var now = clock.GetUtcNow();
        if (environment is null || environment.CallerId != authority.UserId
            || environment.HostId != authority.Binding.Id || environment.TrustId != authority.Binding.TrustDomainId
            || environment.Scope != authority.Tenant || environment.EpochId != authority.Epoch.Id
            || environment.EpochRevision != authority.Epoch.Revision
            || qualifiedAt != PersistedTime(qualifiedAt) || qualifiedAt > now
            || request.IncidentWindowUntil > qualifiedAt || now >= request.ValidUntil || now >= request.PurgeAt
            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == environment.TenantId
                && row.TenantStatusId == (int)TenantStatusEnum.Active, token))
            return null;

        if (!await FenceAsync<ModerationCaseReadGrant>(request.ReadGrantId, true, token)) return null;
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.ReadGrantId, token);
        if (grant is null || !humans.Contains(grant.CallerUserId) || !humans.Contains(grant.IssuerUserId)
            || grant.CallerUserId != environment.CallerId || grant.CaseId != request.OccurrenceCaseId
            || grant.CaseRevision != request.ExpectedCaseRevision || grant.SourceId != request.EvidenceSourceId
            || grant.TenantId != environment.TenantId || grant.TrustDomainId != environment.TrustId
            || !grant.IsCurrentAt(now) || qualifiedAt < grant.IssuedAt || request.PurgeAt > grant.ExpiresAt
            || request.SubjectHumanId == grant.CallerUserId || request.SubjectHumanId == grant.IssuerUserId
            || !authority.Administrators.Contains(grant.CallerUserId)
            || !authority.Administrators.Contains(grant.IssuerUserId)
            || !await AccountAsync(request.SubjectHumanId, false, token))
            return null;
        var assessor = await AdministratorAsync(environment, grant.CallerUserId, null, null, false, token);
        if (assessor is null || !await FenceAsync<ModerationCaseTargetSource>(grant.SourceId, true, token)) return null;
        var material = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == grant.SourceId, token);
        if (material is null || material.Kind != ModerationCaseTargetKind.ParticipantIncident
            || material.NativeKind != ModerationCaseTargetKind.ParticipantIncident
            || material.Version != request.EvidenceSourceVersion || material.RecordedAt > qualifiedAt
            || material.TenantId != environment.TenantId || material.TrustDomainId != environment.TrustId
            || material.EventId is not { } eventId)
            return null;
        var native = await NativeTargetAsync(material.NativeKind, material.NativeTargetId, eventId, true, token);
        if (native is null || native.Fingerprint != material.NativeFactsDigest
            || native.TargetVersion != material.NativeTargetVersion || native.EventVersion != material.EventVersion)
            return null;
        var policy = await PolicyAsync(material.Kind, true, token);
        if (policy is null || policy.TenantId != environment.TenantId || policy.TrustDomainId != environment.TrustId
            || request.ValidUntil - qualifiedAt > policy.ReadLifetime)
            return null;
        await using var occurrenceSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.case-occurrence:{environment.TrustId:D}:{environment.TenantId:D}:{material.OccurrenceId:D}", token);
        if (!await FenceAsync<ModerationCase>(request.OccurrenceCaseId, true, token)) return null;
        var occurrence = await context.ModerationCases.AsNoTracking().Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.Id == request.OccurrenceCaseId, token);
        if (occurrence is null || occurrence.Subject is not null || occurrence.Target is null
            || occurrence.Scope != environment.Scope || occurrence.Revision != request.ExpectedCaseRevision
            || occurrence.OccurrenceId != material.OccurrenceId
            || !occurrence.Evidence.Any(row => row.CaseRevision == request.EvidenceCaseRevision
                && row.ToReference(occurrence.Target, occurrence.OccurrenceId) == material.Evidence(environment.Scope)))
            return null;
        occurrence.ValidatePersistedState();
        var relationship = await RelationshipAsync(ModerationCaseRelationshipKind.CaseReadGrant, grant.Id,
            material, native, environment, occurrence.Id, false, now, token);
        if (relationship is null || request.PurgeAt > relationship.Until) return null;

        if (!await FenceAsync<EventSession>(request.SessionId, true, token)) return null;
        var session = await context.EventSessions.AsNoTracking().Include(row => row.Event)
            .SingleOrDefaultAsync(row => row.Id == request.SessionId && row.EventId == eventId
                && row.TenantId == environment.TenantId && !row.IsDeleted, token);
        if (session is null || session.ConcurrencyStamp != request.ExpectedSessionGeneration
            || session.Event.IsDeleted || session.Event.ConcurrencyStamp != material.EventVersion
            || session.StartTime is not { } start || session.EndTime is not { } end
            || start > request.IncidentWindowFrom || end < request.IncidentWindowUntil)
            return null;
        var order = await context.RegistrationOrders.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == native.ParentId && row.EventId == eventId && !row.IsDeleted, token);
        var participant = await context.RegistrationParticipants.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == material.NativeTargetId && row.RegistrationOrderId == native.ParentId && !row.IsDeleted, token);
        if (order is null || participant is null || order.ConcurrencyStamp != native.ParentVersion
            || participant.ConcurrencyStamp != native.TargetVersion) return null;

        var original = await context.ModerationCaseNarratives.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CaseId == occurrence.Id && row.SourceId == material.Id && row.SourceVersion == material.Version
            && row.MaterialFactId == material.MaterialFactId, token);
        if (original is null || !humans.Contains(original.ReporterUserId)
            || original.ReporterUserId == grant.CallerUserId
            || !await AccountAsync(original.ReporterUserId, false, token)
            || !await FenceAsync<ModerationCaseNarrative>(original.Id, true, token)
            || qualifiedAt < original.CapturedAt || request.PurgeAt > original.PurgeAt) return null;
        var custody = new ModerationCaseNarrativePolicyReader(context, clock);
        var allegation = await custody.ReadAsync(authority, material.Kind, token,
            ModerationCaseNarrativePurpose.ConfidentialAllegation);
        var investigation = await custody.ReadAsync(authority, material.Kind, token,
            ModerationCaseNarrativePurpose.ParticipantInvestigation);
        if (allegation is null || investigation is null
            || allegation.Value.Source.Id == investigation.Value.Source.Id
            || allegation.Value.Adoption.Id == investigation.Value.Adoption.Id
            || request.PurgeAt > investigation.Value.Adoption.ValidUntil
            || qualifiedAt < investigation.Value.Adoption.AdoptedAt
            || request.PurgeAt - qualifiedAt > investigation.Value.Source.Terms.MaximumRetention)
            return null;
        var plaintext = ModerationOriginalNarrativeAuthentication.Authenticate(protection, original,
            material, allegation.Value.Source, allegation.Value.Adoption, environment.Scope, now);
        if (plaintext is null) return null;
        CryptographicOperations.ZeroMemory(plaintext);
        var originalCipher = original.ProtectedMaterial.ToArray();
        string originalDigest;
        try { originalDigest = Digest(originalCipher); }
        finally { CryptographicOperations.ZeroMemory(originalCipher); }
        if (await EnvironmentAsync(false, token) != environment || clock.GetUtcNow() < now
            || clock.GetUtcNow() >= request.ValidUntil) return null;
        return new(authority.Binding, occurrence, material, original, session, order, participant,
            SubjectReference.Create(ModerationSubjectKind.Human, request.SubjectHumanId, environment.Scope.Parent!),
            assessor.GrantId, policy, investigation.Value.Source, investigation.Value.Adoption, grant,
            ParticipantInvestigationAuthorityDigest(native, assessor, relationship, grant, session, original, originalDigest));
    }

    private static string ParticipantInvestigationAuthorityDigest(NativeTarget native, Administrator assessor,
        Relationship relationship, ModerationCaseReadGrant grant, EventSession session,
        ModerationCaseNarrative original, string originalDigest) =>
        Digest(System.Text.Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"{native.Fingerprint}|{assessor.Fingerprint}|{relationship.Fingerprint}|{grant.IssuedAt.UtcTicks}|{grant.IssuerRoleGrantId:N}|{grant.IssuerPlatformGrant}|{session.StartTime!.Value.UtcTicks}|{session.EndTime!.Value.UtcTicks}|{(int)session.EndTimeType}|{session.EventSessionStatusId}|{originalDigest}|{original.ReporterUserId:N}|{original.ReceiptId:N}|{original.PolicySourceId:N}|{original.AdoptionId:N}|{original.PolicyWriterGeneration}|{original.AdoptionWriterGeneration}|{original.CapturedAt.UtcTicks}|{original.QualificationEpochId:N}|{original.QualificationRevision}")));

    internal sealed record ParticipantInvestigationFacts(
        ModerationHostBinding Host, ModerationCase Occurrence, ModerationCaseTargetSource Material,
        ModerationCaseNarrative Original, EventSession Session, RegistrationOrder Order,
        RegistrationParticipant Participant, SubjectReference Subject, Guid AssessorRoleGrantId,
        ModerationCaseTargetPolicy Policy, ModerationCaseNarrativePolicySource CustodySource,
        ModerationCaseNarrativeAdoption CustodyAdoption, ModerationCaseReadGrant Grant, string AuthorityDigest);
}
