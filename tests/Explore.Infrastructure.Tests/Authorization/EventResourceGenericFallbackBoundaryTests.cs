using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed class EventResourceGenericFallbackBoundaryTests
{
    private static readonly Guid TenantId = new("019d2f35-47d8-7b2d-96d3-570cc42f8c11");
    private static readonly Guid ResourceId = new("019d2f35-47d8-7b2d-96d3-570cc42f8c12");
    private static readonly string[] Actions =
    [
        "view", "view-management", "create", "update", "publish", "unpublish", "archive", "delete",
        "access", "download", "view-audit", "export", "moderate"
    ];

    [Test]
    public async Task ResourceDenialsDoNotDependOnGenericAdminAuthorityAvailability()
    {
        var admin = Substitute.For<IAdminContext>();
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("Generic admin authority unavailable")));
        var provider = Create(admin);
        var requests = Actions.Select(action => Resource(action)).ToArray();

        foreach (var request in requests)
            await Assert.That((await provider.AuthorizeAsync(request)).IsAllowed).IsFalse();
        var batch = await provider.AuthorizeBatchAsync(requests);
        await Assert.That(batch.Count).IsEqualTo(requests.Length);
        await Assert.That(batch.All(decision => !decision.IsAllowed)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GenericAdminAndSafeModeCannotAuthorizeResourcesOrForgedFacts(bool safeMode)
    {
        var admin = Substitute.For<IAdminContext>();
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        var provider = Create(admin);
        if (safeMode) provider.ActivateSafeMode();
        var requests = Actions.Select(action => Resource(action) with
        {
            Facts = new TenantScopedAuthorizationFacts(TenantId),
            Subject = new AuthorizationSubject(ResourceId),
            Tenant = new AuthorizationTenant(TenantId)
        }).ToArray();
        var batch = await provider.AuthorizeBatchAsync(requests);
        for (var index = 0; index < requests.Length; index++)
        {
            var single = await provider.AuthorizeAsync(requests[index]);
            await Assert.That(single.IsAllowed).IsFalse();
            await Assert.That(batch[index]).IsEqualTo(single);
        }
    }

    [Test]
    public async Task MachineScopeCannotSubstituteForResourceAuthority()
    {
        var machine = Substitute.For<IMachinePrincipalAccessor>();
        machine.IsMachineCaller.Returns(true);
        machine.Current.Returns(new ApiKeyPrincipalContext("key-id", TenantId,
            ExternalApiKeyOwnerType.Tenant, TenantId, [ExternalApiKeyScopes.AdminInstance]));
        var provider = Create(Substitute.For<IAdminContext>(), machine);
        var requests = Actions.Select(action => Resource(action) with
        {
            Facts = new TenantScopedAuthorizationFacts(TenantId)
        }).ToArray();
        var batch = await provider.AuthorizeBatchAsync(requests);
        for (var index = 0; index < requests.Length; index++)
        {
            var single = await provider.AuthorizeAsync(requests[index]);
            await Assert.That(single.IsAllowed).IsFalse();
            await Assert.That(batch[index]).IsEqualTo(single);
        }
    }

    [Test]
    public async Task MixedBatchPreservesNonResourceDecisionsAndInputOrder()
    {
        var admin = Substitute.For<IAdminContext>();
        admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        var provider = Create(admin);
        AuthorizationRequest[] requests =
        [
            Resource("update"),
            new(ResourceKinds.Tenant, TenantId.ToString("D"), AuthorizationActions.Update),
            Resource("download"),
            new(ResourceKinds.User, ResourceId.ToString("D"), AuthorizationActions.Update)
        ];
        var batch = await provider.AuthorizeBatchAsync(requests);
        await Assert.That(batch.Select(decision => decision.IsAllowed)).IsEquivalentTo(new[] { false, true, false, true });
        for (var index = 0; index < requests.Length; index++)
            await Assert.That(batch[index]).IsEqualTo(await provider.AuthorizeAsync(requests[index]));
    }

    private static AuthorizationRequest Resource(string action) =>
        new(ResourceKinds.EventResource, ResourceId.ToString("D"), action);

    private static FallbackAuthorizationService Create(IAdminContext admin, IMachinePrincipalAccessor? machine = null)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return new(admin, machine ?? Substitute.For<IMachinePrincipalAccessor>(),
            Substitute.For<IEventAuthoritySnapshotService>(), Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupMemberRepository>(), Substitute.For<IHierarchicalSettingsResolver>(), tenant,
            NullLogger<FallbackAuthorizationService>.Instance);
    }
}
