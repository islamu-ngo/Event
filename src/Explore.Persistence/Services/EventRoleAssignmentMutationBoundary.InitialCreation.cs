using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed partial class EventRoleAssignmentMutationBoundary
{
    public async Task<BaseCommandResponse<Guid>> ExecuteCreationAsync(EventInitialCreation creation,
        Func<Guid, CancellationToken, Task<bool>> qualifyPublisher,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(creation);
        ArgumentNullException.ThrowIfNull(qualifyPublisher);
        ArgumentNullException.ThrowIfNull(operation);
        RequireCreator();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Initial Event creation requires an isolated native attempt.");
        if (new[] { creation.EventId, creation.OwnerAssignmentId, creation.CreatorUserId, creation.PublisherActorId }
                .Any(id => id == Guid.Empty) || creation.EventId == creation.OwnerAssignmentId
            || creation.CreatedAtUtc.Kind != DateTimeKind.Utc
            || creation.RequestedStatus is not (EventStatusEnum.Draft or EventStatusEnum.Published)) DenyInitialCreation();
        creation = creation with { ImageIds = creation.ImageIds.Distinct().Order().ToArray() };
        InitialCreationAttempt? attempted = null;
        EventInitialCreationWriteReceipt? writes = null;
        var settings = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        string publisherSetting = creation.OrganizationId.HasValue ? GovernanceSettingKeys.Events.OrganizationSubmissionEnabled
            : creation.GroupId.HasValue ? GovernanceSettingKeys.Events.GroupSubmissionEnabled
            : GovernanceSettingKeys.Events.UserSubmissionEnabled;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => settings.ExecuteOrderedGroupsAsync(
                [VisitorAccessCapabilityResolver.AuthoritySettingKeys, [publisherSetting, GovernanceSettingKeys.Events.RequireApproval]],
                token => context.Database.CreateExecutionStrategy().ExecuteAsync<object?, BaseCommandResponse<Guid>>(null,
                    async (_, _, inner) => await ExecuteAttemptAsync(inner),
                    async (_, _, inner) => new ExecutionResult<BaseCommandResponse<Guid>>(
                        await VerifyAsync(inner), attempted is null ? default! : attempted.Result), token),
                cancellationToken), cancellationToken);
        }
        catch (Exception failure) when (attempted is not null && TransportFailure(failure))
        {
            if (await VerifyAsync(cancellationToken)) return attempted!.Result;
            throw;
        }
        finally { writes?.Dispose(); }

        async Task<BaseCommandResponse<Guid>> ExecuteAttemptAsync(CancellationToken token)
        {
            RequireCreator();
            if (await VerifyAsync(token)) return attempted!.Result;
            if (await identity.ResolveUserIdAsync(token) != creation.CreatorUserId) DenyInitialCreation();
            var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(ResourceKinds.Event, AuthorizationActions.Create),
                CreateEventCommand.PreCreateResourceId,
                Facts: new PreCreateAuthorizationFacts(creation.TenantId, null, creation.OrganizationId, creation.GroupId),
                Subject: new AuthorizationSubject(creation.CreatorUserId), Tenant: new AuthorizationTenant(creation.TenantId)), token);
            if (!decision.IsAllowed)
            {
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(ResourceKinds.Event, AuthorizationActions.Create);
                DenyInitialCreation();
            }
            context.ChangeTracker.Clear();
            Guid? epochId = null;
            long revision = 0;
            long beforeRevision = 0;
            HostState? heldHost = null;
            bool held = false;
            InitialPublisherPlan? plan = null;
            InitialImage[] heldImages = [];
            DateTime? acquiredAt = null;
            BaseCommandResponse<Guid>? result = null;
            EventPublicationModerationFacts? publication = null;
            var publicationReader = new EventPublicationModerationSnapshotReader(context, continuity);
            async Task RecheckAsync(CancellationToken inner)
            {
                RequireCreator();
                if (await identity.ResolveUserIdAsync(inner) != creation.CreatorUserId
                    || await privacy.IsSubjectFencedAsync(creation.CreatorUserId, inner)
                    || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == creation.CreatorUserId && !row.IsDeleted, inner)
                    || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == creation.TenantId && row.TenantStatus.IsActiveState, inner)
                    || !await new ModerationQualificationAuthority(context, identity, machine)
                        .ProviderTenantMatchesAsync(creation.TenantId, inner)) DenyInitialCreation();
                if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                    .AnyAsync(row => row.Id == id && row.TenantId == creation.TenantId && row.Revision == revision, inner)) DenyInitialCreation();
                if (held)
                {
                    if (await ReadInitialHostAsync(inner) != heldHost
                        || heldHost is not null && !await context.ServicePrincipals.AsNoTracking()
                            .AnyAsync(row => row.Id == heldHost.MaintenanceServicePrincipalId && !row.IsDeleted, inner)) DenyInitialCreation();
                    if (!await qualifyPublisher(creation.CreatorUserId, inner)) DenyInitialCreation();
                    if (plan is null || !SameInitialPlan(plan, await ReadInitialPublisherPlanAsync(creation, inner))) throw Changed();
                    var currentImages = await ReadInitialImagesAsync(creation, inner);
                    if (currentImages.Length != heldImages.Length || currentImages.Where((image, index) =>
                        image.Id != heldImages[index].Id || image.ActorId != heldImages[index].ActorId
                            && (result is null || image.ActorId != creation.PublisherActorId)).Any()) throw Changed();
                }
                if (acquiredAt is { } start && clock.GetUtcNow().UtcDateTime < start) DenyInitialCreation();
                if (result is null) return;
                if (!result.IsSuccess)
                {
                    await writes!.CaptureAsync(inner);
                    if (writes!.HasWrites) throw Changed();
                    return;
                }
                if (result.Id != creation.EventId || !await ValidInitialGraphAsync(creation, inner)) throw Changed();
                if (creation.RequestedStatus == EventStatusEnum.Published)
                {
                    if (publication is null || publication.QualificationEpochId != epochId
                        || publication.QualificationRevision != beforeRevision
                        || revision != (epochId.HasValue ? checked(beforeRevision + 1) : 0)
                        || !publication.PermitsAt(clock.GetUtcNow())) throw Changed();
                    var current = await publicationReader.ReadAsync(creation.CreatorUserId, clock.GetUtcNow(), inner);
                    // Only this creation's exact epoch advance may differ from the original authority snapshot.
                    if ((publication with { QualificationRevision = revision }) != current) throw Changed();
                }
                if (attempted is null)
                {
                    await writes!.CaptureAsync(inner);
                    attempted = new(result, epochId, beforeRevision, revision);
                }
                else if (await writes!.ReadStateAsync(inner) != EventInitialCreationWriteState.After) throw Changed();
                // Snapshot and receipt reads await I/O; they cannot extend the original publication window.
                if (creation.RequestedStatus == EventStatusEnum.Published
                    && !publication!.PermitsAt(clock.GetUtcNow())) throw Changed();
            }
            return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async inner =>
                {
                    await using var hierarchy = creation.OrganizationId is null && creation.GroupId.HasValue
                        ? await RelationalNamedLock.AcquireTransactionAsync(context, $"group-hierarchy:{creation.TenantId}", inner) : null;
                    await using var installation = await RelationalNamedLock.AcquireTransactionAsync(context,
                        "moderation.authority-source.installation", inner);
                    plan = await ReadInitialPublisherPlanAsync(creation, inner);
                    heldImages = await ReadInitialImagesAsync(creation, inner);
                    if (heldImages.Length != creation.ImageIds.Count) DenyInitialCreation();
                    await EventAuthoritySnapshotService.AcquireHumanSourcesAsync(context, creation.TenantId, [creation.CreatorUserId],
                        plan.Members.Select(row => row.RoleId).Append((int)RoleEnum.EventOwner).ToArray(), null, inner);
                    heldHost = await ReadInitialHostAsync(inner);
                    if (heldHost is not null)
                    {
                        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, heldHost.Id, inner);
                        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, heldHost.MaintenanceServicePrincipalId, inner);
                    }
                    if (creation.OrganizationId is { } organization)
                        await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(context, organization, inner);
                    else if (creation.GroupId is { } group)
                        await RelationalEntityRowFence.AcquireGlobalAsync<Group>(context, group, inner);
                    foreach (var actor in plan.ActorIds.Concat(heldImages.Where(image => image.ActorId.HasValue)
                        .Select(image => image.ActorId!.Value)).Distinct().Order())
                        await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actor, inner);
                    await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, creation.TenantId, inner);
                    var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == creation.TenantId, inner);
                    if (tenant is null) DenyInitialCreation();
                    await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], inner);
                    foreach (var image in heldImages)
                        await RelationalEntityRowFence.AcquireAsync<StorageObject>(context, creation.TenantId, row => row.Id, image.Id, inner);
                    foreach (var participation in plan.Participations)
                    {
                        if (creation.OrganizationId.HasValue)
                            await RelationalEntityRowFence.AcquireAsync<OrganizationTenant>(context, creation.TenantId, row => row.Id, participation.Id, inner);
                        else
                            await RelationalEntityRowFence.AcquireAsync<GroupTenant>(context, creation.TenantId, row => row.Id, participation.Id, inner);
                    }
                    foreach (var statusId in plan.Participations.Select(row => row.StatusId).Distinct().Order())
                        await RelationalEntityRowFence.AcquireLookupAsync<ApprovalStatus>(context, [statusId], inner);
                    foreach (var membership in plan.Members)
                    {
                        if (creation.OrganizationId.HasValue)
                            await RelationalEntityRowFence.AcquireAsync<OrganizationMember>(context, creation.TenantId, row => row.Id, membership.Id, inner);
                        else
                            await RelationalEntityRowFence.AcquireAsync<GroupMember>(context, creation.TenantId, row => row.Id, membership.Id, inner);
                    }
                    ModerationQualificationEpoch? epoch = null;
                    if (heldHost is not null)
                    {
                        var found = await context.ModerationQualificationEpochs.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.HostBindingId == heldHost.Id && row.TenantId == creation.TenantId, inner);
                        if (found is null) DenyInitialCreation();
                        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, found.Id, inner);
                        epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == found.Id, inner);
                        epochId = epoch.Id;
                        beforeRevision = revision = epoch.Revision;
                        if (revision < 1) DenyInitialCreation();
                    }
                    held = true;
                    await RecheckAsync(inner);
                    acquiredAt = clock.GetUtcNow().UtcDateTime;
                    if (creation.CreatedAtUtc > acquiredAt.Value) DenyInitialCreation();
                    if (creation.RequestedStatus == EventStatusEnum.Published)
                    {
                        if (heldHost is not null)
                        {
                            await using var instanceSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
                                $"moderation.publication:{heldHost.Id:N}:{heldHost.InstanceId:N}", inner);
                            await using var tenantSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
                                $"moderation.publication:{heldHost.Id:N}:{creation.TenantId:N}", inner);
                        }
                        publication = await publicationReader.ReadAsync(creation.CreatorUserId, clock.GetUtcNow(), inner);
                        if (!publication.PermitsAt(clock.GetUtcNow()))
                            return BaseCommandResponse.Failure<Guid>(
                                publication.Outcome == ISLAMU.Moderation.Domain.ModerationRestrictionOutcome.Denied
                                    ? "event_publish_restricted" : "event_publish_authority_unavailable",
                                "Publication is not available under the current moderation authority.");
                        if (publication.QualificationEpochId != epochId || publication.QualificationRevision != beforeRevision)
                            throw Changed();
                    }
                    writes?.Dispose();
                    writes = new EventInitialCreationWriteReceipt(context, creation.TenantId, creation.EventId, creation.ImageIds.ToArray());
                    await writes.InitializeAsync(inner);
                    result = await operation(creation.CreatorUserId, inner);
                    if (result.IsSuccess && epoch is not null)
                    {
                        epoch.Advance(revision);
                        revision = epoch.Revision;
                        await context.SaveChangesAsync(inner);
                    }
                    return result;
                }, RecheckAsync, privacyToken), RecheckAsync, token);
        }

        async Task<bool> VerifyAsync(CancellationToken token)
        {
            if (attempted is not { } receipt) return false;
            RequireCreator();
            if (await identity.ResolveUserIdAsync(token) != creation.CreatorUserId) DenyInitialCreation();
            context.ChangeTracker.Clear();
            return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async inner =>
                {
                    var state = await writes!.ReadStateAsync(inner);
                    long? revision = receipt.EpochId is { } id ? await context.ModerationQualificationEpochs.AsNoTracking()
                        .Where(row => row.Id == id).Select(row => (long?)row.Revision).SingleOrDefaultAsync(inner) : 0;
                    if (state == EventInitialCreationWriteState.After && revision == receipt.AfterRevision) return true;
                    if (state == EventInitialCreationWriteState.Before && revision == receipt.BeforeRevision)
                    { attempted = null; return false; }
                    throw Changed();
                }, privacyToken), _ => Task.CompletedTask, token);
        }
        void RequireCreator()
        {
            if (creation.TenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != creation.TenantId) DenyInitialCreation();
        }
    }

    private Task<HostState?> ReadInitialHostAsync(CancellationToken token) => context.ModerationHostBindings.AsNoTracking()
        .Select(row => new HostState(row.Id, row.TrustDomainId, row.SolutionId, row.InstanceId, row.MaintenanceServicePrincipalId))
        .SingleOrDefaultAsync(token);

    private async Task<InitialPublisherPlan> ReadInitialPublisherPlanAsync(EventInitialCreation creation, CancellationToken token)
    {
        var actors = context.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AsNoTracking();
        Guid[] actorIds = await actors.Where(row => row.Id == creation.PublisherActorId
                || creation.OrganizationId.HasValue && row.OrganizationId == creation.OrganizationId
                || !creation.OrganizationId.HasValue && creation.GroupId.HasValue && row.GroupId == creation.GroupId)
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        InitialParticipation[] participations = [];
        InitialMember[] members = [];
        if (creation.OrganizationId is { } organization)
        {
            participations = await context.OrganizationTenants.AsNoTracking().Where(row => row.TenantId == creation.TenantId && row.OrganizationId == organization)
                .OrderBy(row => row.Id).Select(row => new InitialParticipation(row.Id, row.ApprovalStatusId, row.IsOrganizerEligible, row.IsSuspended)).ToArrayAsync(token);
            var ids = participations.Select(row => row.Id).ToArray();
            members = await context.OrganizationMembers.AsNoTracking().Where(row => row.TenantId == creation.TenantId
                    && ids.Contains(row.OrganizationTenantId) && row.UserId == creation.CreatorUserId)
                .OrderBy(row => row.Id).Select(row => new InitialMember(row.Id, row.RoleId)).ToArrayAsync(token);
        }
        else if (creation.GroupId is { } group)
        {
            participations = await context.GroupTenants.AsNoTracking().Where(row => row.TenantId == creation.TenantId && row.GroupId == group)
                .OrderBy(row => row.Id).Select(row => new InitialParticipation(row.Id, row.ApprovalStatusId, row.IsOrganizerEligible, row.IsSuspended)).ToArrayAsync(token);
            var ids = participations.Select(row => row.Id).ToArray();
            members = await context.GroupMembers.AsNoTracking().Where(row => row.TenantId == creation.TenantId
                    && ids.Contains(row.GroupTenantId) && row.UserId == creation.CreatorUserId)
                .OrderBy(row => row.Id).Select(row => new InitialMember(row.Id, row.RoleId)).ToArrayAsync(token);
        }
        return new(actorIds, participations, members);
    }

    private async Task<bool> ValidInitialGraphAsync(EventInitialCreation creation, CancellationToken token)
    {
        var parent = await context.Events.AsNoTracking().SingleOrDefaultAsync(row => row.Id == creation.EventId && row.TenantId == creation.TenantId, token);
        if (parent is null || parent.EventStatusId != (int)creation.RequestedStatus
            || parent.ActorId != creation.PublisherActorId || parent.CreatedBy != creation.CreatorUserId
            || parent.CreatedAt != creation.CreatedAtUtc || parent.EventProvenanceTypeId != (int)(creation.CommunitySubmission
                ? EventProvenanceTypeEnum.CommunityReported : EventProvenanceTypeEnum.OrganizerCreated)
            || parent.OrganizerActorId != (creation.CommunitySubmission ? null : (Guid?)creation.PublisherActorId)
            || parent.SubmittedByUserId != (creation.CommunitySubmission ? (Guid?)creation.CreatorUserId : null)
            || !await context.Set<EventParticipationConfiguration>().AsNoTracking().AnyAsync(row => row.Id == creation.EventId
                && row.TenantId == creation.TenantId, token)) return false;
        var assignments = await context.EventRoleAssignments.AsNoTracking().Where(row => row.EventId == creation.EventId && row.TenantId == creation.TenantId).ToArrayAsync(token);
        if (creation.CommunitySubmission) return assignments.Length == 0;
        return assignments.Length == 1 && assignments[0].Id == creation.OwnerAssignmentId
            && assignments[0].UserId == creation.CreatorUserId && assignments[0].CreatedBy == creation.CreatorUserId
            && assignments[0].RoleId == (int)RoleEnum.EventOwner && assignments[0].Status == EventRoleAssignmentStatus.Active
            && assignments[0].StartsAtUtc == creation.CreatedAtUtc && assignments[0].ExpiresAtUtc is null && assignments[0].Version == 1;
    }

    private Task<InitialImage[]> ReadInitialImagesAsync(EventInitialCreation creation, CancellationToken token)
    {
        var ids = creation.ImageIds.ToArray();
        return context.StorageObjects.AsNoTracking().Where(row => row.TenantId == creation.TenantId && ids.Contains(row.Id))
            .OrderBy(row => row.Id).Select(row => new InitialImage(row.Id, row.ActorId)).ToArrayAsync(token);
    }

    private static bool SameInitialPlan(InitialPublisherPlan left, InitialPublisherPlan right) =>
        left.ActorIds.SequenceEqual(right.ActorIds) && left.Participations.SequenceEqual(right.Participations) && left.Members.SequenceEqual(right.Members);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void DenyInitialCreation() => throw new AuthorizationException(ResourceKinds.Event, AuthorizationActions.Create);
    private sealed record InitialMember(Guid Id, int RoleId);
    private sealed record InitialImage(Guid Id, Guid? ActorId);
    private sealed record InitialParticipation(Guid Id, int StatusId, bool Eligible, bool Suspended);
    private sealed record InitialPublisherPlan(Guid[] ActorIds, InitialParticipation[] Participations, InitialMember[] Members);
    private sealed record InitialCreationAttempt(BaseCommandResponse<Guid> Result, Guid? EpochId, long BeforeRevision, long AfterRevision)
    { public override string ToString() => nameof(InitialCreationAttempt); }
}
