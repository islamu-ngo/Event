using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Explore.Domain.Ai;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public class AiConversationRepository : GenericRepository<AiConversation, Guid>, IAiConversationRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly PostgresNativeQueueEnrollment? _nativeEnrollment;
    private readonly Dictionary<Guid, (IDbContextTransaction Transaction, RelationalConnectionIdentity Connection,
        bool Privacy, HashSet<(Type Type, Guid Id)> Children, AiConversation Root,
        Guid TenantId, Guid UserId, Guid? ActorId)> _mutationRoots = [];

    public AiConversationRepository(ExploreDbContext dbContext, PostgresNativeQueueEnrollment? nativeEnrollment = null) : base(dbContext)
    {
        _dbContext = dbContext;
        _nativeEnrollment = nativeEnrollment;
    }

    public async Task<AiConversation?> GetByIdWithDetailsAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await IncludeConversationDetails(_dbContext.AiConversations.AsNoTracking())
            .FirstOrDefaultAsync(conversation => conversation.Id == conversationId, cancellationToken);
    }

    public async Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return await LoadForMutationAsync(conversationId, privacy: false, cancellationToken);
    }

    public Task<AiConversation?> GetByIdForUpdateAsync(Guid conversationId, Guid? admittedActorId, CancellationToken cancellationToken) =>
        LoadForMutationAsync(conversationId, privacy: false, cancellationToken, admittedActorId);

    public Task<AiConversation?> GetByIdForPrivacyUpdateAsync(Guid conversationId, CancellationToken cancellationToken) =>
        LoadForMutationAsync(conversationId, privacy: true, cancellationToken);

    public async Task<bool> IsPrivacyFencedAsync(AiConversation conversation, CancellationToken cancellationToken)
    {
        void RequireOriginalOwner()
        {
            Guid tenantId = RequireMutationScope();
            if (!_mutationRoots.TryGetValue(conversation.Id, out var owner) || owner.Privacy
                || !ReferenceEquals(owner.Root, conversation)
                || _dbContext.Entry(conversation).State == EntityState.Detached
                || conversation.TenantId != tenantId || conversation.TenantId != owner.TenantId
                || conversation.UserId != owner.UserId || conversation.ActorId != owner.ActorId
                || !ReferenceEquals(owner.Transaction, _dbContext.Database.CurrentTransaction)
                || !owner.Connection.Matches(_dbContext)
                || !owner.Connection.Owns(owner.Transaction.GetDbTransaction()))
                throw new InvalidOperationException("AI privacy fencing requires its unchanged original acquired graph owner.");
        }
        RequireOriginalOwner();
        bool fenced = AiRepositoryNativeOperations.For(_dbContext) is { } native
            ? await native.IsPrivacyFencedAsync(
                _nativeEnrollment ?? throw new InvalidOperationException("AI privacy fencing requires deployment-owned native enrollment."),
                conversation.TenantId, conversation.UserId, cancellationToken)
            : await _dbContext.PrivacyErasureSagas.AsNoTracking()
                .AnyAsync(value => value.SubjectId == conversation.UserId, cancellationToken);
        RequireOriginalOwner();
        return fenced;
    }

    private Guid RequireMutationScope()
    {
        _dbContext.ValidateTenantLease();
        if (_dbContext.IsTenantFilterBypassed || _dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("AI mutation requires its exact Runtime tenant Serializable transaction.");
        return tenantId;
    }

    private async Task<bool> FenceOwnerAsync(Guid userId, Guid? actorId, bool privacy, CancellationToken cancellationToken,
        Guid? admittedActorId = null)
    {
        Guid tenantId = RequireMutationScope();
        if (AiRepositoryNativeOperations.For(_dbContext) is { } native)
            return await native.FenceOwnerAsync(
                _nativeEnrollment ?? throw new InvalidOperationException("AI owner fencing requires deployment-owned native enrollment."),
                tenantId, userId, actorId, admittedActorId, privacy, cancellationToken);
        foreach (Guid actor in new[] { actorId, admittedActorId }.Where(value => value.HasValue)
            .Select(value => value!.Value).Distinct().Order())
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(_dbContext, actor, cancellationToken);
            if (!privacy && admittedActorId == actor && !await _dbContext.Actors.AsNoTracking()
                .AnyAsync(value => value.Id == actor && !value.IsDeleted && !value.IsSuspended, cancellationToken))
                return false;
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(_dbContext, userId, cancellationToken);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(_dbContext, tenantId, cancellationToken);
        int? statusId = await _dbContext.Tenants.AsNoTracking().Where(value => value.Id == tenantId)
            .Select(value => (int?)value.TenantStatusId).SingleOrDefaultAsync(cancellationToken);
        if (statusId is null) return false;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(_dbContext, [statusId.Value], cancellationToken);
        bool eligible = privacy || statusId == (int)TenantStatusEnum.Active
            && await _dbContext.Set<TenantStatus>().AsNoTracking()
                .AnyAsync(value => value.Id == statusId && value.IsActiveState, cancellationToken)
            && await _dbContext.Users.AsNoTracking().AnyAsync(value => value.Id == userId && !value.IsDeleted, cancellationToken);
        return eligible && (privacy || admittedActorId is null
            || await FenceActorGrantAsync(userId, admittedActorId.Value, cancellationToken));
    }

    private async Task<bool> FenceActorGrantAsync(Guid userId, Guid actorId, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationScope();
        var actor = await _dbContext.Actors.AsNoTracking().Where(value => value.Id == actorId && !value.IsDeleted && !value.IsSuspended)
            .Select(value => new
            {
                value.ActorTypeId,
                value.UserId,
                value.OrganizationId,
                value.GroupId,
                value.ServicePrincipalId,
                value.ExternalActorSubjectId
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (actor is null || actor.ServicePrincipalId is not null || actor.ExternalActorSubjectId is not null) return false;
        var tenantUsers = await _dbContext.TenantUsers.AsNoTracking()
            .Where(value => value.TenantId == tenantId && value.UserId == userId && !value.IsDeleted
                && value.StatusId == (int)TenantUserStatusEnum.Active)
            .Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
        foreach (Guid id in tenantUsers)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(_dbContext, tenantId, value => value.Id, id, cancellationToken);
        if (tenantUsers.Count == 0) return false;
        if (actor.ActorTypeId == (int)ActorTypeEnum.User)
            return actor.UserId == userId && actor.OrganizationId is null && actor.GroupId is null;
        List<int> roleIds;
        if (actor.ActorTypeId == (int)ActorTypeEnum.Organization && actor.OrganizationId is { } organizationId
            && actor.UserId is null && actor.GroupId is null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(_dbContext, organizationId, cancellationToken);
            var links = await _dbContext.OrganizationTenants.AsNoTracking()
                .Where(value => value.TenantId == tenantId && value.OrganizationId == organizationId && !value.IsDeleted
                    && !value.IsSuspended && value.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                    && !value.Organization.IsDeleted).Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
            foreach (Guid id in links)
                await RelationalEntityRowFence.AcquireAsync<OrganizationTenant>(_dbContext, tenantId, value => value.Id, id, cancellationToken);
            var members = await _dbContext.OrganizationMembers.AsNoTracking()
                .Where(value => value.TenantId == tenantId && value.UserId == userId && !value.IsDeleted
                    && links.Contains(value.OrganizationTenantId)).Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
            foreach (Guid id in members)
                await RelationalEntityRowFence.AcquireAsync<OrganizationMember>(_dbContext, tenantId, value => value.Id, id, cancellationToken);
            roleIds = await _dbContext.OrganizationMembers.AsNoTracking()
                .Where(value => value.TenantId == tenantId && value.UserId == userId && !value.IsDeleted
                    && members.Contains(value.Id) && !value.OrganizationTenant.IsDeleted && !value.OrganizationTenant.Organization.IsDeleted)
                .Select(value => value.RoleId).Distinct().OrderBy(value => value).ToListAsync(cancellationToken);
        }
        else if (actor.ActorTypeId == (int)ActorTypeEnum.Group && actor.GroupId is { } groupId
            && actor.UserId is null && actor.OrganizationId is null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Group>(_dbContext, groupId, cancellationToken);
            var links = await _dbContext.GroupTenants.AsNoTracking()
                .Where(value => value.TenantId == tenantId && value.GroupId == groupId && !value.IsDeleted
                    && !value.IsSuspended && value.ApprovalStatusId == (int)ApprovalStatusEnum.Approved
                    && !value.Group.IsDeleted).Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
            foreach (Guid id in links)
                await RelationalEntityRowFence.AcquireAsync<GroupTenant>(_dbContext, tenantId, value => value.Id, id, cancellationToken);
            var members = await _dbContext.GroupMembers.AsNoTracking()
                .Where(value => value.TenantId == tenantId && value.UserId == userId && !value.IsDeleted
                    && links.Contains(value.GroupTenantId)).Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
            foreach (Guid id in members)
                await RelationalEntityRowFence.AcquireAsync<GroupMember>(_dbContext, tenantId, value => value.Id, id, cancellationToken);
            roleIds = await _dbContext.GroupMembers.AsNoTracking()
                .Where(value => value.TenantId == tenantId && value.UserId == userId && !value.IsDeleted
                    && members.Contains(value.Id) && !value.GroupTenant.IsDeleted && !value.GroupTenant.Group.IsDeleted)
                .Select(value => value.RoleId).Distinct().OrderBy(value => value).ToListAsync(cancellationToken);
        }
        else return false;
        if (roleIds.Count == 0) return false;
        foreach (int roleId in roleIds)
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(_dbContext, [roleId], cancellationToken);
        var permissions = await _dbContext.Set<Permission>().AsNoTracking()
            .Where(value => value.MasterCode == PermissionCodes.EventCreate)
            .Select(value => value.Id).OrderBy(value => value).ToListAsync(cancellationToken);
        foreach (int permissionId in permissions)
            await RelationalEntityRowFence.AcquireLookupAsync<Permission>(_dbContext, [permissionId], cancellationToken);
        var grants = await _dbContext.Set<RolePermission>().AsNoTracking()
            .Where(value => roleIds.Contains(value.RoleId) && permissions.Contains(value.PermissionId))
            .Select(value => new { value.RoleId, value.PermissionId }).OrderBy(value => value.RoleId)
            .ThenBy(value => value.PermissionId).ToListAsync(cancellationToken);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquirePrimaryKeyAsync<RolePermission>(
                _dbContext, [grant.RoleId, grant.PermissionId], cancellationToken);
        return await _dbContext.Set<RolePermission>().AsNoTracking()
            .AnyAsync(value => roleIds.Contains(value.RoleId) && permissions.Contains(value.PermissionId)
                && value.Role.RoleScopeId == (int)(actor.ActorTypeId == (int)ActorTypeEnum.Organization
                    ? RoleScopeEnum.Organization : RoleScopeEnum.Group)
                && value.Permission.MasterCode == PermissionCodes.EventCreate && value.Permission.IsActive, cancellationToken);
    }

    private async Task<AiConversation?> LoadForMutationAsync(Guid conversationId, bool privacy, CancellationToken cancellationToken,
        Guid? admittedActorId = null)
    {
        Guid tenantId = RequireMutationScope();
        var candidate = await _dbContext.AiConversations.AsNoTracking()
            .Where(value => value.Id == conversationId && value.TenantId == tenantId && !value.IsDeleted)
            .Select(value => new { value.UserId, value.ActorId }).SingleOrDefaultAsync(cancellationToken);
        if (candidate is null || !await FenceOwnerAsync(candidate.UserId, candidate.ActorId, privacy, cancellationToken, admittedActorId)) return null;
        await RelationalEntityRowFence.AcquireAsync<AiConversation>(
            _dbContext, tenantId, value => value.Id, conversationId, cancellationToken);
        var entries = _dbContext.ChangeTracker.Entries().Where(entry => entry.Entity switch
        {
            AiConversation value => value.Id == conversationId,
            AiMessage value => value.ConversationId == conversationId,
            AiRun value => value.ConversationId == conversationId,
            AiConversationReference value => value.ConversationId == conversationId,
            AiProposedAction value => value.ConversationId == conversationId,
            _ => false
        }).ToArray();
        if (entries.Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("AI mutation cannot discard pending graph changes when reacquiring authority.");
        foreach (var entry in entries) entry.State = EntityState.Detached;
        var root = await IncludeConversationDetails(_dbContext.AiConversations)
            .SingleOrDefaultAsync(value => value.Id == conversationId && value.TenantId == tenantId
                && value.UserId == candidate.UserId && value.ActorId == candidate.ActorId && !value.IsDeleted, cancellationToken);
        if (root is not null)
        {
            _mutationRoots[root.Id] = (_dbContext.Database.CurrentTransaction!, RelationalConnectionIdentity.Capture(_dbContext), privacy,
                root.Messages.Select(value => (typeof(AiMessage), value.Id))
                    .Concat(root.Runs.Select(value => (typeof(AiRun), value.Id)))
                    .Concat(root.References.Select(value => (typeof(AiConversationReference), value.Id)))
                    .Concat(root.ProposedActions.Select(value => (typeof(AiProposedAction), value.Id))).ToHashSet(),
                root, tenantId, candidate.UserId, candidate.ActorId);
            if (AiRepositoryNativeOperations.For(_dbContext) is { } native)
                native.RetainGraphOwner(root.Id, tenantId, candidate.UserId, candidate.ActorId, admittedActorId);
        }
        return root;
    }

    public override async Task<AiConversation> Create(AiConversation entity)
    {
        Guid tenantId = RequireMutationScope();
        Guid userId = entity.UserId;
        Guid? actorId = entity.ActorId;
        if (entity.TenantId != tenantId || entity.UserId == Guid.Empty
            || !await FenceOwnerAsync(userId, actorId, privacy: false, CancellationToken.None, actorId)
            || entity.TenantId != tenantId || entity.UserId != userId || entity.ActorId != actorId)
            throw new AuthorizationException(ResourceKinds.AiConversation, AuthorizationActions.Create);
        if (AiRepositoryNativeOperations.For(_dbContext) is { } native)
            native.RetainGraphOwner(entity.Id, tenantId, userId, actorId, actorId);
        return await base.Create(entity);
    }

    public async Task<int> HardDeleteUserConversationGraphAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        RequireId(subjectId, nameof(subjectId));

        string reason = TenantFilterBypassReasons.UserPrivacyErasure;

        // ponytail: root delete relies on verified FK cascade shape for the entire AI graph.
        return await _dbContext.AiConversations
            .IgnoreAllFilters(reason)
            .Where(conversation => conversation.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiConversation>> ListRecentForUserAsync(
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        var boundedLimit = Math.Min(limit, 100);

        return await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);
    }

    public override async Task Update(AiConversation entity)
    {
        Guid tenantId = RequireMutationScope();
        var entry = _dbContext.Entry(entity);
        if (entry.State == EntityState.Detached || entity.TenantId != tenantId
            || !_mutationRoots.TryGetValue(entity.Id, out var authority)
            || !ReferenceEquals(authority.Transaction, _dbContext.Database.CurrentTransaction)
            || !authority.Connection.Matches(_dbContext)
            || !authority.Connection.Owns(authority.Transaction.GetDbTransaction())
            || entry.OriginalValues.GetValue<Guid>(nameof(AiConversation.UserId)) != entity.UserId
            || entry.OriginalValues.GetValue<Guid>(nameof(AiConversation.TenantId)) != entity.TenantId)
            throw new InvalidOperationException("AI persistence requires its freshly fenced tracked root and unchanged ownership.");
        if (entity.Messages.Select(value => (value.TenantId, value.ConversationId))
            .Concat(entity.Runs.Select(value => (value.TenantId, value.ConversationId)))
            .Concat(entity.References.Select(value => (value.TenantId, value.ConversationId)))
            .Concat(entity.ProposedActions.Select(value => (value.TenantId, value.ConversationId)))
            .Any(value => value.TenantId != tenantId || value.ConversationId != entity.Id))
            throw new InvalidOperationException("AI graph children must belong to the exact fenced tenant and root.");
        var children = entity.Messages.Select(value => _dbContext.Entry((object)value))
            .Concat(entity.Runs.Select(value => _dbContext.Entry((object)value)))
            .Concat(entity.References.Select(value => _dbContext.Entry((object)value)))
            .Concat(entity.ProposedActions.Select(value => _dbContext.Entry((object)value))).ToArray();
        foreach (var child in children)
        {
            var key = (child.Metadata.ClrType, (Guid)child.Property("Id").CurrentValue!);
            // Application-assigned UUIDs make EF treat discovered new children as
            // existing rows. Only the freshly loaded graph establishes persisted keys.
            if (!authority.Children.Contains(key))
                child.State = EntityState.Added;
            else if (child.State == EntityState.Detached)
                throw new InvalidOperationException("AI persistence cannot replace its freshly tracked children.");
        }
        _dbContext.ChangeTracker.DetectChanges();
        if (authority.Privacy && (entry.Properties.Any(property => property.IsModified)
            || _dbContext.ChangeTracker.Entries().Any(child => child.Entity switch
            {
                AiMessage value when value.ConversationId == entity.Id =>
                    child.State is EntityState.Added or EntityState.Deleted
                    || child.Properties.Any(property => property.IsModified && property.Metadata.Name != nameof(AiMessage.Content)),
                AiConversationReference value when value.ConversationId == entity.Id =>
                    child.State is EntityState.Added or EntityState.Deleted
                    || child.Properties.Any(property => property.IsModified && property.Metadata.Name != nameof(AiConversationReference.Summary)),
                AiRun value when value.ConversationId == entity.Id => child.State != EntityState.Unchanged,
                AiProposedAction value when value.ConversationId == entity.Id => child.State != EntityState.Unchanged,
                _ => false
            })))
            throw new InvalidOperationException("AI privacy cleanup may only redact existing transcript content and reference summaries.");
        entity.ConcurrencyStamp = Guid.CreateVersion7();
        entry.State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();
        authority.Children.UnionWith(children.Select(child =>
            (child.Metadata.ClrType, (Guid)child.Property("Id").CurrentValue!)));
    }

    public async Task<int> CountUserMessagesSinceAsync(
        Guid userId,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiMessages
            .AsNoTracking()
            .Where(message => message.RoleId == (int)AiMessageRole.User)
            .Where(message => message.CreatedBy == userId)
            .Where(message => message.CreatedAt >= sinceUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountTenantMessagesSinceAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiMessages
            .AsNoTracking()
            .Where(message => message.RoleId == (int)AiMessageRole.User)
            .Where(message => message.CreatedAt >= sinceUtc)
            .CountAsync(cancellationToken);
    }

    public async Task<int> ReleaseStaleRunningConversationsForUserAsync(
        Guid userId,
        DateTime staleBeforeUtc,
        string failureCode,
        string failureMessage,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationScope();
        if (!await FenceOwnerAsync(userId, null, privacy: false, cancellationToken))
            throw new AuthorizationException(ResourceKinds.AiConversation, AuthorizationActions.Update);
        var activeRunStatuses = new[]
        {
            (int)AiRunStatus.Queued,
            (int)AiRunStatus.InProgress
        };

        var staleQuery = _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId && conversation.TenantId == tenantId)
            .Where(conversation => conversation.StatusId == (int)AiConversationStatus.Running)
            .Where(conversation => conversation.Runs.Any(run =>
                activeRunStatuses.Contains(run.StatusId)
                && (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc))
            .Where(conversation => !conversation.Runs.Any(run =>
                activeRunStatuses.Contains(run.StatusId)
                && (run.StartedAt ?? run.QueuedAt) > staleBeforeUtc))
            .Select(conversation => conversation.Id);
        var candidates = await staleQuery.OrderBy(value => value).ToListAsync(cancellationToken);
        foreach (Guid id in candidates)
            await RelationalEntityRowFence.AcquireAsync<AiConversation>(
                _dbContext, tenantId, value => value.Id, id, cancellationToken);
        var staleConversationIds = await staleQuery.Where(value => candidates.Contains(value)).ToListAsync(cancellationToken);

        if (staleConversationIds.Count == 0)
        {
            return 0;
        }

        var normalizedFailureCode = string.IsNullOrWhiteSpace(failureCode)
            ? "stale_ai_run_released"
            : failureCode.Trim();
        var normalizedFailureMessage = string.IsNullOrWhiteSpace(failureMessage)
            ? "AI run was released after it stopped reporting progress."
            : failureMessage.Trim();

        await _dbContext.AiRuns
            .Where(run => run.TenantId == tenantId && staleConversationIds.Contains(run.ConversationId))
            .Where(run => activeRunStatuses.Contains(run.StatusId))
            .Where(run => (run.StartedAt ?? run.QueuedAt) <= staleBeforeUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.StatusId, (int)AiRunStatus.Failed)
                .SetProperty(run => run.CompletedAt, utcNow)
                .SetProperty(run => run.FailureCode, normalizedFailureCode)
                .SetProperty(run => run.FailureMessage, normalizedFailureMessage),
                cancellationToken);

        Guid recoveryStamp = Guid.CreateVersion7();
        return await _dbContext.AiConversations
            .Where(conversation => conversation.TenantId == tenantId && conversation.UserId == userId
                && staleConversationIds.Contains(conversation.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.StatusId, (int)AiConversationStatus.Active)
                .SetProperty(conversation => conversation.BlockedReason, (string?)null)
                .SetProperty(conversation => conversation.UpdatedAt, utcNow)
                .SetProperty(conversation => conversation.UpdatedBy, userId)
                .SetProperty(conversation => conversation.ConcurrencyStamp, recoveryStamp),
                cancellationToken);
    }

    public async Task<int> CountRunningConversationsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .Where(conversation => conversation.StatusId == (int)AiConversationStatus.Running)
            .CountAsync(cancellationToken);
    }

    public Task<AiProposedAction?> GetProposedActionForUpdateAsync(
        Guid proposedActionId,
        CancellationToken cancellationToken) =>
        GetProposedActionForUpdateAsync(proposedActionId, null, cancellationToken);

    public async Task<AiProposedAction?> GetProposedActionForUpdateAsync(
        Guid proposedActionId, Guid? admittedActorId, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationScope();
        Guid? rootId = await _dbContext.AiProposedActions.AsNoTracking()
            .Where(value => value.Id == proposedActionId && value.TenantId == tenantId)
            .Select(value => (Guid?)value.ConversationId).SingleOrDefaultAsync(cancellationToken);
        if (rootId is null) return null;
        var root = await GetByIdForUpdateAsync(rootId.Value, admittedActorId, cancellationToken);
        if (root is null) return null;
        await RelationalEntityRowFence.AcquireAsync<AiProposedAction>(
            _dbContext, tenantId, value => value.Id, proposedActionId, cancellationToken);
        return root.ProposedActions.SingleOrDefault(value => value.Id == proposedActionId && value.TenantId == tenantId);
    }

    public async Task<AiProposedAction?> GetProposedActionAsync(Guid proposedActionId, CancellationToken cancellationToken)
    {
        return await _dbContext.AiProposedActions
            // Proposal decisions belong to UpdateProposedActionAsync, not a nested tool's SaveChanges.
            .AsNoTrackingWithIdentityResolution()
            .Include(action => action.Conversation)
                .ThenInclude(conversation => conversation!.Messages)
            .Include(action => action.Message)
            .FirstOrDefaultAsync(action => action.Id == proposedActionId, cancellationToken);
    }

    public async Task UpdateProposedActionAsync(AiProposedAction proposedAction, CancellationToken cancellationToken)
    {
        RequireMutationScope();
        var root = _dbContext.ChangeTracker.Entries<AiConversation>()
            .Select(entry => entry.Entity).SingleOrDefault(value => value.Id == proposedAction.ConversationId);
        if (root is null || !root.ProposedActions.Any(value => ReferenceEquals(value, proposedAction)))
            throw new InvalidOperationException("AI action persistence requires the exact tracked action and fenced parent.");
        await Update(root);
    }

    public async Task CreateToolExecutionAsync(AiToolExecution toolExecution, CancellationToken cancellationToken)
    {
        Guid tenantId = RequireMutationScope();
        var root = _dbContext.ChangeTracker.Entries<AiConversation>().Select(entry => entry.Entity)
            .SingleOrDefault(value => value.TenantId == tenantId
                && value.ProposedActions.Any(action => action.Id == toolExecution.ProposedActionId));
        if (root is null || toolExecution.TenantId != tenantId)
            throw new InvalidOperationException("AI execution persistence requires its exact fenced action and parent tenant.");
        await _dbContext.AiToolExecutions.AddAsync(toolExecution, cancellationToken);
        await Update(root);
    }

    public async Task<IReadOnlyList<AiToolExecution>> ListToolExecutionsForProposedActionAsync(
        Guid proposedActionId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.AiToolExecutions
            .AsNoTracking()
            .Where(execution => execution.ProposedActionId == proposedActionId)
            .OrderByDescending(execution => execution.StartedAt)
            .ThenByDescending(execution => execution.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiRetentionCleanupResult> RedactExpiredConversationsAsync(
        DateTime cutoffUtc,
        int retentionDays,
        DateTime utcNow,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        if (_dbContext.IsTenantFilterBypassed || _dbContext.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty
            || _dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            throw new InvalidOperationException("AI retention requires its exact Runtime tenant Serializable transaction.");
        var conversationIds = await _dbContext.AiConversations
            .AsNoTracking()
            .Where(conversation => conversation.TenantId == tenantId)
            .Where(conversation => (conversation.UpdatedAt ?? conversation.CreatedAt) <= cutoffUtc)
            .OrderBy(conversation => conversation.UpdatedAt ?? conversation.CreatedAt)
            .Select(conversation => conversation.Id)
            .ToListAsync(cancellationToken);

        if (!dryRun)
        {
            foreach (Guid id in conversationIds.Order())
                await RelationalEntityRowFence.AcquireAsync<AiConversation>(
                    _dbContext, tenantId, conversation => conversation.Id, id, cancellationToken);
            conversationIds = await _dbContext.AiConversations.AsNoTracking()
                .Where(conversation => conversation.TenantId == tenantId && conversationIds.Contains(conversation.Id)
                    && (conversation.UpdatedAt ?? conversation.CreatedAt) <= cutoffUtc)
                .Select(conversation => conversation.Id).ToListAsync(cancellationToken);
        }

        if (conversationIds.Count == 0)
        {
            return new AiRetentionCleanupResult(cutoffUtc, retentionDays, 0, 0, 0, 0, 0, 0, 0, dryRun);
        }

        var proposedActionIds = await _dbContext.AiProposedActions
            .AsNoTracking()
            .Where(action => conversationIds.Contains(action.ConversationId))
            .Select(action => action.Id)
            .ToListAsync(cancellationToken);

        var messageCount = await _dbContext.AiMessages
            .AsNoTracking()
            .Where(message => conversationIds.Contains(message.ConversationId))
            .CountAsync(cancellationToken);
        var runCount = await _dbContext.AiRuns
            .AsNoTracking()
            .Where(run => conversationIds.Contains(run.ConversationId))
            .CountAsync(cancellationToken);
        var referenceCount = await _dbContext.AiConversationReferences
            .AsNoTracking()
            .Where(reference => conversationIds.Contains(reference.ConversationId))
            .CountAsync(cancellationToken);
        var proposedActionCount = proposedActionIds.Count;
        var toolExecutionCount = proposedActionIds.Count == 0
            ? 0
            : await _dbContext.AiToolExecutions
                .AsNoTracking()
                .Where(execution => proposedActionIds.Contains(execution.ProposedActionId))
                .CountAsync(cancellationToken);

        if (dryRun)
        {
            return new AiRetentionCleanupResult(
                cutoffUtc,
                retentionDays,
                conversationIds.Count,
                0,
                0,
                0,
                0,
                0,
                0,
                dryRun);
        }

        await _dbContext.AiMessages
            .Where(message => conversationIds.Contains(message.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Content, RetentionRedactedMessage),
                cancellationToken);

        await _dbContext.AiRuns
            .Where(run => conversationIds.Contains(run.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.FailureMessage, (string?)null),
                cancellationToken);

        await _dbContext.AiConversationReferences
            .Where(reference => conversationIds.Contains(reference.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(reference => reference.DisplayName, RetentionRedactedReference)
                .SetProperty(reference => reference.Summary, (string?)null),
                cancellationToken);

        await _dbContext.AiProposedActions
            .Where(action => conversationIds.Contains(action.ConversationId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(action => action.PayloadJson, RetentionRedactedPayload)
                .SetProperty(action => action.FailureMessage, (string?)null),
                cancellationToken);

        if (proposedActionIds.Count > 0)
        {
            await _dbContext.AiToolExecutions
                .Where(execution => proposedActionIds.Contains(execution.ProposedActionId))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(execution => execution.FailureMessage, (string?)null),
                    cancellationToken);
        }

        var redactedConversationCount = await _dbContext.AiConversations
            .Where(conversation => conversationIds.Contains(conversation.Id))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(conversation => conversation.Title, RetentionRedactedTitle)
                .SetProperty(conversation => conversation.BlockedReason, (string?)null)
                .SetProperty(conversation => conversation.IsDeleted, true)
                .SetProperty(conversation => conversation.DeletedAt, utcNow)
                .SetProperty(conversation => conversation.DeletedBy, (Guid?)null)
                .SetProperty(conversation => conversation.UpdatedAt, utcNow)
                .SetProperty(conversation => conversation.UpdatedBy, (Guid?)null),
                cancellationToken);

        return new AiRetentionCleanupResult(
            cutoffUtc,
            retentionDays,
            conversationIds.Count,
            redactedConversationCount,
            messageCount,
            runCount,
            referenceCount,
            proposedActionCount,
            toolExecutionCount,
            dryRun);
    }

    private static IQueryable<AiConversation> IncludeConversationDetails(IQueryable<AiConversation> query)
    {
        return query
            .Include(conversation => conversation.Messages.OrderBy(message => message.Sequence))
            .Include(conversation => conversation.Runs.OrderBy(run => run.QueuedAt))
            .Include(conversation => conversation.References.OrderBy(reference => reference.CreatedAt))
            .Include(conversation => conversation.ProposedActions.OrderBy(action => action.CreatedAt));
    }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }

    private const string RetentionRedactedMessage = "[redacted by AI retention policy]";
    private const string RetentionRedactedReference = "[redacted reference]";
    private const string RetentionRedactedPayload = "{}";
    private const string RetentionRedactedTitle = "[redacted AI conversation]";
}
