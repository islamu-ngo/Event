using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPublicEventCalendarRepository
{
    private async Task<PublicProvenance?> AcquireProvenanceAsync(
        ModerationPublicEventCalendarRequest request, ModerationHostBinding discoveredHost,
        Guid publicationId, CancellationToken token)
    {
        var humans = new HashSet<Guid>();
        var publication = await context.ModerationPolicyPublications.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == publicationId, token);
        if (publication is not null) humans.Add(publication.PrincipalId);
        var candidate = await context.ModerationPolicyPublicationCandidates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == publicationId, token);
        if (candidate is not null) humans.Add(candidate.PreparedByHumanId);
        var governance = await context.Set<ModerationConstitutionSource>().AsNoTracking().Include(row => row.Reviewers)
            .Where(row => row.HostBindingId == discoveredHost.Id && row.TenantId == request.TenantId
                && row.PublicationId == publicationId && row.OriginalEffectId == null
                && !context.Set<ModerationConstitutionSource>().Any(newer => newer.HostBindingId == row.HostBindingId
                    && newer.TenantId == row.TenantId && newer.PublicationId == row.PublicationId
                    && newer.ProfileId == row.ProfileId && newer.ModeCode == row.ModeCode
                    && newer.OriginalEffectId == null && newer.WriterGeneration > row.WriterGeneration)).ToArrayAsync(token);
        foreach (var source in governance)
        {
            humans.UnionWith(new[] { source.RecordedByHumanId, source.CoordinatorHumanId, source.FallbackHumanId });
            foreach (var reviewer in source.Reviewers)
                humans.UnionWith(new[] { reviewer.ReviewerHumanId, reviewer.ControllerHumanId });
        }
        var governanceIds = governance.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .Where(row => governanceIds.Contains(row.SourceId)).ToArrayAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } corrector) humans.Add(corrector);
        }
        var age = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .Where(row => row.HostBindingId == discoveredHost.Id && row.TenantId == request.TenantId
                && row.PublicationId == publicationId
                && !context.Set<ModerationChildAgePolicySource>().Any(newer => newer.HostBindingId == row.HostBindingId
                    && newer.TenantId == row.TenantId && newer.PublicationId == row.PublicationId
                    && newer.ProfileId == row.ProfileId && newer.ModeCode == row.ModeCode
                    && newer.WriterGeneration > row.WriterGeneration)).ToArrayAsync(token);
        foreach (var source in age)
            humans.UnionWith(new[] { source.RecordedByHumanId, source.Ownership.ReviewerHumanId,
                source.Ownership.ReviewerControllerHumanId, source.Ownership.CollectionOwnerHumanId,
                source.Ownership.CollectionControllerHumanId, source.Ownership.GovernanceControllerHumanId });
        var ageIds = age.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking()
            .Where(row => ageIds.Contains(row.SourceId)).ToArrayAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } corrector) humans.Add(corrector);
        }
        var taxonomies = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .Where(row => row.HostBindingId == discoveredHost.Id && row.TenantId == request.TenantId
                && row.PublicationId == publicationId
                && !context.Set<ModerationClassificationTaxonomySource>().Any(newer => newer.HostBindingId == row.HostBindingId
                    && newer.TenantId == row.TenantId && newer.TaxonomyId == row.TaxonomyId
                    && newer.WriterGeneration > row.WriterGeneration)).ToArrayAsync(token);
        foreach (var source in taxonomies)
            humans.UnionWith(new[] { source.OwnerHumanId, source.OwnerControllerHumanId,
                source.ReviewerHumanId, source.ReviewerControllerHumanId });
        var taxonomyIds = taxonomies.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
            .Where(row => taxonomyIds.Contains(row.SourceId)).ToArrayAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.WithdrawnByHumanId is { } corrector) humans.Add(corrector);
        }
        var materials = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .Where(row => row.HostBindingId == discoveredHost.Id && row.TenantId == request.TenantId
                && row.EventId == request.EventId
                && !context.Set<ModerationClassificationMaterialSource>().Any(newer => newer.HostBindingId == row.HostBindingId
                    && newer.TenantId == row.TenantId && newer.EventId == row.EventId
                    && newer.WriterGeneration > row.WriterGeneration)).ToArrayAsync(token);
        foreach (var source in materials)
            humans.UnionWith(new[] { source.OwnerHumanId, source.OwnerControllerHumanId,
                source.ReviewerHumanId, source.ReviewerControllerHumanId });
        var materialIds = materials.Select(row => row.Id).ToArray();
        foreach (var review in await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
            .Where(row => materialIds.Contains(row.SourceId)).ToArrayAsync(token))
        {
            humans.Add(review.ReviewerHumanId);
            if (review.EndedByHumanId is { } corrector) humans.Add(corrector);
        }
        var controls = await context.Set<ModerationClassificationControlSelection>().AsNoTracking()
            .Where(row => row.HostBindingId == discoveredHost.Id && row.TenantId == request.TenantId
                && row.EventId == request.EventId
                && !context.Set<ModerationClassificationControlSelection>().Any(newer => newer.HostBindingId == row.HostBindingId
                    && newer.TenantId == row.TenantId && newer.EventId == row.EventId
                    && newer.WriterGeneration > row.WriterGeneration)).ToArrayAsync(token);
        foreach (var control in controls)
            humans.UnionWith(new[] { control.AuthoredByHumanId, control.OwnerHumanId,
                control.OwnerControllerHumanId, control.ReviewerHumanId, control.ReviewerControllerHumanId });
        var controlIds = controls.Select(row => row.Id).ToArray();
        foreach (var adoption in await context.Set<ModerationClassificationEventTargetAdoption>().AsNoTracking()
            .Where(row => controlIds.Contains(row.SelectionId)).ToArrayAsync(token))
        {
            humans.Add(adoption.AdoptedByHumanId);
            if (adoption.RevokedByHumanId is { } corrector) humans.Add(corrector);
        }
        foreach (var generation in await context.ModerationEventGenerations.AsNoTracking()
            .Where(row => row.HostBindingId == discoveredHost.Id && row.TenantId == request.TenantId
                && row.EventId == request.EventId
                && !context.ModerationEventGenerations.Any(newer => newer.HostBindingId == row.HostBindingId
                    && newer.TenantId == row.TenantId && newer.EventId == row.EventId && newer.Revision > row.Revision)).ToArrayAsync(token))
        {
            humans.Add(generation.ObservedByHumanId);
            if (generation.SubmittedByHumanId is { } publisher) humans.Add(publisher);
        }
        humans.UnionWith(await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
            source => ModerationClassificationMaterialSourceOwner.InterestedHumansAsync(
                source, request.EventId, request.TenantId, clock.GetUtcNow(), token), token));
        var speakers = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context, async source =>
        {
            var actors = await source.Set<EventSessionSpeaker>().AsNoTracking()
                .Where(row => row.TenantId == request.TenantId && row.EventSession.EventId == request.EventId)
                .Select(row => row.ActorId).Distinct().ToArrayAsync(token);
            var users = await source.Actors.AsNoTracking().Where(row => actors.Contains(row.Id))
                .Select(row => row.UserId).ToArrayAsync(token);
            return (Actors: actors, Humans: users.OfType<Guid>().ToArray());
        }, token);
        humans.UnionWith(speakers.Humans);
        var materialGenerations = await EventAuthorityProviderOperations.ReadMaterialGenerationPlansAsync(
            context, discoveredHost.Id, request.TenantId, token, eventId: request.EventId);
        var calendarParents = await EventAuthorityProviderOperations.ReadPlanAsync(
            context, request.TenantId, [], [request.EventId], token);
        var humanIds = humans.Order().ToArray();
        var materialAuthority = await EventAuthorityProviderOperations.ReadPlanAsync(
            context, request.TenantId, humanIds, [request.EventId], token);
        // Same global order as native writers: identity authority, humans, logins,
        // grants/memberships/roles, host, tenant/status, qualification epoch, sources.
        foreach (var id in humanIds)
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
        foreach (var id in await context.UserExternalLogins.AsNoTracking().Where(row => humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token))
            await RelationalEntityRowFence.AcquireGlobalAsync<UserExternalLogin>(context, id, token);
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in platform)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        var members = await context.TenantUsers.AsNoTracking()
            .Where(row => row.TenantId == request.TenantId && humanIds.Contains(row.UserId))
            .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
        foreach (var id in members)
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, request.TenantId, row => row.Id, id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row => members.Contains(row.TenantUserId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToArrayAsync(token);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, request.TenantId, row => row.Id, grant.Id, token);
        foreach (var id in platform.Select(row => row.RoleId).Concat(grants.Select(row => row.RoleId))
            .Concat(materialAuthority.Assignments.Select(row => row.RoleId)).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [id], token);
        await EventAuthoritySnapshotService.AcquirePermissionSourcesAsync(context,
            materialAuthority.Assignments.Select(row => row.RoleId).Distinct().Order().ToArray(), token);
        foreach (var human in humanIds)
            if (await privacy.IsSubjectFencedAsync(human, token)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, discoveredHost.Id, token);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || host.Id != discoveredHost.Id || host.TrustDomainId != discoveredHost.TrustDomainId
            || host.InstanceId != discoveredHost.InstanceId || host.SolutionId != discoveredHost.SolutionId
            || host.MaintenanceServicePrincipalId != discoveredHost.MaintenanceServicePrincipalId) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, token);
        foreach (var actorId in materialGenerations.Values.Concat(calendarParents.Parents)
            .SelectMany(row => new Guid?[] { row.ActorId, row.OrganizerActorId }).OfType<Guid>()
            .Concat(speakers.Actors).Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, request.TenantId, token);
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == request.TenantId, token);
        if (tenant is null) return null;
        await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], token);
        var discoveredEpoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == request.TenantId, token);
        if (discoveredEpoch is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, discoveredEpoch.Id, token);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleOrDefaultAsync(row => row.Id == discoveredEpoch.Id, token);
        if (epoch is null || epoch.Revision < 1) return null;
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var provenance = new PublicProvenance(host, epoch, instance, AuthorityScope.Tenant(instance, request.TenantId),
            humanIds, await AdministratorsAsync(humanIds, request.TenantId, token), materialGenerations, materialAuthority);
        return await ProvenanceCurrentAsync(provenance, token) ? provenance : null;
    }

    private async Task<Guid[]> AdministratorsAsync(Guid[] humans, Guid tenant, CancellationToken token)
    {
        var platform = await context.PlatformUserRoles.AsNoTracking()
            .Where(row => humans.Contains(row.UserId) && !row.User.IsDeleted
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
            .Select(row => row.UserId).ToArrayAsync(token);
        var local = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenant
            && humans.Contains(row.TenantUser.UserId) && row.RevokedAt == null
            && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
            && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted)
            .Select(row => row.TenantUser.UserId).ToArrayAsync(token);
        return platform.Concat(local).Distinct().Order().ToArray();
    }

    private async Task<bool> ProvenanceCurrentAsync(PublicProvenance provenance, CancellationToken token)
    {
        var humans = provenance.FencedHumans.ToArray();
        if (!await context.ModerationHostBindings.AsNoTracking().AnyAsync(row => row.Id == provenance.Binding.Id
                && row.TrustDomainId == provenance.Binding.TrustDomainId && row.InstanceId == provenance.Binding.InstanceId
                && row.SolutionId == provenance.Binding.SolutionId
                && row.MaintenanceServicePrincipalId == provenance.Binding.MaintenanceServicePrincipalId, token)
            || !await context.ServicePrincipals.AsNoTracking().AnyAsync(row =>
                row.Id == provenance.Binding.MaintenanceServicePrincipalId && !row.IsDeleted, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == provenance.Epoch.Id
                && row.HostBindingId == provenance.Binding.Id && row.TenantId == provenance.Tenant.Id
                && row.Revision == provenance.Epoch.Revision, token)
            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == provenance.Tenant.Id
                && row.TenantStatusId == (int)TenantStatusEnum.Active && row.TenantStatus.IsActiveState, token)
            || await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Length
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token)
            || !(await AdministratorsAsync(humans, provenance.Tenant.Id, token)).SequenceEqual(provenance.Administrators))
            return false;
        foreach (var human in humans)
            if (await privacy.IsSubjectFencedAsync(human, token)) return false;
        return true;
    }

    private sealed record PublicProvenance(
        ModerationHostBinding Binding, ModerationQualificationEpoch Epoch,
        AuthorityScope Instance, AuthorityScope Tenant,
        IReadOnlyList<Guid> FencedHumans, IReadOnlyList<Guid> Administrators,
        IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.Ownership> MaterialGenerations,
        EventAuthorityProviderOperations.AuthorityPlan? MaterialAuthority) : IModerationSourceProvenance;
}
