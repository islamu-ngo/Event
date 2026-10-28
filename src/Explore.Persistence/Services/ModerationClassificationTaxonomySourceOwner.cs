using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Authenticates native taxonomy heads on the original acquisition. Governance supplies the
/// coordinator's ownership and independently qualified canonical reviewer roster; child-age
/// adoption supplies normative floors, never reviewer competence or individual age assurance.
/// </summary>
public sealed class ModerationClassificationTaxonomySourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions, TimeProvider clock)
    : IModerationClassificationTaxonomySourceOwner
{
    public Task<ModerationClassificationTaxonomyObservation?> AcquireCurrentAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid taxonomyId,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        AcquireCurrentAsync(ModerationSourceReadSession.Authenticated(evidenceSession),
            transactionId, taxonomyId, profileId, mode, observedAt, token);

    internal async Task<ModerationClassificationTaxonomyObservation?> AcquireCurrentAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid taxonomyId,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var source = await AcquireSourceAsync(session, transactionId, taxonomyId, profileId, mode, observedAt, token);
        if (source is null) return null;
        var id = await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
            .Where(row => row.SourceId == source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationTaxonomyAdoption>(context, id.Value, token);
        var adoption = await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        var governanceOwner = new ModerationConstitutionSourceOwner(context, definitions, clock);
        if (!adoption.MatchesCurrent(source, observedAt) || adoption.WriterGeneration > native.Authority.Epoch.Revision
            || !await governanceOwner.HasReceiptAsync(native.Authority, AdoptionRequest(adoption, native.Policy.Publication.Jurisdiction),
                ModerationQualificationSourceKind.ClassificationTaxonomyAdoption, adoption.Id, 1,
                adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
        var current = await AcquireSourceAsync(session, transactionId, taxonomyId, profileId, mode, observedAt, token);
        var prerequisites = await ObservePrerequisitesAsync(session, transactionId, profileId, mode, source.Mandate, observedAt, token);
        if (current?.Id != source.Id || prerequisites is null
            || !source.MatchesPrerequisites(prerequisites.Age, prerequisites.AgeAdoption,
                prerequisites.Governance, prerequisites.GovernanceAdoption, clock.GetUtcNow())
            || !adoption.MatchesCurrent(current, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return new(transactionId, source, adoption, source.Project(prerequisites.Age, prerequisites.AgeAdoption),
            observedAt, adoption.ValidUntil);
    }

    internal Task<ModerationClassificationTaxonomySource?> AcquireSourceAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid taxonomyId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        AcquireSourceAsync((ModerationSourceReadSession)session, transactionId, taxonomyId, profileId, mode, observedAt, token);

    internal async Task<ModerationClassificationTaxonomySource?> AcquireSourceAsync(
        ModerationSourceReadSession session, Guid transactionId, Guid taxonomyId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context, Slot(native.Authority, taxonomyId), token);
        // Select before testing approval/currentness. A newer unapproved, expired or withdrawn
        // proposal closes this logical taxonomy; filtering it out would resurrect older meaning.
        var id = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.TaxonomyId == taxonomyId)
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationTaxonomySource>(context, id.Value, token);
        var source = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .Include(row => row.Categories.OrderBy(category => category.Ordinal)).SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        var publication = native.Policy.Publication;
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.PolicyId != publication.PolicyId
            || source.PolicyRevision != publication.ProposedRevision || source.PublicationId != publication.PublicationId
            || source.ProfileId != profileId || source.ModeCode != (int)mode
            || source.RecordedAt > observedAt || source.ValidUntil <= observedAt
            || source.WriterGeneration > native.Authority.Epoch.Revision
            || source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || !await new ModerationConstitutionSourceOwner(context, definitions, clock).HasReceiptAsync(
                native.Authority, RecordRequest(source, publication.Jurisdiction),
                ModerationQualificationSourceKind.ClassificationTaxonomySource, source.Id, 1,
                source.OwnerHumanId, source.WriterGeneration, source.RecordedAt, token)) return null;
        var prerequisites = await ObservePrerequisitesAsync(session, transactionId, profileId, mode, source.Mandate, observedAt, token);
        if (prerequisites is null || !source.MatchesPrerequisites(prerequisites.Age, prerequisites.AgeAdoption,
                prerequisites.Governance, prerequisites.GovernanceAdoption, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return source;
    }

    internal Task<TaxonomyPrerequisites?> ObservePrerequisitesAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationClassificationTaxonomyMandate mandate, DateTimeOffset observedAt, CancellationToken token) =>
        ObservePrerequisitesAsync((ModerationSourceReadSession)session, transactionId, profileId, mode, mandate, observedAt, token);

    internal async Task<TaxonomyPrerequisites?> ObservePrerequisitesAsync(
        ModerationSourceReadSession session, Guid transactionId, Guid profileId, ModerationNativeMode mode,
        ModerationClassificationTaxonomyMandate mandate, DateTimeOffset observedAt, CancellationToken token)
    {
        mandate.Validate();
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context) || !Enum.IsDefined(mode)) return null;
        var definition = await definitions.ReadDefinitionAsync(native, observedAt, token);
        var profile = definition?.Policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId);
        if (profile is null || !definition!.Policy.Proposed.Modes.Contains(mode)
            || !profile.Definition.Strategies.Any(strategy => strategy is ModerationAdditiveStrategyKind.ClassificationIntegrity
                or ModerationAdditiveStrategyKind.AgeAudienceEligibility)) return null;
        var governanceOwner = new ModerationConstitutionSourceOwner(context, definitions, clock);
        var governance = await governanceOwner.AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        var governanceAdoption = await governanceOwner.ReadAdoptionAsync(session, transactionId, profileId, mode, observedAt, token);
        if (governance is null || governanceAdoption is null
            || !governanceAdoption.Adoption.MatchesCurrent(governance.Source, clock.GetUtcNow())
            || governance.Source.OriginalEffectId is not null || governance.Source.CoordinatorHumanId != mandate.OwnerHumanId
            || !governance.Source.Reviewers.Any(row => row.ReviewerHumanId == mandate.OwnerHumanId
                && row.ControllerHumanId == mandate.OwnerControllerHumanId)
            || !governance.Source.Reviewers.Any(row => row.ReviewerHumanId == mandate.ReviewerHumanId
                && row.ControllerHumanId == mandate.ReviewerControllerHumanId)) return null;
        // Roster qualification/independence and self-recorded availability are independently
        // receipted and reread by governance, not inferred from our supplied mandate statements.
        var ageOwner = new ModerationChildAgePolicySourceOwner(context, definitions, clock);
        var age = await ageOwner.AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        var ageAdoption = await ageOwner.ReadAdoptionAsync(session, transactionId, profileId, mode, observedAt, token);
        if (age is null || ageAdoption is null || !ageAdoption.Adoption.MatchesCurrent(age.Source, clock.GetUtcNow())
            || age.Source.DefinitionId != governance.Source.DefinitionId
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        var until = ageAdoption.ValidUntil < governanceAdoption.ValidUntil ? ageAdoption.ValidUntil : governanceAdoption.ValidUntil;
        return clock.GetUtcNow() < until
            ? new(age.Source, ageAdoption.Adoption, governance.Source, governanceAdoption.Adoption, until) : null;
    }

    internal static RecordModerationClassificationTaxonomySource RecordRequest(
        ModerationClassificationTaxonomySource source, AuthorityScope target) => new()
        {
            OperationId = Guid.Empty,
            Target = target,
            ExpectedQualificationRevision = source.WriterGeneration - 1,
            SourceId = source.Id,
            TaxonomyId = source.TaxonomyId,
            ExpectedTaxonomyGeneration = source.PreviousGeneration,
            PolicyId = source.PolicyId,
            ExpectedPolicyRevision = source.PolicyRevision,
            PublicationId = source.PublicationId,
            ProfileId = source.ProfileId,
            Mode = (ModerationNativeMode)source.ModeCode,
            Mandate = source.Mandate,
            Categories = source.Categories.Select(row => row.Terms).ToImmutableArray(),
            ValidUntil = source.ValidUntil
        };

    internal static AdoptModerationClassificationTaxonomySource AdoptionRequest(
        ModerationClassificationTaxonomyAdoption adoption, AuthorityScope target) => new()
        {
            OperationId = Guid.Empty,
            Target = target,
            ExpectedQualificationRevision = adoption.WriterGeneration - 1,
            SourceId = adoption.SourceId,
            ExpectedSourceRevision = adoption.SourceRevision,
            ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration,
            AdoptionId = adoption.Id,
            ValidUntil = adoption.ValidUntil
        };

    internal static string Slot(IModerationSourceProvenance authority, Guid taxonomyId) =>
        $"moderation.classification-taxonomy:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{taxonomyId:N}";

    internal sealed record TaxonomyPrerequisites(ModerationChildAgePolicySource Age, ModerationChildAgePolicyAdoption AgeAdoption,
        ModerationConstitutionSource Governance, ModerationConstitutionAdoption GovernanceAdoption, DateTimeOffset ValidUntil);
}
