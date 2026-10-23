using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner : IModerationCaseAttributionOwner
{
    private PendingWrite? _attribution;
    private ModerationAuthorityParticipant.FindingProvenanceSession? _attributionProvenance;

    public async Task<ModerationCase?> StageAttributionAsync(
        ModerationCaseAttributionRequest request, Guid caseId, CancellationToken cancellationToken)
    {
        ResetAttributionTransaction();
        if (_attribution is not null || _pending is not null || _proof is not null
            || request.OperationId == Guid.Empty || caseId == Guid.Empty)
            return null;
        var facts = await AttributionFactsAsync(request, true, cancellationToken);
        if (facts is null || facts.Grant.CallerUserId != facts.Environment.CallerId) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.case-attribution:{facts.Environment.TenantId:D}:{request.OccurrenceCaseId:D}:" +
            $"{(int)request.SubjectKind}:{request.SubjectId:D}", cancellationToken);
        var source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.OperationId, cancellationToken);
        ModerationCase? adjudication;
        ModerationCaseAttributionHead head;
        if (source is not null)
        {
            if (!await FenceAsync<ModerationCaseAttributionSource>(source.Id, true, cancellationToken)
                || !await FenceAsync<ModerationCase>(source.CaseId, true, cancellationToken))
                return null;
            source = await context.ModerationCaseAttributionSources.AsNoTracking()
                .SingleAsync(row => row.Id == source.Id, cancellationToken);
            adjudication = await context.ModerationCases.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.CaseId, cancellationToken);
            var discoveredHead = await context.ModerationCaseAttributionHeads.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CaseId == source.CaseId, cancellationToken);
            if (discoveredHead is null
                || !await FenceAsync<ModerationCaseAttributionHead>(discoveredHead.Id, true, cancellationToken))
                return null;
            head = await context.ModerationCaseAttributionHeads.AsNoTracking()
                .SingleAsync(row => row.Id == discoveredHead.Id, cancellationToken);
            if (adjudication is null || !AttributionMatches(source, adjudication, head, request, facts, clock.GetUtcNow()))
                return null;
        }
        else
        {
            // One qualification per subject, not one per report, alias, basis or retry.
            if (await context.ModerationCaseAttributionSources.AsNoTracking().AnyAsync(row =>
                row.OccurrenceCaseId == request.OccurrenceCaseId && row.SubjectKind == request.SubjectKind
                && row.SubjectId == request.SubjectId, cancellationToken))
                return null;
            var now = PersistedTime(clock.GetUtcNow());
            var host = await context.ModerationHostBindings.AsNoTracking()
                .SingleAsync(row => row.Id == facts.Environment.HostId, cancellationToken);
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                .SingleAsync(row => row.Id == facts.Environment.EpochId, cancellationToken);
            source = ModerationCaseAttributionSource.Qualify(request.OperationId, caseId, 1, facts.Occurrence,
                request.EvidenceCaseRevision, facts.Material, facts.Subject, request.Basis, host, epoch,
                facts.Grant, facts.Policy, facts.Qualifier.GrantId, facts.Qualifier.Platform,
                facts.Digest, facts.AuthorityDigest, now, request.ValidUntil, facts.Investigation);
            adjudication = ModerationCase.OpenAttributed(caseId, facts.Subject, facts.Occurrence,
                request.EvidenceCaseRevision, source.Id, now);
            head = ModerationCaseAttributionHead.Open(Guid.CreateVersion7(), adjudication, source, host, now);
            context.ModerationCases.Add(adjudication);
            context.ModerationCaseAttributionSources.Add(source);
            context.ModerationCaseAttributionHeads.Add(head);
            _fences.Add((typeof(ModerationCase), adjudication.Id));
            _fences.Add((typeof(ModerationCaseAttributionSource), source.Id));
            _fences.Add((typeof(ModerationCaseAttributionHead), head.Id));
        }
        HoldAttribution(request, source, adjudication, head, facts);
        return adjudication;
    }

    public Task<ModerationCaseAttributionSource?> AcquireCurrentAttributionAsync(
        Guid caseId, CancellationToken cancellationToken) =>
        AcquireAttributionAsync(caseId, null, cancellationToken);

    public Task<ModerationCaseAttributionSource?> AcquireCurrentAttributionAsync(
        IModerationFindingProvenanceSession provenance, Guid caseId, CancellationToken cancellationToken) =>
        provenance is ModerationAuthorityParticipant.FindingProvenanceSession native
            ? AcquireAttributionAsync(caseId, native, cancellationToken)
            : Task.FromResult<ModerationCaseAttributionSource?>(null);

    private async Task<ModerationCaseAttributionSource?> AcquireAttributionAsync(
        Guid caseId, ModerationAuthorityParticipant.FindingProvenanceSession? provenance,
        CancellationToken cancellationToken)
    {
        ResetAttributionTransaction();
        if (_pending is not null || _proof is not null
            || _attribution is not null && !ReferenceEquals(_attributionProvenance, provenance)
            || provenance is not null && await provenance.AcquireAsync(context, caseId, cancellationToken) is null)
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null || context.ModerationTrustDomainId is { } trust && trust != host.TrustDomainId) return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var discoveredHead = await context.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CaseId == caseId, cancellationToken);
        if (discoveredHead is null) return null;
        var source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discoveredHead.SourceId && row.CaseId == caseId, cancellationToken);
        if (source is null) return null;
        var request = RequestFor(source);
        var facts = await AttributionFactsAsync(request, true, cancellationToken, provenance);
        if (facts is null || !await FenceAsync<ModerationCaseAttributionHead>(discoveredHead.Id, true, cancellationToken)
            || !await FenceAsync<ModerationCaseAttributionSource>(source.Id, true, cancellationToken)
            || !await FenceAsync<ModerationCase>(caseId, true, cancellationToken)) return null;
        var head = await context.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleAsync(row => row.Id == discoveredHead.Id, cancellationToken);
        if (head.SourceId != source.Id || head.Generation != source.Generation
            || head.Revision != discoveredHead.Revision) return null;
        source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleAsync(row => row.Id == source.Id, cancellationToken);
        var adjudication = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == caseId, cancellationToken);
        if (adjudication is null || !AttributionMatches(source, adjudication, head, request, facts, clock.GetUtcNow()))
            return null;
        HoldAttribution(request, source, adjudication, head, facts, provenance);
        return source;
    }

    private void HoldAttribution(ModerationCaseAttributionRequest request,
        ModerationCaseAttributionSource source, ModerationCase adjudication,
        ModerationCaseAttributionHead head, AttributionFacts facts,
        ModerationAuthorityParticipant.FindingProvenanceSession? provenance = null)
    {
        _attributionProvenance = provenance;
        var caseRevision = adjudication.Revision;
        var headTuple = (head.Id, head.SourceId, head.Generation, head.Revision);
        var previous = _attribution;
        _attribution = new(facts.Environment, source.QualifiedAt, source.ValidUntil, async token =>
        {
            if (previous is not null && !await previous.Recheck(token)) return false;
            var current = await AttributionFactsAsync(request, false, token, provenance);
            if (current is null || current.Environment.EpochRevision < facts.Environment.EpochRevision
                || (current.Environment with { EpochRevision = facts.Environment.EpochRevision }) != facts.Environment
                || current.Digest != facts.Digest || current.AuthorityDigest != facts.AuthorityDigest)
                return false;
            var persisted = await context.ModerationCaseAttributionSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.Id, token);
            var persistedCase = await context.ModerationCases.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adjudication.Id, token);
            var persistedHead = await context.ModerationCaseAttributionHeads.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == headTuple.Id, token);
            return persisted is not null && persistedCase is not null && persistedHead is not null
                && persistedHead.SourceId == headTuple.SourceId && persistedHead.Generation == headTuple.Generation
                && persistedHead.Revision == headTuple.Revision && persistedCase.Revision == caseRevision
                && persisted.CaseId == source.CaseId && persisted.Generation == source.Generation
                && persisted.QualifiedAt == source.QualifiedAt
                && AttributionMatches(persisted, persistedCase, persistedHead, request, current, clock.GetUtcNow());
        });
    }

    public async Task<ModerationCaseAttributionSource?> StageAttributionWithdrawalAsync(
        Guid sourceId, Guid operationId, long expectedRevision, CancellationToken cancellationToken)
    {
        ResetAttributionTransaction();
        if (_attribution is not null || _pending is not null || _proof is not null
            || operationId == Guid.Empty || expectedRevision != 1) return null;
        var environment = await EnvironmentAsync(true, cancellationToken);
        if (environment is null) return null;
        var admin = await AdministratorAsync(environment, environment.CallerId, null, null, true, cancellationToken);
        if (admin is null || !await FenceAsync<ModerationCaseAttributionSource>(sourceId, true, cancellationToken))
            return null;
        var source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId, cancellationToken);
        if (source is null || source.HostBindingId != environment.HostId
            || source.SubjectKind is ModerationSubjectKind.Service or ModerationSubjectKind.Organization && !admin.Platform)
            return null;
        source.ValidatePersistedState();
        if (source.WithdrawnAt is not null)
        {
            if (source.WithdrawalOperationId != operationId || source.WithdrawnByHumanId != environment.CallerId)
                return null;
        }
        else
        {
            if (context.ChangeTracker.Entries<ModerationCaseAttributionSource>().Any(row => row.Entity.Id == sourceId))
                return null;
            context.Attach(source);
            source.Withdraw(operationId, expectedRevision, environment.CallerId, PersistedTime(clock.GetUtcNow()));
        }
        var withdrawnAt = source.WithdrawnAt;
        _attribution = new(environment, withdrawnAt!.Value, DateTimeOffset.MaxValue, async token =>
            await AdministratorAsync(environment, environment.CallerId, admin.GrantId, admin.Platform, false, token) == admin
            && await context.ModerationCaseAttributionSources.AsNoTracking().AnyAsync(row =>
                row.Id == sourceId && row.CaseId == source.CaseId && row.Revision == 2
                && row.HostBindingId == environment.HostId && row.WithdrawnAt == withdrawnAt
                && row.WithdrawalOperationId == operationId && row.WithdrawnByHumanId == environment.CallerId, token));
        return source;
    }

    public async Task<bool> RecheckAttributionAsync(CancellationToken cancellationToken)
    {
        if (_attribution is not { } pending || context.ChangeTracker.HasChanges())
            return false;
        var current = _attributionProvenance is { } provenance
            ? await AttributionEnvironmentAsync(provenance, false, cancellationToken)
            : await EnvironmentAsync(false, cancellationToken);
        if (current is null || current.EpochRevision < pending.Environment.EpochRevision
            || (current with { EpochRevision = pending.Environment.EpochRevision }) != pending.Environment
            || !await pending.Recheck(cancellationToken)
            || (_attributionProvenance is { } retained
                ? await AttributionEnvironmentAsync(retained, false, cancellationToken)
                : await EnvironmentAsync(false, cancellationToken)) != current)
            return false;
        var now = clock.GetUtcNow();
        return now >= pending.NotBefore && now < pending.ValidUntil;
    }

    private void ResetAttributionTransaction()
    {
        if (_attribution?.Environment.TransactionId != context.Database.CurrentTransaction?.TransactionId)
        {
            _attribution = null;
            _attributionProvenance = null;
        }
        ResetExpiredTransactionState();
    }

    private async Task<AttributionFacts?> AttributionFactsAsync(
        ModerationCaseAttributionRequest request, bool acquire, CancellationToken token,
        ModerationAuthorityParticipant.FindingProvenanceSession? provenance = null)
    {
        var now = clock.GetUtcNow();
        if (request.OperationId == Guid.Empty || request.OccurrenceCaseId == Guid.Empty || request.ReadGrantId == Guid.Empty
            || request.ExpectedCaseRevision < 1 || request.EvidenceCaseRevision < 1
            || request.EvidenceCaseRevision > request.ExpectedCaseRevision || !Enum.IsDefined(request.Basis)
            || !Enum.IsDefined(request.SubjectKind) || request.SubjectId == Guid.Empty
            || (request.Basis == ModerationResponsibilityBasis.ParticipantInvestigation
                ? request.SubjectKind != ModerationSubjectKind.Human
                    || request.InvestigationSourceId is not { Version: 7, Variant: >= 8 and <= 11 }
                    || request.InvestigationSourceRevision != 1
                : request.InvestigationSourceId is not null || request.InvestigationSourceRevision is not null)
            || request.ValidUntil <= now || request.ValidUntil == DateTimeOffset.MaxValue
            || request.ValidUntil != PersistedTime(request.ValidUntil))
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || context.ModerationTrustDomainId is { } trust && trust != host.TrustDomainId) return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var discovered = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.ReadGrantId, token);
        if (discovered is null) return null;
        var environment = provenance is null ? null : await AttributionEnvironmentAsync(provenance, acquire, token);
        if (provenance is not null && environment is null) return null;
        if (provenance is null && caller.UserId is null) return null;
        // Human/erasure writer rows precede host, grant, target and case locks. Discovery is
        // only an acquisition plan; all facts are reread after those native fences are held.
        IEnumerable<Guid> humans = new[] { discovered.CallerUserId, discovered.IssuerUserId }
            .Append(request.SubjectKind == ModerationSubjectKind.Human ? request.SubjectId : discovered.CallerUserId);
        if (request.InvestigationSourceId is { } investigationId)
            humans = humans.Concat(await ModerationParticipantInvestigationHumanDiscovery.DiscoverAsync(
                context, investigationId, host.Id, context.TenantFilterTenantId ?? Guid.Empty, token));
        if (provenance is null) humans = humans.Append(caller.UserId!.Value);
        var humanIds = humans.Distinct().Order().ToArray();
        if (provenance is not null)
        {
            var borrowed = await provenance.AcquireAsync(context, provenance.CaseId, token);
            if (borrowed is null || humanIds.Any(human => !borrowed.FencedHumans.Contains(human))) return null;
        }
        foreach (var human in humanIds)
            if (!await AccountAsync(human, provenance is null && acquire, token)) return null;
        if (request.InvestigationSourceId is not null && provenance is null && acquire)
            await FenceInvestigationAdministrativeRowsAsync(humanIds, token);
        environment ??= await EnvironmentAsync(acquire, token);
        if (environment is null
            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == environment.TenantId
                && row.TenantStatusId == (int)TenantStatusEnum.Active, token)
            || !await FenceAsync<ModerationCaseReadGrant>(discovered.Id, acquire, token)) return null;
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (grant is null || grant.CallerUserId != discovered.CallerUserId || grant.IssuerUserId != discovered.IssuerUserId
            || grant.CaseId != request.OccurrenceCaseId || grant.CaseRevision != request.ExpectedCaseRevision
            || !grant.IsCurrentAt(now) || request.ValidUntil > grant.ExpiresAt
            || request.SubjectKind == ModerationSubjectKind.Human
                && (request.SubjectId == grant.CallerUserId || request.SubjectId == grant.IssuerUserId))
            return null;
        var qualifier = await AdministratorAsync(environment, grant.CallerUserId, null,
            request.SubjectKind is ModerationSubjectKind.Organization or ModerationSubjectKind.Service ? true : null,
            provenance is null && acquire, token);
        if (qualifier is null || !await FenceAsync<ModerationCaseTargetSource>(grant.SourceId, acquire, token)) return null;
        var material = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == grant.SourceId, token);
        if (material is null || material.RecordedAt > now) return null;
        var native = await NativeTargetAsync(material.NativeKind, material.NativeTargetId, material.EventId, acquire, token);
        if (native is null || native.Fingerprint != material.NativeFactsDigest
            || native.TargetVersion != material.NativeTargetVersion || native.EventVersion != material.EventVersion)
            return null;
        var subject = await ResponsibilitySubjectAsync(request, material, native, environment, acquire, token);
        if (subject is null) return null;
        var policy = await PolicyAsync(material.Kind, acquire, token);
        if (policy is null || request.ValidUntil - now > policy.ReadLifetime) return null;
        // Root intake uses this same slot; a subject case never becomes canonical intake.
        if (acquire)
        {
            await using var occurrenceFence = await RelationalNamedLock.AcquireTransactionAsync(context,
                $"moderation.case-occurrence:{environment.TrustId:D}:{environment.TenantId:D}:{material.OccurrenceId:D}", token);
        }
        if (!await FenceAsync<ModerationCase>(request.OccurrenceCaseId, acquire, token)) return null;
        var occurrence = await context.ModerationCases.AsNoTracking().Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.Id == request.OccurrenceCaseId, token);
        if (occurrence is null || occurrence.Subject is not null || occurrence.Target is null
            || occurrence.Scope != environment.Scope || occurrence.Revision != request.ExpectedCaseRevision
            || !occurrence.Evidence.Any(row => row.CaseRevision == request.EvidenceCaseRevision
                && row.ToReference(occurrence.Target, occurrence.OccurrenceId) == material.Evidence(environment.Scope)))
            return null;
        occurrence.ValidatePersistedState();
        var relationship = await RelationshipAsync(ModerationCaseRelationshipKind.CaseReadGrant, grant.Id,
            material, native, environment with { CallerId = grant.CallerUserId }, occurrence.Id,
            provenance is null && acquire, now, token);
        if (relationship is null) return null;
        ModerationParticipantIncidentResponsibilitySource? investigation = null;
        if (request.Basis == ModerationResponsibilityBasis.ParticipantInvestigation)
        {
            investigation = await ParticipantResponsibilityAsync(
                request, material, native, environment, occurrence, policy, acquire, token);
            if (investigation is null) return null;
        }
        var digest = Digest(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"attribution.v2|{(int)request.Basis}|{(int)subject.Kind}|{subject.Id:D}|{native.Fingerprint}") +
            (investigation is null ? string.Empty : FormattableString.Invariant(
                $"|{investigation.AssessmentDigest}|{investigation.SessionId:N}|{investigation.SessionGeneration:N}|{investigation.IncidentWindowFrom.UtcTicks}|{investigation.IncidentWindowUntil.UtcTicks}"))));
        var authorityDigest = Digest(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"attribution-qualification.v1|{qualifier.Fingerprint}|{relationship.Fingerprint}|{policy.Id:D}|{policy.Revision}") +
            (investigation is null ? string.Empty : "|" + ModerationParticipantInvestigationAuthentication.Binding(investigation))));
        return new(environment, occurrence, material, grant, policy, qualifier, subject, digest, authorityDigest, investigation);
    }

    private async Task<SubjectReference?> ResponsibilitySubjectAsync(
        ModerationCaseAttributionRequest request, ModerationCaseTargetSource source, NativeTarget native,
        NativeEnvironment environment, bool acquire, CancellationToken token)
    {
        switch (request.Basis)
        {
            case ModerationResponsibilityBasis.ParticipantInvestigation:
                // This claimed coordinate is usable only after ParticipantResponsibilityAsync
                // authenticates its actual protected assessment and native producer receipt.
                if (request.SubjectKind != ModerationSubjectKind.Human
                    || source.Kind != ModerationCaseTargetKind.ParticipantIncident
                    || source.NativeKind != ModerationCaseTargetKind.ParticipantIncident)
                    return null;
                return SubjectReference.Create(ModerationSubjectKind.Human, request.SubjectId, environment.Scope.Parent!);
            case ModerationResponsibilityBasis.EventSubmission:
                // Authored submission, not publisher Actor.UserId, resource ownership or a
                // participant's LinkedUserId. The reviewer explicitly alleges this responsibility.
                if (request.SubjectKind != ModerationSubjectKind.Human
                    || source.Kind != ModerationCaseTargetKind.EventVersion
                    || source.NativeKind != ModerationCaseTargetKind.EventVersion
                    || native.SubmitterId != request.SubjectId) return null;
                return SubjectReference.Create(ModerationSubjectKind.Human, request.SubjectId, environment.Scope.Parent!);
            case ModerationResponsibilityBasis.EventContent:
                if (request.SubjectKind != ModerationSubjectKind.Content || source.EventId != request.SubjectId
                    || source.Kind is ModerationCaseTargetKind.LocalIncident or ModerationCaseTargetKind.ParticipantIncident
                    || source.NativeKind is not (ModerationCaseTargetKind.EventVersion or ModerationCaseTargetKind.Resource
                        or ModerationCaseTargetKind.Form or ModerationCaseTargetKind.Destination)) return null;
                return SubjectReference.Create(ModerationSubjectKind.Content, request.SubjectId, environment.Scope);
            case ModerationResponsibilityBasis.OrganizationalActor:
                if (request.SubjectKind != ModerationSubjectKind.Organization || source.Kind != ModerationCaseTargetKind.Actor
                    || source.NativeKind != ModerationCaseTargetKind.Actor
                    || !await FenceAsync<Organization>(request.SubjectId, acquire, token)
                    || !await context.Organizations.AsNoTracking().AnyAsync(row => row.Id == request.SubjectId && !row.IsDeleted, token)
                    || !await context.Actors.AsNoTracking().AnyAsync(row => row.Id == source.NativeTargetId
                        && row.ActorTypeId == (int)ActorTypeEnum.Organization && row.OrganizationId == request.SubjectId
                        && !row.IsDeleted, token)) return null;
                return SubjectReference.Create(ModerationSubjectKind.Organization, request.SubjectId, environment.Scope.Parent!);
            case ModerationResponsibilityBasis.ServiceIdentity:
                if (request.SubjectKind != ModerationSubjectKind.Service || source.Kind != ModerationCaseTargetKind.Service
                    || source.NativeKind != ModerationCaseTargetKind.Service || source.NativeTargetId != request.SubjectId)
                    return null;
                return SubjectReference.Create(ModerationSubjectKind.Service, request.SubjectId, environment.Scope.Parent!);
            default:
                return null;
        }
    }

    private static ModerationCaseAttributionRequest RequestFor(ModerationCaseAttributionSource source) =>
        new(source.Id, source.OccurrenceCaseId, source.OccurrenceCaseRevision, source.EvidenceCaseRevision,
            source.ReadGrantId, source.Basis, source.SubjectKind, source.SubjectId, source.ValidUntil)
        {
            InvestigationSourceId = source.InvestigationSourceId,
            InvestigationSourceRevision = source.InvestigationSourceRevision
        };

    private static bool AttributionMatches(ModerationCaseAttributionSource source, ModerationCase adjudication,
        ModerationCaseAttributionHead head, ModerationCaseAttributionRequest request,
        AttributionFacts facts, DateTimeOffset now)
    {
        source.ValidatePersistedState();
        adjudication.ValidatePersistedState();
        head.ValidatePersistedState();
        return RequestFor(source) == request && source.IsCurrentAt(now)
            && source.CaseId == adjudication.Id && head.CaseId == adjudication.Id
            && head.SourceId == source.Id && head.Generation == source.Generation
            && adjudication.AttributionSourceId == head.OpeningSourceId
            && head.TenantId == source.TenantId && head.TrustDomainId == source.TrustDomainId
            && head.SolutionId == adjudication.ScopeSolutionId && head.InstanceId == adjudication.ScopeInstanceId
            && head.HostBindingId == source.HostBindingId && head.SubjectKind == source.SubjectKind
            && head.SubjectId == source.SubjectId && head.OccurrenceCaseId == source.OccurrenceCaseId
            && head.OccurrenceId == source.OccurrenceId
            && adjudication.OccurrenceCaseId == facts.Occurrence.Id
            && adjudication.OccurrenceEvidenceRevision == head.OccurrenceEvidenceRevision
            && source.EvidenceCaseRevision >= head.OccurrenceEvidenceRevision
            && adjudication.OccurrenceId == facts.Occurrence.OccurrenceId && adjudication.Subject == facts.Subject
            && adjudication.Scope == facts.Environment.Scope
            && source.OccurrenceId == facts.Occurrence.OccurrenceId
            && source.EvidenceSourceId == facts.Material.Id && source.EvidenceSourceVersion == facts.Material.Version
            && source.MaterialFactId == facts.Material.MaterialFactId
            && source.TenantId == facts.Environment.TenantId && source.TrustDomainId == facts.Environment.TrustId
            && source.HostBindingId == facts.Environment.HostId && source.QualificationEpochId == facts.Environment.EpochId
            && source.QualificationRevision <= facts.Environment.EpochRevision
            && source.QualifiedByHumanId == facts.Grant.CallerUserId
            && source.QualifierRoleGrantId == facts.Qualifier.GrantId && source.QualifierPlatformGrant == facts.Qualifier.Platform
            && source.ReadGrantRevision == facts.Grant.Revision
            && source.LifetimePolicyId == facts.Policy.Id && source.LifetimePolicyRevision == facts.Policy.Revision
            && source.ValidUntil - source.QualifiedAt <= facts.Policy.ReadLifetime
            && source.NativeResponsibilityDigest == facts.Digest
            && source.QualificationFactsDigest == facts.AuthorityDigest;
    }

    private sealed record AttributionFacts(NativeEnvironment Environment, ModerationCase Occurrence,
        ModerationCaseTargetSource Material, ModerationCaseReadGrant Grant, ModerationCaseTargetPolicy Policy,
        Administrator Qualifier, SubjectReference Subject, string Digest, string AuthorityDigest,
        ModerationParticipantIncidentResponsibilitySource? Investigation);
}
