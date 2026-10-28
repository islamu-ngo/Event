using System.Text;
using System.Text.Json;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    internal static Task<Guid?[]> DiscoverResourceSafetyAttributionHumansAsync(
        ExploreDbContext context, Guid caseId, CancellationToken token) =>
        (from head in context.ModerationCaseAttributionHeads.AsNoTracking()
         join source in context.ModerationCaseAttributionSources.AsNoTracking() on head.SourceId equals source.Id
         where head.CaseId == caseId && source.CaseId == caseId
         select (Guid?)source.QualifiedByHumanId).ToArrayAsync(token);

    /// <summary>
    /// Validates retained attribution metadata for a new Resource safety purpose, never the
    /// original protected allegation or its grant. Only the native release owner's live
    /// capability enters here; existing finding provenance retains its stronger disclosure
    /// qualification contract. Head advancement remains the expected-head renewal writer's job.
    /// </summary>
    internal static async Task<ResourceSafetyAttribution?> AcquireResourceSafetyAttributionAsync(
        ExploreDbContext context,
        ModerationResourceQuarantineReleaseRepository.ResourceSafetyProvenanceSession session,
        CancellationToken token)
    {
        if (!await session.IsCurrentAsync(context, token)) return null;
        var authority = session.Authority;
        var native = session.Native;
        var discovered = await context.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CaseId == session.Case.Id, token);
        if (discovered is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseAttributionHead>(context, discovered.Id, token);
        var head = await context.ModerationCaseAttributionHeads.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id && row.CaseId == session.Case.Id, token);
        if (head is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseAttributionSource>(context, head.SourceId, token);
        var source = await context.ModerationCaseAttributionSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == head.SourceId && row.CaseId == session.Case.Id, token);
        if (source is null || !source.IsCurrentAt(session.CurrentTime)
            || !authority.FencedHumans.Contains(source.QualifiedByHumanId)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseTargetSource>(context, source.EvidenceSourceId, token);
        var material = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.EvidenceSourceId, token);
        if (material is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, source.OccurrenceCaseId, token);
        var occurrence = await context.ModerationCases.AsNoTracking().Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.Id == source.OccurrenceCaseId, token);
        var owner = await context.ModerationCases.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == session.Case.Id, token);
        if (occurrence is null || owner is null) return null;
        try
        {
            head.ValidatePersistedState();
            source.ValidatePersistedState();
            occurrence.ValidatePersistedState();
            owner.ValidatePersistedState();
        }
        catch (ArgumentException) { return null; }
        if (owner.Scope != authority.Tenant || owner.Revision != session.Case.Revision
            || owner.State != ModerationCaseState.Open || owner.Subject != session.Case.Subject
            || owner.Subject is not { Kind: ModerationSubjectKind.Content } subject
            || subject.Id != native.EventId || subject.Origin != authority.Tenant
            || head.TenantId != authority.Tenant.Id || head.TrustDomainId != authority.Binding.TrustDomainId
            || head.HostBindingId != authority.Binding.Id || head.SolutionId != authority.Binding.SolutionId
            || head.InstanceId != authority.Binding.InstanceId || head.SubjectKind != subject.Kind || head.SubjectId != subject.Id
            || head.OpeningSourceId != owner.AttributionSourceId || head.OccurrenceCaseId != owner.OccurrenceCaseId
            || head.OccurrenceId != owner.OccurrenceId || head.OccurrenceEvidenceRevision != owner.OccurrenceEvidenceRevision
            || head.SourceId != source.Id || head.Generation != source.Generation
            || source.TenantId != authority.Tenant.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.HostBindingId != authority.Binding.Id || source.QualificationEpochId != authority.Epoch.Id
            || source.QualificationRevision > authority.Epoch.Revision
            || source.SubjectKind != subject.Kind || source.SubjectId != subject.Id
            || source.Basis != ModerationResponsibilityBasis.EventContent
            || source.OccurrenceCaseId != head.OccurrenceCaseId || source.OccurrenceId != head.OccurrenceId
            || source.EvidenceCaseRevision < head.OccurrenceEvidenceRevision
            || source.EvidenceSourceVersion != material.Version || source.MaterialFactId != material.MaterialFactId
            || material.Kind != ModerationCaseTargetKind.Resource || material.NativeKind != ModerationCaseTargetKind.Resource
            || material.NativeTargetId != native.ResourceId || material.EventId != native.EventId
            || material.NativeTargetVersion != session.EvidenceResourceStamp || material.EventVersion != native.EventStamp
            || material.RecordedAt > source.QualifiedAt
            || material.TenantId != native.TenantId || material.TrustDomainId != native.TrustDomainId
            || material.OccurrenceId != source.OccurrenceId
            || occurrence.Subject is not null || occurrence.Target is null || occurrence.Scope != authority.Tenant
            || occurrence.OccurrenceId != source.OccurrenceId || occurrence.Revision != source.OccurrenceCaseRevision
            || !occurrence.Evidence.Any(row => row.CaseRevision == source.EvidenceCaseRevision
                && row.ToReference(occurrence.Target, occurrence.OccurrenceId) == material.Evidence(authority.Tenant)))
            return null;
        // Reproduce only native, payload-free material coordinates. This does not read a
        // narrative, external destination, object bytes, original grant or qualifier role.
        var submitter = await context.Events.AsNoTracking().Where(row => row.Id == native.EventId)
            .Select(row => row.SubmittedByUserId).SingleAsync(token);
        var target = new NativeTarget(session.EvidenceResourceStamp, native.EventStamp, submitter,
            ParentId: native.StorageObjectId, ParentVersion: native.StorageObjectStamp,
            ProviderId: native.StorageProviderBindingId, StorageProvider: native.StorageProvider, EventId: native.EventId);
        if (target.Fingerprint != material.NativeFactsDigest
            || source.NativeResponsibilityDigest != Digest(Encoding.UTF8.GetBytes(FormattableString.Invariant(
                $"attribution.v2|{(int)source.Basis}|{(int)subject.Kind}|{subject.Id:D}|{target.Fingerprint}"))))
            return null;
        if (!await session.IsCurrentAsync(context, token) || !source.IsCurrentAt(session.CurrentTime)) return null;
        return new(head.Id, head.SourceId, head.Generation, head.Revision,
            Digest(JsonSerializer.SerializeToUtf8Bytes(source)), Digest(JsonSerializer.SerializeToUtf8Bytes(material)),
            source.QualifiedAt, source.ValidUntil, source.WithdrawnAt);
    }

    internal sealed record ResourceSafetyAttribution(
        Guid HeadId, Guid SourceId, long Generation, long HeadRevision, string SourceDigest, string MaterialDigest,
        DateTimeOffset QualifiedAt, DateTimeOffset ValidUntil, DateTimeOffset? WithdrawnAt);
}
