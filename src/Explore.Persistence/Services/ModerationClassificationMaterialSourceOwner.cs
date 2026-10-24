using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Native complete material acquisition, never a supplied dictionary or an Event-root stamp
/// presented as proof of completeness. Every use rereads the finite inventory and authenticated
/// owner/reviewer/taxonomy ancestry on the original serializable transaction.
/// </summary>
public sealed class ModerationClassificationMaterialSourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions, TimeProvider clock)
    : IModerationClassificationMaterialSourceOwner
{
    public Task<ModerationClassificationMaterialInventoryObservation?> AcquireInventoryAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid eventGenerationId,
        Guid taxonomyId, Guid profileId, ModerationNativeMode mode, ModerationClassificationMaterialMandate mandate,
        DateTimeOffset observedAt, CancellationToken token) =>
        ObserveInventoryAsync(evidenceSession, transactionId, eventGenerationId, taxonomyId, profileId, mode,
            mandate, observedAt, false, token);

    internal Task<ModerationClassificationMaterialInventoryObservation?> ObserveInventoryAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid eventGenerationId,
        Guid taxonomyId, Guid profileId, ModerationNativeMode mode, ModerationClassificationMaterialMandate mandate,
        DateTimeOffset observedAt, bool pendingWrite, CancellationToken token) =>
        ObserveInventoryAsync(ModerationSourceReadSession.Authenticated(evidenceSession), transactionId,
            eventGenerationId, taxonomyId, profileId, mode, mandate, observedAt, pendingWrite, token);

    internal async Task<ModerationClassificationMaterialInventoryObservation?> ObserveInventoryAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid eventGenerationId,
        Guid taxonomyId, Guid profileId, ModerationNativeMode mode, ModerationClassificationMaterialMandate mandate,
        DateTimeOffset observedAt, bool pendingWrite, CancellationToken token)
    {
        mandate.Validate();
        if ((!pendingWrite && context.ChangeTracker.HasChanges()) || pendingWrite && HasForeignChanges()
            || session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context) || native.OriginalRecipe is not null) return null;
        if (!await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == native.Authority.Epoch.Id
            && row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
            && row.Revision == native.Authority.Epoch.Revision, token)) return null;
        var taxonomy = await new ModerationClassificationTaxonomySourceOwner(context, definitions, clock)
            .AcquireCurrentAsync(session, transactionId, taxonomyId, profileId, mode, observedAt, token);
        if (taxonomy is null) return null;
        var governanceOwner = new ModerationConstitutionSourceOwner(context, definitions, clock);
        var governance = await governanceOwner.AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        var governanceAdoption = await governanceOwner.ReadAdoptionAsync(session, transactionId, profileId, mode, observedAt, token);
        var ageOwner = new ModerationChildAgePolicySourceOwner(context, definitions, clock);
        var age = await ageOwner.AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        var ageAdoption = await ageOwner.ReadAdoptionAsync(session, transactionId, profileId, mode, observedAt, token);
        if (governance is null || governanceAdoption is null || age is null || ageAdoption is null
            || !taxonomy.Source.MatchesPrerequisites(age.Source, ageAdoption.Adoption, governance.Source,
                governanceAdoption.Adoption, clock.GetUtcNow())) return null;
        var generation = await ReadGenerationAsync(native.Authority, eventGenerationId, observedAt, token);
        if (generation is null) return null;
        var until = new[] { generation.ValidUntil, taxonomy.ValidUntil, governanceAdoption.ValidUntil, ageAdoption.ValidUntil }.Min();
        var qualifiedUntil = await QualifyMandateAsync(native.Authority, generation, governance.Source, mandate, observedAt, until, token);
        if (qualifiedUntil is null) return null;
        // Native raw custom-property deletions participate in this same discovery fence.
        // Do not seal the scope: the identity owner still owes its real final disclosure flush.
        await RelationalNamedLock.AcquireTransactionAsync(context, $"discovery-epoch:{native.Authority.Tenant.Id:N}", token);
        await new EventDiscoveryDisclosureProviderOperations(context).AcquireRevisionAsync(native.Authority.Tenant.Id, token);
        var inventory = await context.ReadClassificationInventoryAsync(native.Authority.Tenant.Id, generation.EventId, token);
        if (inventory is null || inventory.Materials[0].EventStamp != generation.EventStamp
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null || clock.GetUtcNow() >= qualifiedUntil
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == native.Authority.Epoch.Id
                && row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.Revision == native.Authority.Epoch.Revision, token)
            || await QualifyMandateAsync(native.Authority, generation, governance.Source, mandate,
                clock.GetUtcNow(), qualifiedUntil.Value, token) is null)
            return null;
        return new(transactionId, generation, taxonomy, age.Source, ageAdoption.Adoption,
            inventory.Materials, inventory.Components, observedAt, qualifiedUntil.Value);
    }

    public Task<ModerationClassificationMaterialObservation?> AcquireCurrentAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid eventId,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token) =>
        AcquireCurrentAsync(ModerationSourceReadSession.Authenticated(evidenceSession),
            transactionId, eventId, profileId, mode, observedAt, token);

    internal async Task<ModerationClassificationMaterialObservation?> AcquireCurrentAsync(
        ModerationSourceReadSession? session, Guid transactionId, Guid eventId,
        Guid profileId, ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || session is null
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var source = await AcquireSourceAsync(session, transactionId, eventId, profileId, mode, observedAt, token);
        if (source is null) return null;
        var reviewId = await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
            .Where(row => row.SourceId == source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (reviewId is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialReview>(context, reviewId.Value, token);
        var review = await context.Set<ModerationClassificationMaterialReview>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == reviewId, token);
        if (review is null) return null;
        review.ValidatePersistedState();
        if (!review.MatchesCurrent(source, observedAt) || review.WriterGeneration > native.Authority.Epoch.Revision
            || !await new ModerationConstitutionSourceOwner(context, definitions, clock).HasReceiptAsync(native.Authority,
                AcceptanceRequest(review, native.Authority.Tenant), ModerationQualificationSourceKind.ClassificationMaterialReview,
                review.Id, 1, review.ReviewerHumanId, review.WriterGeneration, review.AcceptedAt, token)) return null;
        var live = await ObserveInventoryAsync(session, transactionId, source.EventGenerationId, source.TaxonomyId,
            profileId, mode, source.Mandate, observedAt, false, token);
        if (live is null || !source.MatchesCurrent(live.Generation, live.Taxonomy.Source, live.Taxonomy.Adoption,
                live.Materials, live.Components, clock.GetUtcNow()) || !review.MatchesCurrent(source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null)
            return null;
        return new(transactionId, source, review, live.Generation, live.Taxonomy, live.AgeSource, live.AgeAdoption,
            source.Project(review, live.Generation, live.Taxonomy.Source, live.Taxonomy.Adoption, live.AgeSource, live.AgeAdoption, clock.GetUtcNow()),
            observedAt, review.ValidUntil < live.ValidUntil ? review.ValidUntil : live.ValidUntil);
    }

    internal Task<ModerationClassificationMaterialSource?> AcquireSourceAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid eventId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token, bool pendingWrite = false) =>
        AcquireSourceAsync((ModerationSourceReadSession)session, transactionId, eventId, profileId, mode, observedAt, token, pendingWrite);

    internal async Task<ModerationClassificationMaterialSource?> AcquireSourceAsync(
        ModerationSourceReadSession session, Guid transactionId, Guid eventId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token, bool pendingWrite = false)
    {
        if ((!pendingWrite && context.ChangeTracker.HasChanges()) || pendingWrite && HasForeignChanges()
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context, Slot(native.Authority, eventId), token);
        // A head is selected by logical Event, never by accepted/live policy/profile predicates.
        var id = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id && row.EventId == eventId)
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialSource>(context, id.Value, token);
        var source = await Graph().SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.ProfileId != profileId || source.ModeCode != (int)mode
            || source.PolicyId != native.Policy.Publication.PolicyId || source.PublicationId != native.Policy.Publication.PublicationId
            || source.PolicyRevision != native.Policy.Publication.ProposedRevision || source.WriterGeneration > native.Authority.Epoch.Revision
            || !await new ModerationConstitutionSourceOwner(context, definitions, clock).HasReceiptAsync(native.Authority,
                RecordRequest(source, native.Authority.Tenant), ModerationQualificationSourceKind.ClassificationMaterialSource,
                source.Id, 1, source.OwnerHumanId, source.WriterGeneration, source.RecordedAt, token)) return null;
        var live = await ObserveInventoryAsync(session, transactionId, source.EventGenerationId, source.TaxonomyId,
            profileId, mode, source.Mandate, observedAt, pendingWrite, token);
        return live is not null && source.ValidUntil <= live.ValidUntil
            && source.MatchesCurrent(live.Generation, live.Taxonomy.Source, live.Taxonomy.Adoption,
                live.Materials, live.Components, clock.GetUtcNow()) ? source : null;
    }

    internal IQueryable<ModerationClassificationMaterialSource> Graph() =>
        context.Set<ModerationClassificationMaterialSource>().AsNoTracking().AsSplitQuery()
            .Include(row => row.Materials.OrderBy(item => item.Ordinal)).ThenInclude(row => row.Categories.OrderBy(item => item.Ordinal))
            .Include(row => row.Components.OrderBy(item => item.Ordinal));

    private bool HasForeignChanges() => context.ChangeTracker.Entries().Any(entry =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        && entry.Entity is not (ModerationClassificationMaterialSource or ModerationClassificationMaterialEntry
            or ModerationClassificationMaterialCategory or ModerationClassificationMaterialComponent
            or ModerationClassificationMaterialReview or ModerationQualificationEpoch or ModerationQualificationMutationReceipt));

    private async Task<ModerationEventGeneration?> ReadGenerationAsync(
        IModerationSourceProvenance authority, Guid id, DateTimeOffset now, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventGeneration>(context, id, token);
        var generation = await context.ModerationEventGenerations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (generation is null) return null;
        generation.ValidatePersistedState();
        if (!authority.MaterialGenerations.TryGetValue(id, out var plan)
            || plan.Id != generation.EventId || plan.ActorId != generation.ActorId
            || plan.OrganizerActorId != generation.OrganizerActorId) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.event-generation:{authority.Binding.Id:N}:{generation.EventId:N}", token);
        if (generation.HostBindingId != authority.Binding.Id || generation.TrustDomainId != authority.Binding.TrustDomainId
            || generation.TenantId != authority.Tenant.Id || generation.WriterGeneration > authority.Epoch.Revision
            || generation.ObservedAt > now || generation.ValidUntil <= now
            || await context.ModerationEventGenerations.AsNoTracking().AnyAsync(row => row.HostBindingId == authority.Binding.Id
                && row.EventId == generation.EventId && row.Revision > generation.Revision, token)) return null;
        var governance = new ModerationConstitutionSourceOwner(context, definitions, clock);
        if (!await governance.AdministratorAsync(authority, generation.ObservedByHumanId, authority.Tenant, token)
            || !await governance.HasReceiptAsync(authority, new RecordModerationEventGenerationSource
            {
                OperationId = Guid.Empty,
                Target = authority.Tenant,
                ExpectedQualificationRevision = generation.WriterGeneration - 1,
                SourceId = generation.Id,
                EventId = generation.EventId,
                ExpectedEventRevision = generation.Revision - 1,
                ValidUntil = generation.ValidUntil
            }, ModerationQualificationSourceKind.EventGeneration, generation.Id, generation.Revision,
                generation.ObservedByHumanId, generation.WriterGeneration, generation.ObservedAt, token)) return null;
        var native = await ModerationEventGenerationReader.AcquireAsync(context, generation.Id, generation.EventId,
            generation.HostBindingId, generation.TrustDomainId, generation.TenantId, generation.Revision, generation.ObservedByHumanId,
            generation.WriterGeneration, generation.ObservedAt, generation.ValidUntil, token, generation, ownershipPlan: plan);
        return native is not null && generation.HasSameNativeVersion(native) ? generation : null;
    }

    private async Task<DateTimeOffset?> QualifyMandateAsync(IModerationSourceProvenance authority, ModerationEventGeneration generation,
        ModerationConstitutionSource governance, ModerationClassificationMaterialMandate mandate,
        DateTimeOffset now, DateTimeOffset until, CancellationToken token)
    {
        var humans = new[] { mandate.OwnerHumanId, mandate.OwnerControllerHumanId, mandate.ReviewerHumanId, mandate.ReviewerControllerHumanId };
        if (humans.Any(id => !authority.FencedHumans.Contains(id))
            || !governance.Reviewers.Any(row => row.ReviewerHumanId == mandate.OwnerHumanId && row.ControllerHumanId == mandate.OwnerControllerHumanId)
            || !governance.Reviewers.Any(row => row.ReviewerHumanId == mandate.ReviewerHumanId && row.ControllerHumanId == mandate.ReviewerControllerHumanId)
            || await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Distinct().Count()
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token))
            return null;
        if (authority.MaterialAuthority is not { } planned) return null;
        var ownerPlan = new EventAuthorityProviderOperations.AuthorityPlan(
            planned.Parents.Where(row => row.Id == generation.EventId).ToList(),
            planned.Assignments.Where(row => row.EventId == generation.EventId && row.UserId == mandate.OwnerHumanId).ToList());
        var snapshots = await new EventAuthoritySnapshotService(context).GetCommitBoundForUsersAndEventsAsync(
            authority.Tenant.Id, [mandate.OwnerHumanId], [generation.EventId], now.UtcDateTime, token, ownerPlan);
        var rights = snapshots[mandate.OwnerHumanId];
        if (!rights.Events.TryGetValue(generation.EventId, out var ownerRights) || !ownerRights.IsOwner && !ownerRights.IsManager) return null;
        var assignment = await context.EventRoleAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == mandate.OwnerAssignmentId
            && row.EventId == generation.EventId && row.TenantId == authority.Tenant.Id && row.UserId == mandate.OwnerHumanId, token);
        if (assignment is null || assignment.Version != mandate.OwnerAssignmentRevision || assignment.RevokedAtUtc is not null
            || !assignment.IsEffectiveAt(now.UtcDateTime) || assignment.RoleId is not ((int)RoleEnum.EventOwner) and not ((int)RoleEnum.EventManager)
            || !await context.Roles.AsNoTracking().AnyAsync(row => row.Id == assignment.RoleId && row.RoleScopeId == (int)RoleScopeEnum.Event
                && row.MasterCode == (assignment.RoleId == (int)RoleEnum.EventOwner ? "event.owner" : "event.manager"), token)
            || !await context.TenantUsers.AsNoTracking().AnyAsync(row => row.TenantId == authority.Tenant.Id && row.UserId == mandate.OwnerHumanId
                && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted && !row.User.IsDeleted, token))
            return null;
        var interested = await InterestedHumansAsync(context, generation.EventId, authority.Tenant.Id, now, token);
        // A manager's canonical controller is interested even if the controller has no direct
        // role assignment. Roster identity, not distinct supplied UUIDs, determines this mapping.
        foreach (var row in governance.Reviewers.Where(row => interested.Contains(row.ReviewerHumanId)))
            interested.Add(row.ControllerHumanId);
        if (interested.Contains(mandate.ReviewerHumanId) || interested.Contains(mandate.ReviewerControllerHumanId)) return null;
        if (assignment.ExpiresAtUtc is { } expiry && expiry < until.UtcDateTime)
            until = new(DateTime.SpecifyKind(expiry, DateTimeKind.Utc));
        return now < until ? until : null;
    }

    internal static async Task<HashSet<Guid>> InterestedHumansAsync(
        ExploreDbContext context, Guid eventId, Guid tenantId, DateTimeOffset? now, CancellationToken token)
    {
        var events = await context.Events.AsNoTracking().Where(row => row.Id == eventId && row.TenantId == tenantId)
            .Select(row => new { row.ActorId, row.OrganizerActorId, row.SubmittedByUserId, row.CreatedBy }).ToArrayAsync(token);
        var actorIds = events.Select(row => row.ActorId).Concat(events.Where(row => row.OrganizerActorId != null)
            .Select(row => row.OrganizerActorId!.Value)).Distinct().ToArray();
        var actors = await context.Actors.AsNoTracking().Where(row => actorIds.Contains(row.Id)).ToArrayAsync(token);
        var interested = events.SelectMany(row => new[] { row.SubmittedByUserId, row.CreatedBy })
            .Concat(actors.Select(row => row.UserId)).OfType<Guid>().ToHashSet();
        interested.UnionWith(await context.EventRoleAssignments.AsNoTracking().Where(row => row.EventId == eventId && row.TenantId == tenantId
            && (now == null || row.Status == EventRoleAssignmentStatus.Active && row.RevokedAtUtc == null && row.StartsAtUtc <= now.Value.UtcDateTime
                && (row.ExpiresAtUtc == null || row.ExpiresAtUtc > now.Value.UtcDateTime))
            && (row.RoleId == (int)RoleEnum.EventOwner || row.RoleId == (int)RoleEnum.EventManager)).Select(row => row.UserId).ToArrayAsync(token));
        interested.UnionWith((await context.EventSessions.AsNoTracking().Where(row => row.EventId == eventId && row.TenantId == tenantId)
            .Select(row => row.CreatedBy).ToArrayAsync(token)).OfType<Guid>());
        interested.UnionWith((await context.Set<EventResource>().AsNoTracking().Where(row => row.EventId == eventId && row.TenantId == tenantId)
            .Select(row => row.CreatedBy).ToArrayAsync(token)).OfType<Guid>());
        var organizations = actors.Select(row => row.OrganizationId).OfType<Guid>().ToArray();
        var groups = actors.Select(row => row.GroupId).OfType<Guid>().ToArray();
        interested.UnionWith(await context.Set<OrganizationMember>().AsNoTracking().Where(row => row.TenantId == tenantId
            && organizations.Contains(row.OrganizationTenant.OrganizationId) && !row.IsDeleted
            && row.RoleId == (int)RoleEnum.OrgAdmin).Select(row => row.UserId).ToArrayAsync(token));
        interested.UnionWith(await context.Set<GroupMember>().AsNoTracking().Where(row => row.TenantId == tenantId
            && groups.Contains(row.GroupTenant.GroupId) && !row.IsDeleted
            && row.RoleId == (int)RoleEnum.GroupAdmin).Select(row => row.UserId).ToArrayAsync(token));
        return interested;
    }

    internal static RecordModerationClassificationMaterialSource RecordRequest(ModerationClassificationMaterialSource source, AuthorityScope target) => new()
    {
        OperationId = Guid.Empty,
        Target = target,
        ExpectedQualificationRevision = source.WriterGeneration - 1,
        SourceId = source.Id,
        ExpectedMaterialGeneration = source.PreviousGeneration,
        EventGenerationId = source.EventGenerationId,
        PolicyId = source.PolicyId,
        ExpectedPolicyRevision = source.PolicyRevision,
        PublicationId = source.PublicationId,
        ProfileId = source.ProfileId,
        Mode = (ModerationNativeMode)source.ModeCode,
        TaxonomyId = source.TaxonomyId,
        ExpectedTaxonomySourceId = source.TaxonomySourceId,
        ExpectedTaxonomyAdoptionId = source.TaxonomyAdoptionId,
        Mandate = source.Mandate,
        Materials = source.Materials.Select(row => row.Selection).ToImmutableArray(),
        Components = source.Components.Select(row => row.Version).ToImmutableArray(),
        ValidUntil = source.ValidUntil
    };

    internal static AcceptModerationClassificationMaterialSource AcceptanceRequest(ModerationClassificationMaterialReview review, AuthorityScope target) => new()
    {
        OperationId = Guid.Empty,
        Target = target,
        ExpectedQualificationRevision = review.WriterGeneration - 1,
        SourceId = review.SourceId,
        ExpectedSourceRevision = review.SourceRevision,
        ExpectedSourceWriterGeneration = review.SourceWriterGeneration,
        ReviewId = review.Id,
        ValidUntil = review.ValidUntil
    };

    internal static string Slot(IModerationSourceProvenance authority, Guid eventId) =>
        $"moderation.classification-material:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{eventId:N}";
}
