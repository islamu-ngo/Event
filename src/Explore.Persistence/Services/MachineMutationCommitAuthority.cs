using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

public sealed class MachineMutationCommitAuthority(
    ExploreDbContext dbContext,
    IPersistedApiKeyProofAccessor proofs,
    IHttpContextAccessor httpContext,
    TimeProvider clock) : IMachineMutationCommitAuthority
{
    public async Task<Event?> GetAuthorizedSessionParentAsync(
        Guid tenantId, Guid eventId, string action, CancellationToken cancellationToken)
    {
        if (action is not (AuthorizationActions.Create or AuthorizationActions.Update or AuthorizationActions.Delete)
            || eventId == Guid.Empty || !HasRuntimeTransaction(tenantId))
            return null;
        var proof = proofs.CurrentProof;
        if (proof is null || proof.OwnerType is not (ExternalApiKeyOwnerType.Tenant or ExternalApiKeyOwnerType.User)
            || !await FenceTenantKeyAsync(proof, tenantId, ResourceKinds.EventSession, action, cancellationToken))
            return null;
        Event? parent;
        if (proof.OwnerType == ExternalApiKeyOwnerType.User)
        {
            parent = await new EventMutationAuthority(dbContext, clock).ForSessionAsync(
                tenantId, proof.OwnerId, eventId, action, httpContext, cancellationToken);
        }
        else
        {
            var planned = await dbContext.Events.AsNoTracking().Where(row => row.Id == eventId && row.TenantId == tenantId)
                .Select(row => new { row.ActorId, row.OrganizerActorId }).SingleOrDefaultAsync(cancellationToken);
            if (planned is null)
                return null;
            Guid[] actors = planned.OrganizerActorId is { } organizer ? [planned.ActorId, organizer] : [planned.ActorId];
            foreach (Guid actorId in actors.Distinct().OrderBy(id => id.ToString("N"), StringComparer.Ordinal))
                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(dbContext, actorId, cancellationToken);
            await RelationalEntityRowFence.AcquireAsync<Event>(dbContext, tenantId, row => row.Id, eventId, cancellationToken);
            parent = await dbContext.Events.AsNoTracking().Include(row => row.Actor)
                .Include(row => row.OrganizerActor).Include(row => row.ParticipationConfiguration)
                .SingleOrDefaultAsync(row => row.Id == eventId && row.TenantId == tenantId, cancellationToken);
            if (parent is null || parent.ActorId != planned.ActorId || parent.OrganizerActorId != planned.OrganizerActorId)
                throw new DbUpdateConcurrencyException("Session parent ownership changed before its machine fence.");
            if (parent.IsDeleted || parent.Actor.IsDeleted || parent.Actor.IsSuspended)
                return null;
        }
        // All key/owner/parent locks remain held. Re-evaluate time after the last wait.
        return parent is not null && await KeyStillUsableAsync(
            proof, tenantId, ResourceKinds.EventSession, action, cancellationToken) ? parent : null;
    }

    public async Task<bool> AuthorizeFooterPatchAsync(
        Guid tenantId, Guid groupId, Guid? linkId, CancellationToken cancellationToken)
    {
        if (groupId == Guid.Empty || linkId == Guid.Empty || !HasRuntimeTransaction(tenantId) || proofs.CurrentProof is not { } proof
            || !SnapshotPermits(proof, ResourceKinds.Tenant, AuthorizationActions.Update))
            return false;
        if (proof.OwnerType == ExternalApiKeyOwnerType.InstanceAdmin && proof.TenantId is null)
            return await MachineMutationAuthoritySql.AuthorizeInstanceFooterPatchAsync(
                dbContext, proof, tenantId, groupId, linkId, httpContext, clock, cancellationToken)
                && SnapshotPermits(proof, ResourceKinds.Tenant, AuthorizationActions.Update);
        if (proof.OwnerType == ExternalApiKeyOwnerType.User)
        {
            if (!await AuthorizeUserFooterAsync(tenantId, cancellationToken))
                return false;
        }
        else if (proof.OwnerType != ExternalApiKeyOwnerType.Tenant
            || !await FenceTenantKeyAsync(proof, tenantId, ResourceKinds.Tenant, AuthorizationActions.Update, cancellationToken))
            return false;
        if (!await MachineMutationAuthoritySql.FenceFooterTargetsAsync(dbContext, tenantId, groupId, linkId, cancellationToken))
            return false;
        return await KeyStillUsableAsync(proof, tenantId, ResourceKinds.Tenant, AuthorizationActions.Update, cancellationToken);
    }

    private async Task<bool> AuthorizeUserFooterAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!HasRuntimeTransaction(tenantId) || proofs.CurrentProof is not { OwnerType: ExternalApiKeyOwnerType.User } proof
            || !await FenceTenantKeyAsync(proof, tenantId, ResourceKinds.Tenant, AuthorizationActions.Update, cancellationToken))
            return false;
        var authority = new ExternalApiKeyIssuanceAuthority(dbContext, httpContext);
        return (await authority.IsInstanceAdministratorForCommitAsync(proof.OwnerId, cancellationToken)
                || await authority.IsTenantAdministratorForCommitAsync(proof.OwnerId, tenantId, cancellationToken))
            && await KeyStillUsableAsync(proof, tenantId, ResourceKinds.Tenant, AuthorizationActions.Update, cancellationToken);
    }

    public async Task<LocationRoom?> GetAuthorizedRoomForUpdateAsync(
        Guid tenantId, Guid roomId, Guid? destinationLocationId, CancellationToken cancellationToken)
    {
        if (roomId == Guid.Empty || destinationLocationId == Guid.Empty || !HasRuntimeTransaction(tenantId)
            || proofs.CurrentProof is not { } proof || !SnapshotPermits(proof, ResourceKinds.LocationRoom, AuthorizationActions.Update))
            return null;
        if (proof.OwnerType == ExternalApiKeyOwnerType.InstanceAdmin && proof.TenantId is null)
        {
            if (!await MachineRoomMutationAuthoritySql.AuthorizeAsync(
                    dbContext, proof, tenantId, roomId, destinationLocationId, httpContext, clock, cancellationToken))
                return null;
        }
        else
        {
            if (proof.OwnerType is not (ExternalApiKeyOwnerType.User or ExternalApiKeyOwnerType.Tenant)
                || !await FenceTenantKeyAsync(proof, tenantId, ResourceKinds.LocationRoom, AuthorizationActions.Update, cancellationToken))
                return null;
            if (proof.OwnerType == ExternalApiKeyOwnerType.User)
            {
                var authority = new ExternalApiKeyIssuanceAuthority(dbContext, httpContext);
                if (!await authority.IsInstanceAdministratorForCommitAsync(proof.OwnerId, cancellationToken)
                    && !await authority.IsTenantAdministratorForCommitAsync(proof.OwnerId, tenantId, cancellationToken))
                    return null;
            }
        }
        var room = await new LocationRoomRepository(dbContext).GetForMutationAsync(roomId, destinationLocationId, cancellationToken);
        if (room is null || !SnapshotPermits(proof, ResourceKinds.LocationRoom, AuthorizationActions.Update))
            return null;
        if (proof.TenantId is not null
            && !await KeyStillUsableAsync(proof, tenantId, ResourceKinds.LocationRoom, AuthorizationActions.Update, cancellationToken))
            return null;
        return room;
    }

    private bool HasRuntimeTransaction(Guid tenantId)
    {
        if (tenantId == Guid.Empty || dbContext.IsTenantFilterBypassed || dbContext.TenantFilterTenantId != tenantId)
            return false;
        if (dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Machine mutation authority requires the owning Runtime Serializable transaction.");
        return true;
    }

    private async Task<bool> FenceTenantKeyAsync(
        PersistedApiKeyAuthenticationProof proof, Guid tenantId, string resource, string action, CancellationToken token)
    {
        if (proof.RowId == Guid.Empty || proof.TenantId != tenantId || !SnapshotPermits(proof, resource, action))
            return false;
        if (proof.OwnerType == ExternalApiKeyOwnerType.User)
        {
            if (!await new ExternalApiKeyIssuanceAuthority(dbContext, httpContext)
                    .IsAuthorizedForCommitAsync(proof.OwnerId, tenantId, proof.OwnerType, proof.OwnerId, token))
                return false;
        }
        else if (proof.OwnerType != ExternalApiKeyOwnerType.Tenant || proof.OwnerId != tenantId)
            return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(dbContext, tenantId, token);
        int? status = await dbContext.Tenants.AsNoTracking().Where(row => row.Id == tenantId)
            .Select(row => (int?)row.TenantStatusId).SingleOrDefaultAsync(token);
        if (status is not { } statusId)
            return false;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(dbContext, [statusId], token);
        if (!await dbContext.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, token))
            return false;
        // Nullable-tenant keys deliberately use the same exact-tenant predicate as Runtime EF/RLS.
        await dbContext.ExternalApiKeys.Where(row => row.Id == proof.RowId && row.TenantId == tenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Id, row => row.Id), token);
        return await KeyStillUsableAsync(proof, tenantId, resource, action, token);
    }

    private async Task<bool> KeyStillUsableAsync(
        PersistedApiKeyAuthenticationProof proof, Guid tenantId, string resource, string action, CancellationToken token)
    {
        var key = await dbContext.ExternalApiKeys.AsNoTracking()
            .Where(row => row.Id == proof.RowId && row.TenantId == tenantId)
            .Select(row => new
            {
                row.KeyId,
                row.OwnerId,
                row.ExternalApiKeyOwnerTypeId,
                row.ExternalApiKeyStatusId,
                row.ExpiresAt,
                row.Scopes
            }).SingleOrDefaultAsync(token);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        return key is not null && key.KeyId == proof.KeyId && key.OwnerId == proof.OwnerId
            && key.ExternalApiKeyOwnerTypeId == (int)proof.OwnerType
            && IsUsable(key.ExternalApiKeyStatusId) && (key.ExpiresAt is null || key.ExpiresAt > now)
            && SnapshotPermits(proof, resource, action)
            && MachineScopeMapping.ScopesPermit(key.Scopes.Split([' ', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), resource, action);
    }

    private bool SnapshotPermits(PersistedApiKeyAuthenticationProof proof, string resource, string action) =>
        IsUsable(proof.StatusId) && (proof.ExpiresAt is null || proof.ExpiresAt > clock.GetUtcNow().UtcDateTime)
        && MachineScopeMapping.ScopesPermit(proof.Scopes, resource, action);

    private static bool IsUsable(int status) =>
        status is (int)ExternalApiKeyStatusEnum.Active or (int)ExternalApiKeyStatusEnum.PendingRotation;
}
