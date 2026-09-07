// ABOUTME: Exercises managed Local administrator linkage with native SQLite Identity and application persistence.
// ABOUTME: Guards exact shared identity, current authority, replay after replacement/reset, and transactional grants.

using System.Security.Claims;
using System.Security.Cryptography;
using Explore.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Handlers.Commands;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Management;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class ManagedTenantLocalAdministratorLinkageTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ExistingEmailLessLocalAdministratorCanBeReferencedWithoutCredentialOperation(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
        await Assert.That(identities.Items.Single().HasExactBinding).IsTrue();
        await Assert.That(identities.Items.Single().Email).IsNull();
        var request = fixture.Request();
        using var snapshot = JsonDocument.Parse(ManagedTenantProvisioningRequestCodec.Serialize(request));
        await Assert.That(snapshot.RootElement.GetProperty("administrator").GetProperty("localIdentity")
            .GetProperty("localSubjectId").GetGuid()).IsEqualTo(fixture.Receipt.LocalSubjectId);
        var before = await fixture.CredentialSnapshotAsync();
        var result = await fixture.EnsureAsync();
        await Assert.That(result.IsSuccess).IsTrue();
        await fixture.AssertLinkedAsync(result.Id!);
        await Assert.That(await fixture.CredentialSnapshotAsync()).IsEqualTo(before);
    }

    [Test]
    public async Task SubjectPossessionCannotAuthorizeLinkage()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        fixture.SignIn(fixture.Receipt.LocalSubjectId);
        var result = await fixture.EnsureAsync();
        await Assert.That(result.FailureCode).IsEqualTo("authorization_denied");
        await fixture.AssertNoTenantAsync();
    }

    [Test]
    public async Task CurrentAdministratorCanLinkAfterOriginalIssuerLosesAuthority()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.ReplaceAdministratorAsync();
        var result = await fixture.EnsureAsync();
        await Assert.That(result.IsSuccess).IsTrue();
        await fixture.AssertLinkedAsync(result.Id!);
    }

    [Test]
    public async Task ResetAndPasswordReplacementDoNotInvalidateIdempotentLinkage()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        var first = await fixture.EnsureAsync();
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var reset = fixture.Provider.CreateAsyncScope())
        {
            var current = (await fixture.Store(reset).ReadOperationAsync(fixture.Receipt.OperationId, fixture.Token))!;
            var outcome = await fixture.Store(reset).ResetAsync(new LocalCredentialResetRequest(Guid.CreateVersion7(),
                fixture.Administrator, fixture.Receipt.LocalSubjectId, current.Receipt.OperationId,
                current.OperationConcurrencyStamp, "Operator-approved reset"), fixture.Token);
            await Assert.That(outcome.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        }
        var afterReset = await fixture.EnsureAsync();
        await Assert.That(afterReset.Id!.TenantId).IsEqualTo(first.Id!.TenantId);
        await using (var replace = fixture.Provider.CreateAsyncScope())
        {
            var current = await fixture.Identity(replace).Set<LocalIdentityUser>().AsNoTracking().SingleAsync(fixture.Token);
            var subject = await fixture.Store(replace).ReadReplacementSubjectAsync(current.Id, current.SecurityStamp!, fixture.Token);
            var now = DateTimeOffset.UtcNow;
            var outcome = await fixture.Store(replace).ReplaceAsync(new LocalCredentialReplacementRequest(
                new LocalCredentialReplacementAuthority(subject!, now, now.AddMinutes(5)),
                $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}"), fixture.Token);
            await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        }
        var ready = await fixture.CredentialSnapshotAsync();
        var afterReplacement = await fixture.EnsureAsync();
        await Assert.That(afterReplacement.Id!.TenantId).IsEqualTo(first.Id!.TenantId);
        await fixture.AssertLinkedAsync(afterReplacement.Id);
        await Assert.That(await fixture.CredentialSnapshotAsync()).IsEqualTo(ready);
    }

    [Test]
    [Arguments("pending")]
    [Arguments("malformed")]
    [Arguments("mismatch")]
    [Arguments("suspended")]
    [Arguments("login")]
    public async Task IncompleteOrMismatchedLiveBindingFailsClosed(string damage)
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using (var corrupt = fixture.Provider.CreateAsyncScope())
        {
            var identity = fixture.Identity(corrupt);
            if (damage == "suspended")
                await fixture.Application(corrupt).Actors.Where(actor => actor.Id == fixture.Receipt.PersonalActorId)
                    .ExecuteUpdateAsync(update => update.SetProperty(actor => actor.IsSuspended, true), fixture.Token);
            else if (damage == "login")
                await fixture.Application(corrupt).UserExternalLogins.Where(login => login.Id == fixture.Receipt.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(login => login.ProviderKey, Guid.CreateVersion7().ToString("D")), fixture.Token);
            else
            {
                string value = damage == "malformed" ? "{}" : JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                    LocalCredentialStateMetadata.CurrentVersion, damage == "pending" ? LocalCredentialState.ProvisioningPending : LocalCredentialState.ChangeRequired,
                    fixture.Receipt.OperationId, damage == "mismatch" ? Guid.CreateVersion7() : fixture.Receipt.LocalSubjectId));
                await identity.Set<IdentityUserToken<Guid>>().Where(token => token.UserId == fixture.Receipt.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(token => token.Value, value), fixture.Token);
            }
            await Assert.That(await fixture.Store(corrupt).ReadLinkedIdentityAsync(fixture.Receipt.LocalSubjectId, fixture.Token)).IsNull();
        }
        var result = await fixture.EnsureAsync();
        await Assert.That(result.FailureCode).IsEqualTo("tenant_local_administrator_unavailable");
        await fixture.AssertNoTenantAsync();
    }

    [Test]
    public async Task DurableWorkerRechecksRevokedManagedRegistration()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        var operation = await fixture.ScheduleAsync();
        await using (var revoke = fixture.Provider.CreateAsyncScope())
        {
            var repository = new ManagedControlPlaneRegistrationRepository(fixture.Application(revoke));
            var registration = (await repository.GetCurrentAsync(fixture.Token))!;
            registration.Revoke(DateTime.UtcNow);
            await repository.Update(registration);
        }
        await fixture.ProcessAsync(operation);
        await using var observe = fixture.Provider.CreateAsyncScope();
        var terminal = await new ManagedTenantProvisioningOperationRepository(fixture.Application(observe))
            .GetByIdAsNoTrackingAsync(operation.Id, fixture.Token);
        await Assert.That(terminal!.FailureCode).IsEqualTo("managed_registration_unavailable");
        await fixture.AssertNoTenantAsync();
    }

    [Test]
    public async Task DurableWorkerCarriesExplicitDirectoryIdentityAndCompletesWithoutInvitation()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        var operation = await fixture.ScheduleAsync();
        fixture.SignIn(fixture.Receipt.LocalSubjectId); // A worker has registration authority, not this user or the original issuer.
        await fixture.ProcessAsync(operation);
        await using var observe = fixture.Provider.CreateAsyncScope();
        var terminal = await new ManagedTenantProvisioningOperationRepository(fixture.Application(observe))
            .GetByIdAsNoTrackingAsync(operation.Id, fixture.Token);
        await Assert.That(terminal!.Status).IsEqualTo(ManagedTenantProvisioningStatus.Succeeded);
        await Assert.That(terminal.TenantAdministratorUserId).IsEqualTo(fixture.Receipt.LocalSubjectId);
        var documents = await new TenantSettingsDocumentRepository(fixture.Application(observe)).GetByTenantAndDocumentKey(
            terminal.TenantId!.Value, Explore.Domain.Settings.Documents.SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity, fixture.Token);
        using var payload = JsonDocument.Parse(documents!.PayloadJson);
        await Assert.That(payload.RootElement.GetProperty("legalName").GetString()).IsEqualTo("Operator ASBL");
        await Assert.That(await fixture.Application(observe).Set<EmailDispatchOutbox>().CountAsync(fixture.Token)).IsEqualTo(0);
    }

    [Test]
    public async Task TenantGrantFailureRollsBackOnlyTenantStateAndRetryLinksOnce()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        var before = await fixture.CredentialSnapshotAsync();
        fixture.GrantFault.Armed = true;
        await Assert.That(async () => { await fixture.EnsureAsync(); }).Throws<InjectedGrantFailure>();
        await fixture.AssertNoTenantAsync();
        await Assert.That(await fixture.CredentialSnapshotAsync()).IsEqualTo(before);
        var retry = await fixture.EnsureAsync();
        await Assert.That(retry.IsSuccess).IsTrue();
        await fixture.AssertLinkedAsync(retry.Id!);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"managed-local-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"managed-local-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private IdentityDatabaseTopology _topology;
        internal ServiceProvider Provider { get; private set; } = null!;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;
        internal Guid Administrator { get; } = Guid.CreateVersion7();
        internal CancellationToken Token => _timeout.Token;
        internal HttpContextAccessor Http { get; } = new();
        private Guid _currentUserId;
        internal GrantWriteFault GrantFault { get; } = new();
        internal Guid ManagedInstanceId { get; } = Guid.CreateVersion7();
        private readonly IOptions<ManagedControlPlaneOptions> _managedOptions = Options.Create(new ManagedControlPlaneOptions
            { Enabled = true, MaximumTenantCount = 10 });

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            var fixture = new Fixture { _topology = topology };
            try { await fixture.InitializeAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal ExploreDbContext Application(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal DbContext Identity(AsyncServiceScope scope) => _topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : Application(scope);
        internal LocalIdentityCredentialStateStore Store(AsyncServiceScope scope) => new(Identity(scope), Application(scope),
            scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), TimeProvider.System);
        internal AdminContext Admin(AsyncServiceScope scope) => new(new HttpContextAccessor
            { HttpContext = PrincipalContext(_currentUserId) },
            new PlatformUserRoleRepository(Application(scope)), new TenantUserRoleGrantRepository(Application(scope)),
            new OrganizationMemberRepository(Application(scope)), new GroupMemberRepository(Application(scope)),
            new UserExternalLoginRepository(Application(scope)), scope.ServiceProvider.GetRequiredService<IMemoryCache>(),
            NullLogger<AdminContext>.Instance);

        internal ManagementTenantProvisioningRequestDto Request() => ManagedTenantProvisioningRequestCodec.Deserialize($$$"""
            {"externalRequestId":"managed-1","externalCustomerReference":"customer-1","tenantName":"Managed Tenant","tenantSlug":"managed-tenant",
             "administrator":{"localIdentity":{"localSubjectId":"{{{Receipt.LocalSubjectId:D}}}"}},
             "plan":{"key":"managed","versionId":"018e4e5c-7f00-7000-8000-000000000002"},
             "directoryOperatorIdentity":{"publicName":"Operator","legalName":"Operator ASBL","operatorKindCode":"registered_organization",
             "jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test",
             "legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}}
            """);

        internal void SignIn(Guid userId) => _currentUserId = userId;
        private static DefaultHttpContext PrincipalContext(Guid userId) => new() { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))], "test")) };

        internal ManagedTenantProvisioningPreflight Preflight(AsyncServiceScope scope)
        {
            var app = Application(scope);
            var settings = new SystemSettingRepository(app, new RelationalSettingMutationLock(app, new EfCoreUnitOfWork(app)));
            return new(new TenantRepository(app), new TenantPlanRepository(app), new ModuleDefinitionRepository(app),
                new TenantSettingRepository(app), settings, new TenantBrandingSettingsDocumentLockService(settings),
                new TenantPlanStorageQuotaCeilingPolicy(settings), Store(scope));
        }

        internal EnsureManagedProviderClientProvisionedCommandHandler Handler(AsyncServiceScope scope)
        {
            var app = Application(scope);
            var tenants = new TenantRepository(app);
            var unit = new EfCoreUnitOfWork(app);
            var mutation = new RelationalSettingMutationLock(app, unit);
            var settings = new SystemSettingRepository(app, mutation);
            var tenantSettings = new TenantSettingRepository(app);
            var documents = new TenantSettingsDocumentRepository(app);
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            var typed = new TypedSettingsDocumentResolver(documents, cache);
            var accessor = new TenantContextAccessor(Http);
            var tenantContext = new TenantContext(accessor, new TenantResolverService([], accessor, Options.Create(new DeploymentSettings())));
            var hierarchy = new HierarchicalSettingsResolver(settings, tenantSettings, new OrganizationSettingRepository(app),
                new GroupSettingRepository(app), new GroupTenantRepository(app), new UserPreferenceRepository(app), tenantContext,
                mutation, cache, NullLogger<HierarchicalSettingsResolver>.Instance);
            return new(tenants, new UserRepository(app), new ActorRepository(app), new UserExternalLoginRepository(app),
                new TenantUserRepository(app), new TenantUserProfileRepository(app), new TenantUserRoleGrantRepository(app),
                new RoleRepository(app), new OrganizationRepository(app), new OrganizationTenantRepository(app),
                new OrganizationMemberRepository(app), new GroupRepository(app), new GroupTenantRepository(app),
                new GroupMemberRepository(app), new ExternalBindingRepository(app), new TenantOnboardingStateRepository(app),
                new ManagedTenantProvisioningOperationRepository(app), new TenantPlanRepository(app), new TenantCapabilityRepository(app),
                tenantSettings, documents, new TenantCreationService(tenants, documents), Store(scope), Admin(scope),
                new PlatformUserRoleRepository(app), new ManagedControlPlaneRegistrationRepository(app),
                Provider.GetRequiredService<IDeploymentModeProvider>(), new AuditLogRepository(app),
                new TenantBrandingSettingsDocumentProvisioningService(tenants, documents, typed), typed, hierarchy, Preflight(scope),
                new TenantActivationCapacityPolicy(new InstanceBootstrapStateRepository(app), tenants,
                    new ManagedTenantProvisioningOperationRepository(app), _managedOptions), _managedOptions,
                mutation, unit, NullLogger<EnsureManagedProviderClientProvisionedCommandHandler>.Instance);
        }

        internal async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> EnsureAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            return await Handler(scope).EnsureAsync(ManagedTenantProvisioningRequestCodec.ToProvisioningRequest(Request()), null, null, null, Token);
        }

        internal async Task<ManagedTenantProvisioningOperation> ScheduleAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var app = Application(scope);
            await SeedManagedPrerequisitesAsync(app);
            var operations = new ManagedTenantProvisioningOperationRepository(app);
            var tenants = new TenantRepository(app);
            var handler = new ScheduleManagedTenantProvisioningCommandHandler(Provider.GetRequiredService<IDeploymentModeProvider>(),
                new ManagedControlPlaneRegistrationRepository(app), operations, new ExternalBindingRepository(app), tenants,
                new OutboxRepository(app), new RelationalSettingMutationLock(app, new EfCoreUnitOfWork(app)),
                new TenantActivationCapacityPolicy(new InstanceBootstrapStateRepository(app), tenants, operations, _managedOptions), Preflight(scope));
            var result = await handler.Handle(new ScheduleManagedTenantProvisioningCommand(ManagedInstanceId, Request()), Token);
            await Assert.That(result.IsSuccess).IsTrue();
            return (await operations.GetByIdAsNoTrackingAsync(result.Id!.OperationId, Token))!;
        }

        internal async Task ProcessAsync(ManagedTenantProvisioningOperation operation)
        {
            await using var scope = Provider.CreateAsyncScope();
            await new ProcessManagedTenantProvisioningOperationCommandHandler(
                new ManagedTenantProvisioningOperationRepository(Application(scope)), Handler(scope))
                .Handle(new ProcessManagedTenantProvisioningOperationCommand(operation.Id, operation.CurrentOutboxMessageId), Token);
        }

        internal async Task<string> CredentialSnapshotAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var user = await Identity(scope).Set<LocalIdentityUser>().AsNoTracking().SingleAsync(Token);
            var token = await Identity(scope).Set<IdentityUserToken<Guid>>().AsNoTracking().SingleAsync(Token);
            var summary = (await Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), Token)).Items.Single();
            return JsonSerializer.Serialize(new { user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp,
                token.Value, summary.CurrentOperationId, summary.CurrentOperationConcurrencyStamp });
        }

        internal async Task AssertNoTenantAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            await Assert.That(await new TenantRepository(Application(scope)).GetTenantBySlug("managed-tenant")).IsNull();
            await Assert.That(await Application(scope).Set<ExternalBinding>().CountAsync(Token)).IsEqualTo(0);
        }

        internal async Task AssertLinkedAsync(ManagedProviderClientProvisioningResultDto result)
        {
            await using var scope = Provider.CreateAsyncScope();
            var app = Application(scope);
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

        internal async Task ReplaceAdministratorAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var app = Application(scope);
            await app.Set<PlatformUserRole>().Where(role => role.UserId == Administrator).ExecuteDeleteAsync(Token);
            Guid successor = Guid.CreateVersion7();
            await new UserRepository(app).Create(new User { Id = successor,
                Pii = new UserPii { Email = string.Empty, FirstName = "Successor", LastName = "Administrator" }, CreatedAt = DateTime.UtcNow });
            Role platformAdmin = (await new RoleRepository(app).GetByMasterCodeAsync("platform.admin"))!;
            await new PlatformUserRoleRepository(app).Create(new PlatformUserRole { Id = Guid.CreateVersion7(),
                UserId = successor, User = null!, Role = null!, RoleId = platformAdmin.Id, GrantedAt = DateTime.UtcNow });
            SignIn(successor);
        }

        private async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddDistributedMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.Configure<DeploymentSettings>(settings => settings.Mode = DeploymentMode.MultiTenant);
            services.AddScoped<IInstanceBootstrapStateRepository, InstanceBootstrapStateRepository>();
            services.AddSingleton<IDeploymentModeProvider, DeploymentModeProvider>();
            services.AddDbContext<ExploreDbContext>(options => Configure(options, _applicationPath).AddInterceptors(GrantFault));
            var identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
                .AddUserValidator<OptionalEmailLocalIdentityUserValidator>();
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => Configure(options, _identityPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            Provider = services.BuildServiceProvider();
            SignIn(Administrator);
            await using (var seed = Provider.CreateAsyncScope())
            {
                var app = Application(seed);
                await app.Database.EnsureCreatedAsync(Token);
                if (_topology == IdentityDatabaseTopology.External) await Identity(seed).Database.EnsureCreatedAsync(Token);
                await LookupTableSeeder.SeedAsync(app, Token);
                await new UserRepository(app).Create(new User { Id = Administrator,
                    Pii = new UserPii { Email = string.Empty, FirstName = "Current", LastName = "Administrator" }, CreatedAt = DateTime.UtcNow });
                Role platformAdmin = (await new RoleRepository(app).GetByMasterCodeAsync("platform.admin"))!;
                await new PlatformUserRoleRepository(app).Create(new PlatformUserRole { Id = Guid.CreateVersion7(),
                    UserId = Administrator, User = null!, Role = null!, RoleId = platformAdmin.Id, GrantedAt = DateTime.UtcNow });
                var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.MultiTenant, DateTime.UtcNow);
                bootstrap.CompleteInteractive(Administrator, DateTime.UtcNow);
                await new InstanceBootstrapStateRepository(app).Create(bootstrap);
                var plan = await new TenantPlanRepository(app).Create(new TenantPlan { Id = Guid.CreateVersion7(), Key = "managed",
                    DisplayName = "Managed", CreatedAt = DateTime.UtcNow });
                await new TenantPlanRepository(app).CreateVersionAsync(new TenantPlanVersion {
                    Id = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002"), TenantPlanId = plan.Id, VersionNumber = 1,
                    TenantPlanStatusId = (int)TenantPlanStatusEnum.Published, IsActiveForProvisioning = true,
                    CurrencyCode = "EUR", BillingPeriod = "monthly", CreatedAt = DateTime.UtcNow }, Token);
            }
            await using (var create = Provider.CreateAsyncScope())
            {
                var created = await Store(create).CreatePendingAsync(new LocalCredentialCreateRequest(Guid.CreateVersion7(),
                    Administrator, null, "Local", "Administrator", "local-operator"), Token);
                await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
                Receipt = created.Receipt!;
            }
            await using var reconcile = Provider.CreateAsyncScope();
            var application = Application(reconcile);
            await new UserRepository(application).Create(new User { Id = Receipt.LocalSubjectId,
                Pii = new UserPii { Email = string.Empty, FirstName = "Local", LastName = "Administrator" }, CreatedAt = DateTime.UtcNow });
            await new ActorRepository(application).Create(new Actor { Id = Receipt.PersonalActorId,
                UserId = Receipt.LocalSubjectId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Local Administrator" }, CreatedAt = DateTime.UtcNow });
            await new UserExternalLoginRepository(application).Create(new UserExternalLogin { Id = Receipt.ExternalLoginId,
                UserId = Receipt.LocalSubjectId, User = null!, AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!, ProviderKey = Receipt.LocalSubjectId.ToString("D"), CreatedAt = DateTime.UtcNow });
            application.ChangeTracker.Clear();
            var operation = await Store(reconcile).ReadOperationAsync(Receipt.OperationId, Token);
            await Assert.That(await Store(reconcile).ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(
                Receipt.OperationId, operation!.OperationConcurrencyStamp), Token)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
        }

        private async Task SeedManagedPrerequisitesAsync(ExploreDbContext app)
        {
            var binding = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.Management.ControlPlaneRegistrationCredentials,
                SecretScope.Instance, null, "CONTROL_PLANE_REGISTRATION_CREDENTIALS");
            app.SecretBindings.Add(binding);
            await app.SaveChangesAsync(Token);
            var registration = new ManagedControlPlaneRegistration {
                Id = Guid.CreateVersion7(), ManagedInstanceId = ManagedInstanceId, EventInstanceId = Guid.CreateVersion7(),
                ControlPlaneEndpoint = "https://control.example.test", ManagementApiVersion = ManagedControlPlaneContract.ManagementApiVersion,
                EventVersion = "test", DeploymentMode = DeploymentMode.MultiTenant, Status = ManagedControlPlaneRegistrationStatus.Registered,
                RegisteredAt = DateTime.UtcNow, RequestHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                EventToControlPlaneKeyId = Guid.CreateVersion7().ToString("N"), ControlPlaneToEventKeyId = Guid.CreateVersion7().ToString("N"),
                EventToControlPlaneSecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ControlPlaneToEventSecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                CredentialSecretBindingId = binding.Id, EventToControlPlaneCredentialExpiresAt = DateTime.UtcNow.AddDays(1),
                ControlPlaneToEventCredentialExpiresAt = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow };
            // Existing registration and queued operation are prerequisites for worker linkage.
            // Their pre-existing SQLite xmin creation/generation defect is outside this slice.
            // Supply an initial version without changing EF's concurrency metadata or update predicates.
            await app.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ie_managed_control_plane_registrations
                    (id, managed_instance_id, event_instance_id, control_plane_endpoint, management_api_version,
                     event_version, deployment_mode, status, registered_at, request_hash, event_to_control_plane_key_id,
                     control_plane_to_event_key_id, event_to_control_plane_secret_hash, control_plane_to_event_secret_hash,
                     credential_secret_binding_id, event_to_control_plane_credential_expires_at,
                     control_plane_to_event_credential_expires_at, created_at, xmin)
                VALUES ({registration.Id}, {registration.ManagedInstanceId}, {registration.EventInstanceId},
                    {registration.ControlPlaneEndpoint}, {registration.ManagementApiVersion}, {registration.EventVersion},
                    {registration.DeploymentMode.ToString()}, {registration.Status.ToString()}, {registration.RegisteredAt},
                    {registration.RequestHash}, {registration.EventToControlPlaneKeyId}, {registration.ControlPlaneToEventKeyId},
                    {registration.EventToControlPlaneSecretHash}, {registration.ControlPlaneToEventSecretHash},
                    {registration.CredentialSecretBindingId}, {registration.EventToControlPlaneCredentialExpiresAt},
                    {registration.ControlPlaneToEventCredentialExpiresAt}, {registration.CreatedAt}, {1L})
                """, Token);

            var request = ManagedTenantProvisioningRequestCodec.Normalize(Request());
            Guid operationId = Guid.CreateVersion7();
            Guid outboxMessageId = Guid.CreateVersion7();
            await app.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ie_managed_tenant_provisioning_operations
                    (id, managed_instance_id, external_request_id, external_customer_reference, request_hash,
                     request_json, tenant_slug, current_outbox_message_id, status, created_at, xmin)
                VALUES ({operationId}, {ManagedInstanceId}, {request.ExternalRequestId}, {request.ExternalCustomerReference},
                    {ManagedTenantProvisioningRequestCodec.ComputeHash(request)}, {ManagedTenantProvisioningRequestCodec.Serialize(request)},
                    {request.TenantSlug}, {outboxMessageId}, {ManagedTenantProvisioningStatus.Pending.ToString()}, {DateTime.UtcNow}, {1L})
                """, Token);
        }

        private static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance);

        public async ValueTask DisposeAsync()
        {
            if (Provider is not null) await Provider.DisposeAsync();
            foreach (string path in new[] { _applicationPath, _identityPath })
            { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
            _timeout.Dispose();
        }
    }

    private sealed class InjectedGrantFailure : Exception;
    private sealed class GrantWriteFault : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<TenantUserRoleGrant>().Any(entry => entry.State == EntityState.Added))
            { Armed = false; throw new InjectedGrantFailure(); }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
