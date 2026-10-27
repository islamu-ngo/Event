using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Moderation;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Extensions;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPublicEventCalendarRepository
{
    private IQueryable<ModerationClassificationControlSelection> ControlGraph() =>
        context.Set<ModerationClassificationControlSelection>().AsNoTracking()
            .Include(row => row.Bindings.OrderBy(binding => binding.Ordinal));

    private async Task<QualifiedCalendar?> ReadQualifiedAsync(
        ModerationPublicEventCalendarRequest request, ModerationSourceReadSession session,
        Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
    {
        var native = await session.AcquireNativeAsync(transactionId, observedAt, token);
        if (native is null || !native.IsPublicCalendar || native.OriginalRecipe is not null
            || native.Authority is not PublicProvenance provenance
            || !ReferenceEquals(native.Context, context) || provenance.Tenant.Id != request.TenantId) return null;
        var definitions = Definitions();
        var definition = await definitions.ReadDefinitionAsync(native, observedAt, token);
        if (definition is null) return null;
        var policy = definition.Policy;
        var publication = await definitions.AcquireSelectedAsync(transactionId, provenance.Binding.Id,
            provenance.Tenant, policy.Publication.PolicyId, true, token);
        var governance = new ModerationConstitutionSourceOwner(context, definitions, clock);
        if (publication is null || publication.Id != policy.Publication.PublicationId
            || publication.TenantId != request.TenantId || publication.AuthoritySourceId != provenance.Epoch.Id
            || publication.AuthorityRevision < 1 || publication.QualificationRevision != publication.AuthorityRevision + 1
            || publication.QualificationRevision > provenance.Epoch.Revision
            || publication.PublishedAt > observedAt || publication.PublishedAt.Offset != TimeSpan.Zero
            || publication.RequestDigest is not { Length: 64 } || !publication.RequestDigest.All(char.IsAsciiHexDigit)
            || publication.EvidenceValidUntil is not { } publicationUntil
            || publicationUntil <= observedAt || publicationUntil == DateTimeOffset.MaxValue
            || publicationUntil.Offset != TimeSpan.Zero || publication.Evidence.Count == 0
            || publication.Evidence.Any(row => row.PublicationId != publication.Id || row.ObservedAt != publication.PublishedAt
                || row.ValidUntil < publicationUntil || row.ValidUntil.Offset != TimeSpan.Zero
                || row.ValidUntil == DateTimeOffset.MaxValue)
            || !await governance.AdministratorAsync(provenance, publication.PrincipalId, provenance.Tenant, token))
            return null;
        // A preparation is birth provenance only. The active selected publication above
        // is mandatory; this path never authorizes from the candidate definition.
        var prepared = await definitions.CandidateGraph().SingleOrDefaultAsync(row => row.Id == publication.Id, token);
        if (prepared is null) return null;
        prepared.ValidatePersistedState();
        var preparedPolicy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared, inventory);
        if (preparedPolicy is null || prepared.DefinitionId != definition.DefinitionId
            || prepared.AuthoritySourceId != provenance.Epoch.Id || prepared.WriterGeneration > publication.AuthorityRevision
            || prepared.ExpectedRevision + 1 != publication.Revision || prepared.PreviousPublicationId != publication.PreviousPublicationId
            || prepared.PreparedAt > publication.PublishedAt || prepared.ValidUntil <= publication.PublishedAt
            || prepared.ProposedStateDigest != publication.DeclaredStateDigest
            || !await governance.AdministratorAsync(provenance, prepared.PreparedByHumanId, provenance.Tenant, token))
            return null;
        var preparation = new ModerationPolicyPublicationRequest(prepared.OperationId, prepared.HostBindingId,
            prepared.AuthoritySourceId, prepared.AuthorityRevision, ModerationPolicyPublicationProjection.WriterGeneration,
            prepared.PreviousPublicationId, ModerationPolicyPublicationProjection.CanonicalProposed(preparedPolicy.Proposed));
        if (prepared.RequestDigest != ModerationPolicyPublicationProjection.Digest(
                new { Request = preparation, ValidUntil = prepared.ValidUntil })
            || !CompletePublicationReceipt(publication, policy)) return null;
        var discovered = await ControlGraph().Where(row => row.HostBindingId == provenance.Binding.Id
                && row.TenantId == request.TenantId && row.EventId == request.EventId)
            .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
        if (discovered is null || discovered.PublicationId != publication.Id) return null;
        var profile = policy.Profiles.SingleOrDefault(row => row.Definition.Id == discovered.ProfileId);
        if (policy.Proposed.CatalogueRevision != 2 || profile?.Reference.Id != ModerationReferenceProfileId.CFG29
            || !policy.Proposed.SupportedProfiles.Contains(profile.Definition.Rule)
            || profile.Definition.Timing.Kind != ModerationReferenceTimingKind.CurrentControlUntilQualifiedResolution
            || profile.Definition.Timing.Duration != TimeSpan.Zero
            || !profile.Definition.ScopeKinds.Contains(ModerationScopeKind.Event)
            || !profile.Definition.SubjectKinds.Contains(ModerationSubjectKind.Content)) return null;
        var material = await new ModerationClassificationMaterialSourceOwner(context, definitions, clock)
            .AcquireCurrentAsync(session, transactionId, request.EventId, discovered.ProfileId,
                (ModerationNativeMode)discovered.ModeCode, observedAt, token);
        if (material is null) return null;
        await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.classification-control:{provenance.Binding.Id:N}:{request.TenantId:N}:{request.EventId:N}", token);
        var currentId = await context.Set<ModerationClassificationControlSelection>().AsNoTracking()
            .Where(row => row.HostBindingId == provenance.Binding.Id && row.TenantId == request.TenantId && row.EventId == request.EventId)
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (currentId != discovered.Id) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationControlSelection>(context, discovered.Id, token);
        var selected = await ControlGraph().SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (selected is null) return null;
        selected.ValidatePersistedState();
        if (!selected.MatchesCurrent(material.Source, material.Acceptance, material.Generation, clock.GetUtcNow())
            || selected.ValidUntil > material.ValidUntil || selected.WriterGeneration > provenance.Epoch.Revision
            || selected.PublicationId != publication.Id
            || !await governance.AdministratorAsync(provenance, selected.AuthoredByHumanId, provenance.Tenant, token))
            return null;
        var target = AuthorityScope.Event(provenance.Tenant, request.EventId);
        var contribution = ModerationClassificationControlSelectionProjection.Restore(selected, policy);
        if (contribution.Scope != target || contribution.SubjectKind != ModerationSubjectKind.Content
            || contribution.SubjectId != request.EventId || contribution.StartsAt != material.Acceptance.AcceptedAt
            || contribution.EventWindowEnd is not null
            || contribution.Effects.Any(row => !ExactTarget(row.Target))
            || contribution.Consequences.Any(row => !ExactTarget(row.Target))
            || ModerationNoticeSnapshotFactory.SelectionDigest(contribution) != selected.SelectionDigest) return null;
        var composition = ModerationPolicyProposedStateCompiler.ComposeRetained(policy, [contribution], observedAt);
        if (composition.Outcome != ModerationPolicyCompositionOutcome.Preview
            || composition.Effects.IsEmpty || composition.Consequences.IsEmpty
            || composition.Effects.Any(row => row.Interval.EndsAt is not null || row.Interval.ReviewAt is not null
                || row.Interval.EventWindowEnd is not null)
            || composition.Consequences.Any(row => row.Interval.EndsAt is not null || row.Interval.ReviewAt is not null
                || row.Interval.EventWindowEnd is not null)) return null;
        if (!await governance.HasReceiptAsync(provenance, new RecordModerationClassificationControlSelection
        {
            OperationId = Guid.Empty,
            Target = target,
            ExpectedQualificationRevision = selected.WriterGeneration - 1,
            SelectionId = selected.Id,
            MaterialSourceId = selected.SourceId,
            ExpectedMaterialSourceRevision = selected.SourceRevision,
            ExpectedMaterialWriterGeneration = selected.SourceWriterGeneration,
            MaterialReviewId = selected.ReviewId,
            ExpectedMaterialReviewRevision = selected.ReviewRevision,
            ExpectedReviewWriterGeneration = selected.ReviewWriterGeneration,
            EventGenerationId = selected.GenerationId,
            ExpectedEventGenerationRevision = selected.GenerationRevision,
            ExpectedEventWriterGeneration = selected.GenerationWriterGeneration,
            Contribution = contribution,
            ValidUntil = selected.ValidUntil
        }, ModerationQualificationSourceKind.ClassificationControlSelection, selected.Id, 1,
            selected.AuthoredByHumanId, selected.WriterGeneration, selected.RecordedAt, token)) return null;

        var adoptions = new List<ModerationClassificationEventTargetAdoption>();
        var until = new[] { material.ValidUntil, selected.ValidUntil, publicationUntil }.Min();
        foreach (var binding in selected.Bindings.OrderBy(row => row.Ordinal))
        {
            await RelationalNamedLock.AcquireTransactionAsync(context,
                $"moderation.classification-event-target:{selected.Id:N}:{binding.Ordinal}", token);
            var id = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
                .Where(row => row.SelectionId == selected.Id && row.BindingOrdinal == binding.Ordinal)
                .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
            if (id is null) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationEventTargetAdoption>(context, id.Value, token);
            var adoption = await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == id, token);
            if (adoption is null) return null;
            adoption.ValidatePersistedState();
            if (!adoption.MatchesCurrent(selected, material.Source, material.Acceptance, material.Generation, clock.GetUtcNow())
                || adoption.WriterGeneration > provenance.Epoch.Revision
                || !await governance.AdministratorAsync(provenance, adoption.AdoptedByHumanId, provenance.Tenant, token)
                || !await governance.HasReceiptAsync(provenance, new AdoptModerationClassificationEventTarget
                {
                    OperationId = Guid.Empty,
                    Target = target,
                    ExpectedQualificationRevision = adoption.WriterGeneration - 1,
                    AdoptionId = adoption.Id,
                    SelectionId = adoption.SelectionId,
                    ExpectedSelectionRevision = adoption.SelectionRevision,
                    BindingOrdinal = adoption.BindingOrdinal,
                    EventGenerationId = adoption.GenerationId,
                    ExpectedEventGenerationRevision = adoption.GenerationRevision,
                    ValidUntil = adoption.ValidUntil
                }, ModerationQualificationSourceKind.ClassificationEventTargetAdoption, adoption.Id, 1,
                    adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
            adoptions.Add(adoption);
            if (adoption.ValidUntil < until) until = adoption.ValidUntil;
        }
        // No inferred age, safe-summary exception or per-session segregation. Every actual
        // child, destination, form and public ActorPii carrier was authenticated by the owner.
        var audience = ModerationAudiencePolicyCompiler.Compile(material.Generation,
            material.Reviews.ToDictionary(row => row.Material,
                row => (IReadOnlyCollection<EventContentClassificationVersion>)row.Categories),
            material.Taxonomy.Categories, material.Reviews, material.AgeSource, material.AgeAdoption, clock.GetUtcNow());
        if (audience.Status != ModerationAudiencePolicyCompiler.CompilationStatus.CurrentMetadata
            || audience.WholeEventMinimumAge != 0 || audience.ValidUntil is not { } audienceUntil) return null;
        if (audienceUntil < until) until = audienceUntil;
        var entity = await context.Events.AsNoTracking().WherePubliclyEligible(context)
            .SingleOrDefaultAsync(row => row.Id == request.EventId && row.TenantId == request.TenantId, token);
        if (entity is null || entity.ConcurrencyStamp != material.Generation.EventStamp) return null;
        var sessions = await context.EventSessions.AsNoTracking().WherePubliclyEligible(context)
            .Where(row => row.EventId == entity.Id && row.TenantId == request.TenantId).ToArrayAsync(token);
        // Order in memory: SQLite does not support ordering DateTimeOffset in SQL.
        var primary = sessions.OrderBy(row => row.StartTime).ThenBy(row => row.SortOrder).ThenBy(row => row.Id).FirstOrDefault();
        if (primary?.StartTime is not { } start || primary.EndTime is not { } end
            || start == DateTimeOffset.MinValue || end == DateTimeOffset.MaxValue || end <= start) return null;

        var subject = SubjectReference.Create(ModerationSubjectKind.Content, entity.Id, provenance.Tenant);
        var sanctions = await context.ModerationSanctions.AsNoTracking()
            .Where(row => row.SubjectKind == ModerationSubjectKind.Content && row.SubjectId == entity.Id).ToArrayAsync(token);
        var protective = await context.ModerationProtectiveMeasures.AsNoTracking()
            .Where(row => row.SubjectKind == ModerationSubjectKind.Content && row.SubjectId == entity.Id).ToArrayAsync(token);
        foreach (var sanction in sanctions) sanction.ValidatePersistedState();
        foreach (var measure in protective) measure.ValidatePersistedState();
        if (EffectiveRestrictions.Evaluate(subject, target, ModerationCapability.ReadResource, clock.GetUtcNow(),
            sanctions, protective, ModerationAuthorityState.Known).Outcome != ModerationRestrictionOutcome.Unrestricted)
            return null;
        foreach (var boundary in sanctions.Where(row => row.Subject == subject && row.Scope.Contains(target)
                && row.Capabilities.Contains(ModerationCapability.ReadResource) && row.IsEffectiveAt(row.StartsAt))
            .SelectMany(row => new DateTimeOffset?[] { row.StartsAt, row.EndsAt })
            .Concat(protective.Where(row => row.Subject == subject && row.Scope.Contains(target)
                    && row.Capabilities.Contains(ModerationCapability.ReadResource) && row.IsEffectiveAt(row.StartsAt))
                .SelectMany(row => new DateTimeOffset?[] { row.StartsAt, row.EndsAt })))
            if (boundary is { } at && at > observedAt && at < until) until = at;
        if (until.Offset != TimeSpan.Zero || until == DateTimeOffset.MaxValue || clock.GetUtcNow() >= until
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        var fingerprint = ModerationPolicyPublicationProjection.Digest(new
        {
            Host = provenance.Binding.Id,
            Epoch = provenance.Epoch.Id,
            provenance.Epoch.Revision,
            publication.Id,
            publication.RequestDigest,
            publication.DeclaredStateDigest,
            publication.EvidenceValidUntil,
            Material = material.Source.Id,
            MaterialWriter = material.Source.WriterGeneration,
            Review = material.Acceptance.Id,
            ReviewWriter = material.Acceptance.WriterGeneration,
            Generation = material.Generation.Id,
            GenerationRevision = material.Generation.Revision,
            Taxonomy = material.Taxonomy.Source.Id,
            TaxonomyAdoption = material.Taxonomy.Adoption.Id,
            Age = material.AgeSource.Id,
            AgeAdoption = material.AgeAdoption.Id,
            Selection = selected.Id,
            selected.SelectionDigest,
            Adoptions = adoptions.Select(row => new { row.Id, row.Revision, row.WriterGeneration, row.BindingOrdinal, row.ValidUntil }),
            Event = new { entity.Id, entity.ConcurrencyStamp, entity.Title, entity.Content, entity.Description, entity.Slug },
            Primary = new
            {
                primary.Id,
                primary.ConcurrencyStamp,
                primary.StartTime,
                primary.EndTime,
                primary.SortOrder,
                primary.EventLocationId,
                primary.RoomId
            },
            Sanctions = sanctions.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
            Protective = protective.OrderBy(row => row.Id).Select(row => new { row.Id, row.Revision }),
            Until = until
        });
        return new(entity, primary, until, fingerprint);

        bool ExactTarget(ModerationPolicyTarget value) => value.Scope == target
            && value.SubjectKind == ModerationSubjectKind.Content && value.SubjectId == request.EventId
            && value.TargetId == request.EventId && value.TargetRevision == material.Generation.Revision && value.WindowEnd is null;
    }

    private sealed record QualifiedCalendar(Event Event, EventSession Session, DateTimeOffset ValidUntil, string Fingerprint);

    private static bool CompletePublicationReceipt(ModerationPolicyPublication publication, ModerationPolicyCompiledDraft policy)
    {
        var proposed = ModerationPolicyPublicationProjection.CanonicalProposed(policy.Proposed);
        var obligations = new List<ModerationPolicyPublicationObligation>();
        foreach (var mode in proposed.Modes)
            obligations.Add(new(policy.Publication, mode, policy.Bounds, proposed));
        foreach (var prerequisite in policy.Prerequisites)
        {
            foreach (var gate in prerequisite.ActivationGates)
                obligations.Add(new(policy.Publication, prerequisite.Mode, policy.Bounds, proposed,
                    prerequisite with { ActivationGates = [gate], NativePrerequisites = [] }));
            foreach (var native in prerequisite.NativePrerequisites)
                obligations.Add(new(policy.Publication, prerequisite.Mode, policy.Bounds, proposed,
                    prerequisite with { ActivationGates = [], NativePrerequisites = [native] }));
        }
        obligations.AddRange(policy.Effects.Select(effect =>
            new ModerationPolicyPublicationObligation(policy.Publication, effect.Mode, policy.Bounds, proposed, Effect: effect)));
        obligations.AddRange(policy.Consequences.Select(consequence =>
            new ModerationPolicyPublicationObligation(policy.Publication, consequence.Mode, policy.Bounds, proposed, Consequence: consequence)));
        var required = obligations.Select(ModerationPolicyPublicationProjection.Digest).ToHashSet(StringComparer.Ordinal);
        var consumerKeys = policy.Consumers.SelectMany(requirement => requirement.Consumers.Select(consumer =>
            ModerationPolicyPublicationProjection.Digest(new
            {
                requirement.Binding,
                ConsumerId = consumer.Id,
                proposed.InventoryFingerprint
            }))).ToImmutableHashSet(StringComparer.Ordinal);
        if (publication.Evidence.Count != required.Count + consumerKeys.Count
            || publication.Evidence.Select(row => row.RequirementDigest).Distinct(StringComparer.Ordinal).Count() != publication.Evidence.Count)
            return false;
        return publication.Evidence.All(row => row.SourceId != Guid.Empty && row.SourceRevision > 0
            && !string.IsNullOrWhiteSpace(row.WriterGeneration)
            && (required.Contains(row.RequirementDigest)
                ? row.ApprovalId is { } approval && approval != Guid.Empty && approval != row.SourceId && row.ApprovalRevision > 0
                : consumerKeys.Contains(row.RequirementDigest) && row.ApprovalId is null && row.ApprovalRevision is null));
    }
}
