using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>Reads acting actors without converting a self-read into administrator admission.</summary>
public sealed class HumanActorContextReader(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IPolicySourceSetFence> resolveSourceSet,
    Func<ISerializableAttemptOwner> resolveAttempt,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<IAiActorContextRepository> resolveActors) : IHumanActorContextReader
{
    public async Task<IReadOnlyList<Actor>> ReadAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (machine.IsMachineCaller || tenantId == Guid.Empty || userId == Guid.Empty
            || tenantContext.TenantId != tenantId)
            throw new UnauthorizedAccessException("Actor contexts require the original human and selected tenant.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An original resolved human is required.");
        var request = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("An original authenticated request is required.");
        var principal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(principal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime is required.");
        if (subject.PrincipalId != userId || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("Actor contexts cannot be read for another account.");

        void RequireCustody(DateTimeOffset now)
        {
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || tenantContext.TenantId != tenantId || !lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("Original actor-context request custody changed.");
        }

        void RequirePrincipal(HumanPrincipalSourceFacts facts)
        {
            RequireCustody(clock.GetUtcNow());
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            if (facts.PrincipalId != userId || facts.SelectedTenantId != tenantId)
                throw new UnauthorizedAccessException("Actor-context sources belong to another owner.");
        }

        var source = resolvePrincipal();
        var original = await source.ObserveAsync(cancellationToken);
        RequirePrincipal(original);
        var sourceSet = resolveSourceSet();
        return await resolveAttempt().ExecuteAsync(async token =>
        {
            RequireCustody(clock.GetUtcNow());
            var exclusion = await sourceSet.AcquireAsync(token);
            var held = await source.AcquireAsync(exclusion, token);
            RequireUnchanged(held.Facts);
            resolveFinalizer().Register(async finalToken =>
            {
                await exclusion.ValidateAsync(finalToken);
                RequireUnchanged(await held.ValidateAsync(finalToken));
            }, RequireCustody);
            return await resolveActors().GetAvailableActorsAsync(token);
        }, cancellationToken);

        void RequireUnchanged(HumanPrincipalSourceFacts current)
        {
            RequirePrincipal(current);
            if (!Same(original.Users, current.Users) || !Same(original.Bindings, current.Bindings)
                || !Same(original.PlatformGrants, current.PlatformGrants) || !Same(original.Roles, current.Roles)
                || !Same(original.TenantMemberships, current.TenantMemberships) || !Same(original.TenantGrants, current.TenantGrants)
                || !Same(original.Tenants, current.Tenants) || !Same(original.Organizations, current.Organizations)
                || !Same(original.Groups, current.Groups) || !Same(original.Permissions, current.Permissions)
                || original.PermissionMappingsExist != current.PermissionMappingsExist
                || !original.EventCreateOrganizations.SequenceEqual(current.EventCreateOrganizations)
                || !original.EventCreateGroups.SequenceEqual(current.EventCreateGroups)
                || !original.EventFinanceOrganizations.SequenceEqual(current.EventFinanceOrganizations)
                || !original.EventFinanceGroups.SequenceEqual(current.EventFinanceGroups)
                || original.LocalSession is { } originalLocal && (current.LocalSession is not { } currentLocal
                    || originalLocal.CredentialState != currentLocal.CredentialState
                    || originalLocal.EmailDeliveryEnabled != currentLocal.EmailDeliveryEnabled
                    || !Same(originalLocal.Accounts, currentLocal.Accounts)
                    || !Same(originalLocal.Operations, currentLocal.Operations)
                    || !Same(originalLocal.PersonalActors, currentLocal.PersonalActors)))
                throw new UnauthorizedAccessException("Original actor-context authority contributors changed.");
        }
    }

    private static bool Same<T>(IEnumerable<T> expected, IEnumerable<T> actual) where T : notnull
    {
        var counts = expected.GroupBy(row => row).ToDictionary(group => group.Key, group => group.Count());
        var groups = actual.GroupBy(row => row).ToArray();
        return counts.Count == groups.Length
            && groups.All(group => counts.TryGetValue(group.Key, out int count) && count == group.Count());
    }
}
