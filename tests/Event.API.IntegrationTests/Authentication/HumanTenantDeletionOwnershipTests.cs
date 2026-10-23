using System.Data.Common;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Tenants.Handlers.Commands;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Validated OIDC credentials exercise the ordinary physical owner, not the co-located SQLite
/// Identity transaction exception. The real repository, source fences and finalizer remain active.
/// Only the external PDP is substituted; late changes run inside the actual deletion transaction.
/// </summary>
[NotInParallel]
public sealed class HumanTenantDeletionOwnershipTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("bootstrap")]
    [Arguments("grant")]
    [Arguments("binding")]
    [Arguments("route")]
    [Arguments("credential")]
    [Arguments("principal")]
    [Arguments("request")]
    [Arguments("commit_failure")]
    [Arguments("deny")]
    [Arguments("unavailable")]
    [Arguments("local")]
    public async Task TenantDeletionKeepsDiscoveryCleanupAndOriginalAdmissionInOnePhysicalOwner(string change)
    {
        var transactions = new DeletionTransactions();
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, persistenceInterceptor: transactions);
        var clock = new DeletionClock();
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        Guid userId = Guid.CreateVersion7();
        Guid providerSubject = Guid.CreateVersion7();
        Guid bindingId = Guid.CreateVersion7();
        Guid grantId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid survivingTenantId = Guid.CreateVersion7();
        string email = $"tenant-delete-{userId:N}@example.test";
        await using (var seed = native.CreateDatabase())
        {
            var user = new User
            {
                Id = userId,
                Pii = new UserPii { Email = email, FirstName = "Deletion", LastName = "Owner" },
                EmailVerified = true,
                CreatedAt = clock.Now.UtcDateTime
            };
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = bindingId,
                UserId = userId,
                User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                    native.ExternalIssuer, providerSubject.ToString("D")).Value,
                CreatedAt = clock.Now.UtcDateTime
            });
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = grantId,
                UserId = userId,
                User = user,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = clock.Now.UtcDateTime
            });
            foreach (Guid id in new[] { tenantId, survivingTenantId })
                seed.Tenants.Add(new Tenant
                {
                    Id = id,
                    FullName = "Original deletion target",
                    Slug = $"delete-{id:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!,
                    CreatedAt = clock.Now.UtcDateTime
                });
            var routeSetting = await seed.SystemSettings.SingleOrDefaultAsync(
                row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
            if (routeSetting is null)
                seed.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                    Value = "\"cerbos\""
                });
            else
                routeSetting.Value = "\"cerbos\"";
            await seed.SaveChangesAsync();
            SeedDiscovery(seed, tenantId, change != "bootstrap", clock.Now.UtcDateTime);
            SeedDiscovery(seed, survivingTenantId, true, clock.Now.UtcDateTime);
            await seed.SaveChangesAsync();
        }

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext { RequestServices = services };
        accessor.HttpContext = http;
        try
        {
            http.Request.Headers.Authorization = "Bearer " +
                native.CreateExternalProviderToken(providerSubject, email, true);
            var authentication = await http.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            http.User = authentication.Principal!;
            await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
            var context = services.GetRequiredService<ExploreDbContext>();
            await Assert.That(context.TenantFilterTenantId).IsNull();
            var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(http.User, CancellationToken.None))!;
            await Assert.That(lifetime.LocalSession).IsNull();
            var principal = new ReservationBeforePrincipal(
                services.GetRequiredService<IHumanPrincipalSourceFence>(), context, tenantId);
            var exclusion = new ReservationBeforeSources(
                services.GetRequiredService<IPolicySourceSetFence>(), context, tenantId);
            var transport = new DeletionPolicyTransport(context, change);
            var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://tenant-delete-pdp.test:3593" });
            var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
                services, transport.Client, transport.Factory, options);
            bool deletedBodyObserved = false;
            var owner = new AfterDeletion(new EfCoreUnitOfWork(context), async token =>
            {
                deletedBodyObserved = true;
                await Assert.That(await context.Tenants.AnyAsync(row => row.Id == tenantId, token)).IsFalse();
                await AssertDiscoveryAsync(context, tenantId, false, false, token);
                await Assert.That(context.Database.CurrentTransaction!.GetDbTransaction())
                    .IsSameReferenceAs(principal.Transaction!);
                if (change == "grant")
                    await context.PlatformUserRoles.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
                if (change == "binding")
                    await context.UserExternalLogins.Where(row => row.Id == bindingId).ExecuteDeleteAsync(token);
                if (change == "route")
                {
                    var setting = await context.SystemSettings.SingleOrDefaultAsync(
                        row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider, token);
                    if (setting is null)
                        context.SystemSettings.Add(new SystemSetting
                        {
                            SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                            Value = "\"local\""
                        });
                    else
                        setting.Value = "\"local\"";
                }
                if (change == "principal")
                    http.User = new System.Security.Claims.ClaimsPrincipal(http.User);
                if (change == "request")
                    accessor.HttpContext = new DefaultHttpContext { RequestServices = services, User = http.User };
                // Registered after the original admission, this blocking read finishes last.
                // Credential expiry must still be checked after it, never at an earlier sample.
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.Tenants.AsNoTracking().SingleAsync(row => row.Id == survivingTenantId, finalToken);
                    if (change == "credential")
                        clock.Now = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                }, _ => { });
                await context.SaveChangesAsync(token);
            });
            var preparation = new HumanTenantMutationPreparation(
                services.GetRequiredService<IHumanCommitScopePreparation>(), accessor,
                services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
                services.GetRequiredService<ITenantContext>(), clock, cerbos,
                Options.Create(new AuthorizationProviderDeploymentOptions { Provider = change == "local" ? "local" : null }),
                options, () => principal, () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
                () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
                () => exclusion,
                () => services.GetRequiredService<IHumanActionCommitFinalizer>(), () => owner,
                () => services.GetRequiredService<IEventDiscoveryEnrollmentGate>());
            var repository = new TenantRepository(context, preparation);
            var handler = new DeleteTenantCommandHandler(repository,
                services.GetRequiredService<ITenantSlugCache>(), preparation);
            transactions.Arm(context, change == "commit_failure");
            var deletion = handler.ExecuteAsync(new DeleteTenantCommand { Id = tenantId });
            bool committed = change is "unchanged" or "bootstrap";
            if (committed)
                await Assert.That(await deletion).IsTrue();
            else if (change is "deny" or "local")
                await Assert.ThrowsAsync<AuthorizationException>(() => deletion);
            else if (change == "unavailable")
                await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => deletion);
            else if (change == "commit_failure")
                await Assert.ThrowsAsync<InvalidOperationException>(() => deletion);
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => deletion);

            bool entered = change is not ("deny" or "unavailable" or "local");
            await Assert.That(deletedBodyObserved).IsEqualTo(entered);
            await Assert.That(principal.AcquiredAfterReservation).IsEqualTo(entered);
            await Assert.That(exclusion.AcquiredAfterReservation).IsEqualTo(entered);
            await Assert.That(transactions.Started).IsEqualTo(entered ? 1 : 0);
            await Assert.That(transactions.CommitReached).IsEqualTo(committed || change == "commit_failure");
            await Assert.That(transport.Requests.Count).IsEqualTo(change == "local" ? 0 : 1);
            if (change != "local")
            {
                var sent = transport.Requests.Single();
                await Assert.That(sent.Resources.Single().Resource.Id).IsEqualTo(tenantId.ToString("D"));
                await Assert.That(sent.Resources.Single().Resource.Kind).IsEqualTo(ResourceKinds.Tenant);
                await Assert.That(sent.Resources.Single().Actions.Single()).IsEqualTo(AuthorizationActions.Delete);
            }
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await using var observer = native.CreateDatabase();
            await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == tenantId)).IsEqualTo(!committed);
            await AssertDiscoveryAsync(observer, tenantId, !committed, change != "bootstrap");
            await AssertDiscoveryAsync(observer, survivingTenantId, true, true);
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grantId)).IsTrue();
            await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.Id == bindingId)).IsTrue();
        }
        finally { accessor.HttpContext = null; }
    }

    [Test]
    public async Task ExternalLocalIdentityCannotBorrowTheTenantDeletionOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: IdentityDatabaseTopology.External);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext { RequestServices = services };
        string credential = await fixture.SignInAsync();
        accessor.HttpContext = http;
        try
        {
            http.Request.Headers.Authorization = "Bearer " + credential;
            var authentication = await http.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            http.User = authentication.Principal!;
            var context = services.GetRequiredService<ExploreDbContext>();
            var handler = ActivatorUtilities.CreateInstance<DeleteTenantCommandHandler>(services);
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                handler.ExecuteAsync(new DeleteTenantCommand { Id = PlatformDefaults.DefaultTenantId }));
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await using var observer = fixture.Native.CreateDatabase();
            await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == PlatformDefaults.DefaultTenantId)).IsTrue();
        }
        finally { accessor.HttpContext = null; }
    }

    private static void SeedDiscovery(ExploreDbContext context, Guid tenantId, bool snapshot, DateTime now)
    {
        var member = EventDiscoveryIdentity.Create(tenantId, EventDiscoverySourceKind.AtprotoRecord, "member");
        var primary = EventDiscoveryIdentity.Create(tenantId, EventDiscoverySourceKind.AtprotoRecord, "primary");
        context.AddRange(member, primary, new EventDiscoveryAlias
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MemberIdentityId = member.Id,
            PrimaryIdentityId = primary.Id,
            RelationshipRevision = 1,
            ReviewerId = Guid.CreateVersion7(),
            ReasonCode = "same_event",
            ReviewedAtUtc = now
        });
        if (snapshot)
            context.AddRange(new EventDiscoverySnapshotReservation { TenantId = tenantId },
                EventDiscoverySnapshot.Create(Guid.CreateVersion7(), tenantId, new string('a', 64), 0, 0,
                    now, now.AddMinutes(5), false, true, true,
                    [EventDiscoverySnapshotItem.Create(EventDiscoverySourceKind.AtprotoRecord, Guid.CreateVersion7(),
                        EventDiscoveryCanonicalKind.ReviewedIdentity, primary.Id, null)], new()));
    }

    private static async Task AssertDiscoveryAsync(ExploreDbContext context, Guid tenantId, bool retained,
        bool snapshot, CancellationToken token = default)
    {
        await Assert.That(await context.Set<EventDiscoveryIdentity>().IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId, token)).IsEqualTo(retained ? 2 : 0);
        await Assert.That(await context.Set<EventDiscoveryAlias>().IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId, token)).IsEqualTo(retained ? 1 : 0);
        await Assert.That(await context.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId, token)).IsEqualTo(retained ? 1 : 0);
        await Assert.That(await context.Set<EventDiscoverySnapshot>().IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId, token)).IsEqualTo(retained && snapshot ? 1 : 0);
        await Assert.That(await context.Set<EventDiscoverySnapshotItem>().IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId, token)).IsEqualTo(retained && snapshot ? 1 : 0);
        await Assert.That(await context.Set<EventDiscoverySnapshotReservation>().IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId, token)).IsEqualTo(retained && snapshot ? 1 : 0);
    }

    private sealed class ReservationBeforePrincipal(IHumanPrincipalSourceFence inner, ExploreDbContext context,
        Guid tenantId) : IHumanPrincipalSourceFence
    {
        internal bool AcquiredAfterReservation { get; private set; }
        internal DbTransaction? Transaction { get; private set; }
        public Task<HumanPrincipalSourceFacts> ObserveAsync(CancellationToken cancellationToken = default) =>
            inner.ObserveAsync(cancellationToken);
        public async Task<IHumanPrincipalSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
            CancellationToken cancellationToken = default)
        {
            Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            await Assert.That(await context.Set<EventDiscoverySnapshotReservation>().IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == tenantId, cancellationToken)).IsTrue();
            AcquiredAfterReservation = true;
            return await inner.AcquireAsync(sourceSet, cancellationToken);
        }
    }

    private sealed class ReservationBeforeSources(IPolicySourceSetFence inner, ExploreDbContext context,
        Guid tenantId) : IPolicySourceSetFence
    {
        internal bool AcquiredAfterReservation { get; private set; }
        public async Task<IPolicySourceSetLease> AcquireAsync(CancellationToken cancellationToken = default)
        {
            await Assert.That(await context.Set<EventDiscoverySnapshotReservation>().IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == tenantId, cancellationToken)).IsTrue();
            AcquiredAfterReservation = true;
            return await inner.AcquireAsync(cancellationToken);
        }
    }

    private sealed class AfterDeletion(ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody)
        : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(async token =>
            {
                T result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);
    }

    private sealed class DeletionTransactions : DbTransactionInterceptor
    {
        private ExploreDbContext? _target;
        private bool _failCommit;
        internal int Started { get; private set; }
        internal bool CommitReached { get; private set; }
        internal void Arm(ExploreDbContext target, bool failCommit) { _target = target; _failCommit = failCommit; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target)) Started++;
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target))
            {
                CommitReached = true;
                if (_failCommit) throw new InvalidOperationException("Abort the original tenant deletion commit.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DeletionClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class DeletionPolicyTransport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal DeletionPolicyTransport(ExploreDbContext owner, string change)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("Tenant PDP evaluation must precede the original transaction.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    if (change == "unavailable") throw new RpcException(new Status(StatusCode.Unavailable, "Selected PDP unavailable"));
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), change == "deny" ? Effect.Deny : Effect.Allow);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
