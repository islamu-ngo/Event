using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Handlers.Commands;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class ManagedProviderProvisioningOrchestrationTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "replay")]
    [Arguments(true, "replay")]
    [Arguments(false, "grant")]
    [Arguments(true, "grant")]
    [Arguments(false, "credential")]
    [Arguments(true, "credential")]
    [Arguments(false, "support")]
    [Arguments(true, "support")]
    [Arguments(false, "capacity")]
    [Arguments(true, "capacity")]
    public async Task ActualHandlerRetainsOriginalHumanAndPhysicalOwnerAcrossCompleteProvisioning(
        bool externalTarget, string change)
    {
        var probe = new TerminalProbe();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureProbe(services, probe));
        Guid grant = Guid.CreateVersion7();
        DateTimeOffset supportExpiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid? support = await Seed(fixture, grant, change == "support", supportExpiry);
        string bearer = await fixture.SignInAsync();
        var command = Command(fixture, externalTarget);
        ManagedProviderClientProvisioningResultDto? originalRows = null;
        if (change == "replay")
        {
            await using var firstRequest = new Request(fixture.Host.Services);
            await firstRequest.Authenticate(bearer);
            await using var firstOwner = await firstRequest.SelectOriginalGlobal();
            var first = await Execute(firstOwner.Services, command, capacityEnforced: false);
            await Assert.That(first.IsSuccess).IsTrue();
            originalRows = first.Id;
        }

        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(bearer);
        if (support is { } session)
            request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = session.ToString("D");
        await using var selected = await request.SelectOriginalGlobal();
        var context = selected.Services.GetRequiredService<ExploreDbContext>();
        await Assert.That(context.TenantFilterTenantId).IsNull();
        await Assert.That(selected.Services.GetRequiredService<ITenantContext>().TenantId)
            .IsEqualTo(PlatformDefaults.DefaultTenantId);
        if (support is not null)
            await Assert.That((await selected.Services.GetRequiredService<ISupportAccessSessionService>()
                .GetCurrentAsync()).IsActive).IsTrue();

        bool graphObservedBeforeSettlement = false;
        probe.BeforeSources = async (services, token) =>
        {
            var owner = services.GetRequiredService<ExploreDbContext>();
            await Assert.That(ReferenceEquals(owner, context)).IsTrue();
            var tenant = await owner.Tenants.AsNoTracking()
                .SingleAsync(row => row.Slug == command.ProvisioningDto.TenantSlug, token);
            await Assert.That(await owner.TenantSettingsDocuments.IgnoreQueryFilters().CountAsync(
                row => row.TenantId == tenant.Id, token)).IsEqualTo(2);
            await Assert.That(await owner.TenantUsers.IgnoreQueryFilters().CountAsync(
                row => row.TenantId == tenant.Id, token)).IsEqualTo(1);
            await Assert.That(await owner.TenantUserProfiles.IgnoreQueryFilters().CountAsync(
                row => row.TenantId == tenant.Id, token)).IsEqualTo(1);
            await Assert.That(await owner.TenantUserRoleGrants.IgnoreQueryFilters().CountAsync(
                row => row.TenantId == tenant.Id, token)).IsEqualTo(1);
            graphObservedBeforeSettlement = true;
            if (change == "grant")
                await owner.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
        };
        probe.AfterSources = async (services, token) =>
        {
            if (change is not ("credential" or "support")) return;
            // Keep an actual awaited read between native terminal admission and the
            // synchronous deadline checks; advancing a deterministic clock is not a wait.
            await services.GetRequiredService<ExploreDbContext>().Tenants.AsNoTracking()
                .SingleAsync(row => row.Slug == command.ProvisioningDto.TenantSlug, token);
            DateTimeOffset deadline = supportExpiry;
            if (change == "credential")
            {
                var lifetime = (await services.GetRequiredService<Explore.Application.Authentication.IHumanAuthenticationLifetimeObserver>()
                    .ObserveAsync(request.Context.User, token))!;
                deadline = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
            }
            fixture.Clock.Advance(deadline - fixture.Clock.GetUtcNow());
        };
        probe.Armed = true;

        var response = await Execute(selected.Services, command, change == "capacity");
        bool accepted = change is "unchanged" or "replay";
        await Assert.That(response.IsSuccess).IsEqualTo(accepted);
        if (!accepted)
            await Assert.That(response.FailureCode).IsEqualTo(change == "capacity"
                ? "tenant_provisioning_capacity_exhausted" : "authorization_denied");
        await Assert.That(graphObservedBeforeSettlement).IsEqualTo(change != "capacity");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Slug == command.ProvisioningDto.TenantSlug);
        await Assert.That(persisted is not null).IsEqualTo(accepted);
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
        if (accepted)
        {
            var rows = response.Id ?? throw new InvalidOperationException("Committed provisioning rows are required.");
            await Assert.That(rows.TenantId).IsEqualTo(persisted!.Id);
            await Assert.That(await observer.ExternalBindings.IgnoreQueryFilters().CountAsync(
                row => row.ProviderKey == command.ProvisioningDto.ProviderKey
                    && (row.InternalId == rows.TenantId || row.ScopeTenantId == rows.TenantId))).IsEqualTo(8);
            await Assert.That(await observer.Actors.IgnoreQueryFilters().AnyAsync(row => row.Id == rows.UserActorId)).IsTrue();
            await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.Id == rows.UserExternalLoginId
                && row.UserId == rows.UserId)).IsTrue();
            if (originalRows is not null)
            {
                await Assert.That(rows.TenantId).IsEqualTo(originalRows.TenantId);
                await Assert.That(rows.TenantUserId).IsEqualTo(originalRows.TenantUserId);
                await Assert.That(rows.TenantUserRoleGrantId).IsEqualTo(originalRows.TenantUserRoleGrantId);
                await Assert.That(originalRows.OrganizerMembershipId).IsNotNull();
                await Assert.That(rows.OrganizerId).IsNull();
                await Assert.That(rows.OrganizerActorId).IsNull();
                await Assert.That(rows.OrganizerKind).IsNull();
                await Assert.That(rows.OrganizerMembershipId).IsNull();
                bool retainedOrganizerMember = externalTarget
                    ? await observer.GroupMembers.AnyAsync(row => row.Id == originalRows.OrganizerMembershipId
                        && row.TenantId == rows.TenantId)
                    : await observer.OrganizationMembers.AnyAsync(row => row.Id == originalRows.OrganizerMembershipId
                        && row.TenantId == rows.TenantId);
                await Assert.That(retainedOrganizerMember).IsTrue();
            }
            if (!externalTarget)
            {
                await Assert.That(rows.UserId).IsEqualTo(fixture.Binding.LocalSubjectId);
                await Assert.That(rows.UserActorId).IsEqualTo(fixture.Binding.PersonalActorId);
                await Assert.That(rows.UserExternalLoginId).IsEqualTo(fixture.Binding.ExternalLoginId);
            }
        }
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    private static Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> Execute(
        IServiceProvider services, EnsureManagedProviderClientProvisionedCommand command,
        bool capacityEnforced)
    {
        // Match production selection even for a Local target: the embedded authority
        // is the actual physical owner whenever it supplies the settlement boundary.
        IOriginalOwnerSettlementBoundary owner =
            services.GetRequiredService<IPrivacyIdentityFenceAuthority>() as IOriginalOwnerSettlementBoundary
            ?? (IOriginalOwnerSettlementBoundary)services.GetRequiredService<IUnitOfWork>();
        var coordinator = new HumanManagedProviderProvisioningCoordinator(
            services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHumanManagedProviderProvisioningPreparation>(),
            services.GetRequiredService<PrivacyIdentityFenceOperation>(), owner,
            services.GetRequiredService<ISettingMutationLock>(),
            Options.Create(new ManagedControlPlaneOptions { Enabled = capacityEnforced, MaximumTenantCount = 1 }));
        var handler = ActivatorUtilities.CreateInstance<EnsureManagedProviderClientProvisionedCommandHandler>(services, coordinator);
        return handler.ExecuteAsync(command, default);
    }

    private static EnsureManagedProviderClientProvisionedCommand Command(
        LocalIdentityLifecycleHttpFixture fixture, bool externalTarget)
    {
        string key = Guid.CreateVersion7().ToString("N");
        return new()
        {
            ProvisioningDto = new()
            {
                ProviderKey = "orchestration-" + key,
                ExternalSystem = "orchestration",
                ExternalCustomerId = key,
                TenantFullName = "Original managed customer",
                TenantSlug = "managed-" + key,
                ActivateTenant = true,
                LocalIdentity = externalTarget ? null : new ManagementTenantLocalIdentityDto
                {
                    LocalSubjectId = fixture.Binding.LocalSubjectId
                },
                ExternalAdmin = externalTarget ? new ManagedProviderExternalAdminDto
                {
                    IdentityProvider = "https://orchestration.example.test",
                    Subject = key,
                    Email = key + "@example.test",
                    FirstName = "Managed",
                    LastName = "Administrator",
                    EmailVerified = true
                } : null,
                Organizer = new()
                {
                    Kind = externalTarget ? ManagedProviderOrganizerKindDto.Group : ManagedProviderOrganizerKindDto.Organization,
                    FullName = "Managed organizer"
                },
                DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
                {
                    PublicName = "Managed Operator",
                    LegalName = "Managed Operator ASBL",
                    OperatorKindCode = "registered_organization",
                    JurisdictionCountryCode = "BE",
                    RegistrationIdentifier = "BE 0123.456.789",
                    PublicContactEmail = "contact@example.test",
                    LegalNoticeUrl = "https://example.test/legal",
                    TermsUrl = "https://example.test/terms",
                    PrivacyUrl = "https://example.test/privacy"
                }
            }
        };
    }

    private static async Task<Guid?> Seed(LocalIdentityLifecycleHttpFixture fixture, Guid grant, bool support,
        DateTimeOffset expiresAt)
    {
        await using var context = fixture.Native.CreateDatabase();
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = grant,
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        Guid? sessionId = null;
        if (support)
        {
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Provisioning orchestration", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiresAt);
            context.SupportAccessSessions.Add(session);
            sessionId = session.Id;
        }
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\""),
            (GovernanceSettingKeys.SupportAccess.Enabled, "true"),
            (GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true")
        })
        {
            var row = await context.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
            if (row is null) context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else row.Value = value;
        }
        await context.SaveChangesAsync();
        return sessionId;
    }

    private sealed class TerminalProbe
    {
        internal bool Armed { get; set; }
        internal int Used;
        internal Func<IServiceProvider, CancellationToken, Task>? BeforeSources { get; set; }
        internal Func<IServiceProvider, CancellationToken, Task>? AfterSources { get; set; }
    }

    private static void ConfigureProbe(IServiceCollection services, TerminalProbe probe)
    {
        services.AddHumanManagedProviderProvisioningPreparation();
        var implementation = services.Last(row => row.ServiceType == typeof(IHumanActionCommitFinalizer)).ImplementationType
            ?? throw new InvalidOperationException("The real finalizer implementation is required.");
        services.RemoveAll<IHumanActionCommitFinalizer>();
        services.AddScoped<IHumanActionCommitFinalizer>(provider => new ObservedFinalizer(
            (IHumanActionCommitFinalizer)ActivatorUtilities.CreateInstance(provider, implementation), provider, probe));
    }

    private sealed class ObservedFinalizer(IHumanActionCommitFinalizer inner, IServiceProvider services,
        TerminalProbe probe) : IHumanActionCommitFinalizer
    {
        public void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments) =>
            inner.Register(async token =>
            {
                bool observe = probe.Armed && Interlocked.Exchange(ref probe.Used, 1) == 0;
                if (observe && probe.BeforeSources is { } before) await before(services, token);
                await validateSources(token);
                if (observe && probe.AfterSources is { } after) await after(services, token);
            }, validateAssignments);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }

        internal Request(IServiceProvider services)
        {
            scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            // Install AsyncLocal custody in the caller's flow, not inside Authenticate.
            accessor.HttpContext = Context;
        }

        internal async Task Authenticate(string bearer)
        {
            Context.Request.Headers.Authorization = "Bearer " + bearer;
            var authentication = await Context.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            Context.User = authentication.Principal!;
        }

        internal ValueTask<IPolicyMutationExecutionScope> SelectOriginalGlobal() =>
            Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, default);

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
