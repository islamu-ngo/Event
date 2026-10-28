using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Management;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class ManagedProviderProvisioningOrchestrationTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ExistingEmailLessLocalTargetRetainsCredentialsAndRequiresSupportedHumanOwner(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LinkageFixture.Create(topology);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
        var target = identities.Items.Single(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId);
        await Assert.That(target.HasExactBinding).IsTrue();
        await Assert.That(target.Email).IsNull();
        using var snapshot = JsonDocument.Parse(ManagedTenantProvisioningRequestCodec.Serialize(fixture.ManagedRequest()));
        await Assert.That(snapshot.RootElement.GetProperty("administrator").GetProperty("localIdentity")
            .GetProperty("localSubjectId").GetGuid()).IsEqualTo(fixture.Receipt.LocalSubjectId);
        string before = await fixture.CredentialSnapshot();
        if (topology == IdentityDatabaseTopology.External)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Ensure());
            await fixture.AssertNoTenant();
        }
        else
        {
            var result = await fixture.Ensure();
            await Assert.That(result.IsSuccess).IsTrue();
            await fixture.AssertLinked(result.Id!);
        }
        await Assert.That(await fixture.CredentialSnapshot()).IsEqualTo(before);
    }

    [Test]
    public async Task LocalSubjectPossessionCannotAuthorizeHumanLinkage()
    {
        await using var fixture = await LinkageFixture.Create(IdentityDatabaseTopology.Colocated);
        await fixture.UseOrdinaryPrincipal();
        var result = await fixture.Ensure();
        await Assert.That(result.FailureCode).IsEqualTo("authorization_denied");
        await fixture.AssertNoTenant();
    }

    [Test]
    public async Task CurrentAuthenticatedAdministratorCanLinkAfterOriginalIssuerLosesAuthority()
    {
        await using var fixture = await LinkageFixture.Create(IdentityDatabaseTopology.Colocated);
        await fixture.ReplaceAdministrator();
        var result = await fixture.Ensure();
        await Assert.That(result.IsSuccess).IsTrue();
        await fixture.AssertLinked(result.Id!);
    }

    [Test]
    public async Task TargetResetAndPasswordReplacementDoNotInvalidateIdempotentHumanLinkage()
    {
        await using var fixture = await LinkageFixture.Create(IdentityDatabaseTopology.Colocated);
        var first = await fixture.Ensure();
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var reset = fixture.Provider.CreateAsyncScope())
        {
            var current = (await fixture.Store(reset).ReadOperationAsync(fixture.Receipt.OperationId, fixture.Token))!;
            var outcome = await fixture.Store(reset).ResetAsync(new LocalCredentialResetRequest(Guid.CreateVersion7(),
                fixture.Administrator, fixture.Receipt.LocalSubjectId, current.Receipt.OperationId,
                current.OperationConcurrencyStamp, "Operator-approved reset"), fixture.Token);
            await Assert.That(outcome.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        }
        var afterReset = await fixture.Ensure();
        await Assert.That(afterReset.Id!.TenantId).IsEqualTo(first.Id!.TenantId);
        await using (var replace = fixture.Provider.CreateAsyncScope())
        {
            var current = await fixture.Identity(replace).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Receipt.LocalSubjectId, fixture.Token);
            var subject = await fixture.Store(replace).ReadReplacementSubjectAsync(
                current.Id, current.SecurityStamp!, fixture.Token);
            var now = fixture.Lifecycle.Clock.GetUtcNow();
            var outcome = await fixture.Store(replace).ReplaceAsync(new LocalCredentialReplacementRequest(
                new LocalCredentialReplacementAuthority(subject!, now, now.AddMinutes(5)),
                LocalIdentityLifecycleHttpFixture.NewPassword()), fixture.Token);
            await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        }
        string ready = await fixture.CredentialSnapshot();
        var afterReplacement = await fixture.Ensure();
        await Assert.That(afterReplacement.Id!.TenantId).IsEqualTo(first.Id!.TenantId);
        await fixture.AssertLinked(afterReplacement.Id);
        await Assert.That(await fixture.CredentialSnapshot()).IsEqualTo(ready);
    }

    [Test]
    [Arguments("pending")]
    [Arguments("malformed")]
    [Arguments("mismatch")]
    [Arguments("suspended")]
    [Arguments("login")]
    public async Task IncompleteOrMismatchedTargetBindingFailsInsideGenuineHumanOrchestration(string damage)
    {
        await using var fixture = await LinkageFixture.Create(IdentityDatabaseTopology.Colocated);
        await using (var corrupt = fixture.Provider.CreateAsyncScope())
        {
            var identity = fixture.Identity(corrupt);
            if (damage == "suspended")
                await fixture.Application(corrupt).Actors.Where(actor => actor.Id == fixture.Receipt.PersonalActorId)
                    .ExecuteUpdateAsync(update => update.SetProperty(actor => actor.IsSuspended, true), fixture.Token);
            else if (damage == "login")
                await fixture.Application(corrupt).UserExternalLogins.Where(login => login.Id == fixture.Receipt.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(login => login.ProviderKey,
                        Guid.CreateVersion7().ToString("D")), fixture.Token);
            else
            {
                string value = damage == "malformed" ? "{}" : JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                    LocalCredentialStateMetadata.CurrentVersion,
                    damage == "pending" ? LocalCredentialState.ProvisioningPending : LocalCredentialState.ChangeRequired,
                    fixture.Receipt.OperationId, damage == "mismatch" ? Guid.CreateVersion7() : fixture.Receipt.LocalSubjectId));
                await identity.Set<IdentityUserToken<Guid>>().Where(token => token.UserId == fixture.Receipt.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(token => token.Value, value), fixture.Token);
            }
            await Assert.That(await fixture.Store(corrupt).ReadLinkedIdentityAsync(
                fixture.Receipt.LocalSubjectId, fixture.Token)).IsNull();
        }
        var result = await fixture.Ensure();
        await Assert.That(result.FailureCode).IsEqualTo("tenant_local_administrator_unavailable");
        await fixture.AssertNoTenant();
    }

    [Test]
    public async Task CompleteTenantGraphFailureRollsBackOnlyTenantStateAndFreshHumanRetryLinksOnce()
    {
        await using var fixture = await LinkageFixture.Create(IdentityDatabaseTopology.Colocated);
        string before = await fixture.CredentialSnapshot();
        bool grantObserved = false;
        fixture.Probe.BeforeSources = async (services, token) =>
        {
            var owner = services.GetRequiredService<ExploreDbContext>();
            var tenant = await owner.Tenants.AsNoTracking().SingleAsync(row => row.Slug == "managed-tenant", token);
            var member = await owner.TenantUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(
                row => row.TenantId == tenant.Id && row.UserId == fixture.Receipt.LocalSubjectId, token);
            grantObserved = await owner.TenantUserRoleGrants.IgnoreQueryFilters().AnyAsync(
                row => row.TenantId == tenant.Id && row.TenantUserId == member.Id
                    && row.RoleId == (int)RoleEnum.TenantAdmin, token);
            await Assert.That(grantObserved).IsTrue();
            throw new InjectedLinkageFailure();
        };
        fixture.Probe.Armed = true;
        await Assert.ThrowsAsync<InjectedLinkageFailure>(() => fixture.Ensure());
        await Assert.That(grantObserved).IsTrue();
        await fixture.AssertNoTenant();
        await Assert.That(await fixture.CredentialSnapshot()).IsEqualTo(before);
        fixture.Probe.Armed = false;
        var retry = await fixture.Ensure();
        await Assert.That(retry.IsSuccess).IsTrue();
        await fixture.AssertLinked(retry.Id!);
    }

    private sealed class InjectedLinkageFailure : Exception;

    private sealed class LinkageFixture : IAsyncDisposable
    {
        internal LocalIdentityLifecycleHttpFixture Lifecycle { get; private set; } = null!;
        internal IServiceProvider Provider => Lifecycle.Host.Services;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;
        internal Guid Administrator => Lifecycle.Binding.LocalSubjectId;
        internal CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
        internal TerminalProbe Probe { get; } = new();
        private IdentityDatabaseTopology topology;
        private LocalAuthRequestDto login = null!;

        internal static async Task<LinkageFixture> Create(IdentityDatabaseTopology topology)
        {
            var fixture = new LinkageFixture { topology = topology };
            try
            {
                fixture.Lifecycle = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: topology,
                    configureServices: services => ConfigureProbe(services, fixture.Probe));
                fixture.login = fixture.Lifecycle.Login;
                await using var scope = fixture.Provider.CreateAsyncScope();
                var app = fixture.Application(scope);
                app.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = fixture.Administrator,
                    User = null!,
                    RoleId = (int)RoleEnum.Admin,
                    Role = null!,
                    GrantedAt = fixture.Lifecycle.Clock.GetUtcNow().UtcDateTime
                });
                await app.SaveChangesAsync(fixture.Token);
                var created = await fixture.Store(scope).CreatePendingAsync(new LocalCredentialCreateRequest(
                    Guid.CreateVersion7(), fixture.Administrator, null, "Local", "Administrator", "local-operator"),
                    fixture.Token);
                await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
                fixture.Receipt = created.Receipt!;
                app.AddRange(new User
                {
                    Id = fixture.Receipt.LocalSubjectId,
                    Pii = new UserPii { Email = string.Empty, FirstName = "Local", LastName = "Administrator" },
                    CreatedAt = fixture.Lifecycle.Clock.GetUtcNow().UtcDateTime
                }, new Actor
                {
                    Id = fixture.Receipt.PersonalActorId,
                    UserId = fixture.Receipt.LocalSubjectId,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Local Administrator" },
                    CreatedAt = fixture.Lifecycle.Clock.GetUtcNow().UtcDateTime
                }, new UserExternalLogin
                {
                    Id = fixture.Receipt.ExternalLoginId,
                    UserId = fixture.Receipt.LocalSubjectId,
                    User = null!,
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                    AuthenticationProvider = null!,
                    ProviderKey = fixture.Receipt.LocalSubjectId.ToString("D"),
                    CreatedAt = fixture.Lifecycle.Clock.GetUtcNow().UtcDateTime
                });
                await app.SaveChangesAsync(fixture.Token);
                app.ChangeTracker.Clear();
                var operation = await fixture.Store(scope).ReadOperationAsync(fixture.Receipt.OperationId, fixture.Token);
                await Assert.That(await fixture.Store(scope).ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(
                    fixture.Receipt.OperationId, operation!.OperationConcurrencyStamp), fixture.Token))
                    .IsEqualTo(LocalCredentialActivationOutcome.Activated);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal ExploreDbContext Application(AsyncServiceScope scope) =>
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal DbContext Identity(AsyncServiceScope scope) => topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : Application(scope);
        internal LocalIdentityCredentialStateStore Store(AsyncServiceScope scope) => new(Identity(scope), Application(scope),
            scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), Lifecycle.Clock,
            scope.ServiceProvider.GetRequiredService<PrivacyIdentityFenceOperation>());

        internal ManagementTenantProvisioningRequestDto ManagedRequest() =>
            ManagedTenantProvisioningRequestCodec.Deserialize($$$"""
            {"externalRequestId":"managed-1","externalCustomerReference":"customer-1","tenantName":"Managed Tenant","tenantSlug":"managed-tenant",
             "administrator":{"localIdentity":{"localSubjectId":"{{{Receipt.LocalSubjectId:D}}}"}},
             "plan":{"key":"managed","versionId":"018e4e5c-7f00-7000-8000-000000000002"},
             "directoryOperatorIdentity":{"publicName":"Operator","legalName":"Operator ASBL","operatorKindCode":"registered_organization",
             "jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test",
             "legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}}
            """);

        internal async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> Ensure()
        {
            await using var request = new Request(Provider);
            await request.Authenticate(await Lifecycle.SignInAsync(login));
            await using var global = await request.SelectOriginalGlobal();
            return await Execute(global.Services, new EnsureManagedProviderClientProvisionedCommand
            {
                ProvisioningDto = ManagedTenantProvisioningRequestCodec.ToProvisioningRequest(ManagedRequest())
            }, capacityEnforced: false);
        }

        internal async Task UseOrdinaryPrincipal() => login = await Lifecycle.Native.SeedLocalUserAsync(emailConfirmed: true);

        internal async Task ReplaceAdministrator()
        {
            await UseOrdinaryPrincipal();
            await using var identity = Lifecycle.Native.CreateIdentityDatabase();
            Guid successor = await identity.Set<LocalIdentityUser>().Where(row => row.Email == login.Identifier)
                .Select(row => row.Id).SingleAsync(Token);
            await using var app = Lifecycle.Native.CreateDatabase();
            await app.PlatformUserRoles.Where(row => row.UserId == Administrator).ExecuteDeleteAsync(Token);
            app.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = successor,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = Lifecycle.Clock.GetUtcNow().UtcDateTime
            });
            await app.SaveChangesAsync(Token);
        }

        internal async Task<string> CredentialSnapshot()
        {
            await using var scope = Provider.CreateAsyncScope();
            var user = await Identity(scope).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == Receipt.LocalSubjectId, Token);
            var token = await Identity(scope).Set<IdentityUserToken<Guid>>().AsNoTracking()
                .SingleAsync(row => row.UserId == Receipt.LocalSubjectId, Token);
            var summary = (await Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), Token))
                .Items.Single(row => row.LocalSubjectId == Receipt.LocalSubjectId);
            return JsonSerializer.Serialize(new
            {
                user.PasswordHash,
                user.SecurityStamp,
                user.ConcurrencyStamp,
                token.Value,
                summary.CurrentOperationId,
                summary.CurrentOperationConcurrencyStamp
            });
        }

        internal async Task AssertNoTenant()
        {
            await using var app = Lifecycle.Native.CreateDatabase();
            await Assert.That(await app.Tenants.AnyAsync(row => row.Slug == "managed-tenant", Token)).IsFalse();
            await Assert.That(await app.Set<ExternalBinding>().AnyAsync(Token)).IsFalse();
        }

        internal async Task AssertLinked(ManagedProviderClientProvisioningResultDto result)
        {
            await using var app = Lifecycle.Native.CreateDatabase();
            await Assert.That(result.UserId).IsEqualTo(Receipt.LocalSubjectId);
            await Assert.That(result.UserActorId).IsEqualTo(Receipt.PersonalActorId);
            await Assert.That(result.UserExternalLoginId).IsEqualTo(Receipt.ExternalLoginId);
            var membership = await new TenantUserRepository(app).GetByTenantAndUserAsync(result.TenantId, Receipt.LocalSubjectId);
            await Assert.That(membership!.ActorId).IsEqualTo(Receipt.PersonalActorId);
            var grants = await new TenantUserRoleGrantRepository(app).GetByTenant(result.TenantId);
            await Assert.That(grants.Count).IsEqualTo(1);
            await Assert.That(grants.Single().Role.MasterCode).IsEqualTo("tenant.admin");
            await Assert.That(await new PlatformUserRoleRepository(app).IsUserPlatformAdmin(Receipt.LocalSubjectId)).IsFalse();
            await Assert.That(await app.Set<EmailDispatchOutbox>().CountAsync(Token)).IsEqualTo(0);
        }

        public async ValueTask DisposeAsync()
        {
            if (Lifecycle is not null) await Lifecycle.DisposeAsync();
        }
    }
}
