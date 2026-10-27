
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ManagedProviderProvisioning.Handlers.Commands;
using Explore.Application.Features.Management.Handlers.Commands;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Management;
using Explore.Application.Services;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class ManagedTenantLocalAdministratorLinkageTests
{
    [Test]
    public async Task WorkerOnlyHandlerCannotLendRegistrationAuthorityToHumanProvisioning()
    {
        await using var fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var handler = fixture.Handler(scope);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.EnsureAsync(
            ManagedTenantProvisioningRequestCodec.ToProvisioningRequest(fixture.Request()),
            managementRequest: null, operationId: null, expectedOutboxMessageId: null, fixture.Token));
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
        // This worker has no human coordinator or credential; its persisted registration owns execution.
        await fixture.ProcessAsync(operation);
        await using var observe = fixture.Provider.CreateAsyncScope();
        var terminal = await new ManagedTenantProvisioningOperationRepository(fixture.Application(observe))
            .GetByIdAsNoTrackingAsync(operation.Id, fixture.Token);
        await Assert.That(terminal!.Status).IsEqualTo(ManagedTenantProvisioningStatus.Succeeded);
        await Assert.That(terminal.TenantAdministratorUserId).IsEqualTo(fixture.Receipt.LocalSubjectId);
        var tenant = await new TenantRepository(fixture.Application(observe))
            .GetTenantBySlug(fixture.Request().TenantSlug);
        await Assert.That(tenant).IsNotNull();
        Guid tenantId = tenant!.Id;
        await Assert.That(terminal.TenantId).IsEqualTo(tenantId);
        var revisions = await fixture.Application(observe).Set<EventDiscoveryRevision>().AsNoTracking()
            .IgnoreTenantFilter("Independent SQLite verification of the original worker operation's exact tenant.")
            .Where(row => row.TenantId == tenantId).ToListAsync(fixture.Token);
        await Assert.That(revisions.Count).IsEqualTo(1);
        var revision = revisions.Single();
        await Assert.That(revision.TenantId).IsEqualTo(terminal.TenantId!.Value);
        await Assert.That(revision.Id.Version).IsEqualTo(7);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(1);
        var documents = await new TenantSettingsDocumentRepository(fixture.Application(observe)).GetByTenantAndDocumentKey(
            terminal.TenantId!.Value, Explore.Domain.Settings.Documents.SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity, fixture.Token);
        using var payload = JsonDocument.Parse(documents!.PayloadJson);
        await Assert.That(payload.RootElement.GetProperty("legalName").GetString()).IsEqualTo("Operator ASBL");
        await Assert.That(await fixture.Application(observe).Set<EmailDispatchOutbox>().CountAsync(fixture.Token)).IsEqualTo(0);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"managed-local-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"managed-local-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private IdentityDatabaseTopology _topology;
        internal ServiceProvider Provider { get; private set; } = null!;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;
        internal Guid Administrator { get; } = Guid.CreateVersion7();
        internal CancellationToken Token => _timeout.Token;
        internal Guid ManagedInstanceId { get; } = Guid.CreateVersion7();
        private readonly IOptions<ManagedControlPlaneOptions> _managedOptions = Options.Create(new ManagedControlPlaneOptions
        { Enabled = true, MaximumTenantCount = 10 });

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync();
            var fixture = new Fixture { _topology = topology };
            try { await fixture.InitializeAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal ExploreDbContext Application(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal DbContext Identity(AsyncServiceScope scope) => _topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : Application(scope);
        internal LocalIdentityCredentialStateStore Store(AsyncServiceScope scope) => new(Identity(scope), Application(scope),
            scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), TimeProvider.System,
            identityFence: NativeIdentityRecognitionBoundary.For(Application(scope), TimeProvider.System));

        internal ManagementTenantProvisioningRequestDto Request() => ManagedTenantProvisioningRequestCodec.Deserialize($$$"""
            {"externalRequestId":"managed-1","externalCustomerReference":"customer-1","tenantName":"Managed Tenant","tenantSlug":"managed-tenant",
             "administrator":{"localIdentity":{"localSubjectId":"{{{Receipt.LocalSubjectId:D}}}"}},
             "plan":{"key":"managed","versionId":"018e4e5c-7f00-7000-8000-000000000002"},
             "directoryOperatorIdentity":{"publicName":"Operator","legalName":"Operator ASBL","operatorKindCode":"registered_organization",
             "jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test",
             "legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}}
            """);

        internal ManagedTenantProvisioningPreflight Preflight(AsyncServiceScope scope)
        {
            var app = Application(scope);
            var mutation = new RelationalSettingMutationLock(app, new EfCoreUnitOfWork(app));
            var settings = SystemSettingRepository.CreateReadOnly(app, mutation);
            return new(new TenantRepository(app), new TenantPlanRepository(app), new ModuleDefinitionRepository(app),
                new ManagedTenantDomainRepository(Provider.GetRequiredService<IGlobalExploreDbContextFactory>()),
                settings, new TenantBrandingSettingsDocumentLockService(settings, mutation),
                new TenantPlanStorageQuotaCeilingPolicy(settings), Store(scope));
        }

        internal EnsureManagedProviderClientProvisionedCommandHandler Handler(AsyncServiceScope scope) =>
            ActivatorUtilities.GetServiceOrCreateInstance<EnsureManagedProviderClientProvisionedCommandHandler>(scope.ServiceProvider);

        internal async Task<ManagedTenantProvisioningOperation> ScheduleAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var app = Application(scope);
            await SeedManagedPrerequisitesAsync(app);
            var operations = new ManagedTenantProvisioningOperationRepository(app);
            var handler = ActivatorUtilities.GetServiceOrCreateInstance<ScheduleManagedTenantProvisioningCommandHandler>(scope.ServiceProvider);
            var result = await handler.ExecuteAsync(new ScheduleManagedTenantProvisioningCommand(ManagedInstanceId, Request()), Token);
            await Assert.That(result.IsSuccess).IsTrue();
            return (await operations.GetByIdAsNoTrackingAsync(result.Id!.OperationId, Token))!;
        }

        internal async Task ProcessAsync(ManagedTenantProvisioningOperation operation)
        {
            await using var scope = Provider.CreateAsyncScope();
            await ActivatorUtilities.GetServiceOrCreateInstance<ProcessManagedTenantProvisioningOperationCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new ProcessManagedTenantProvisioningOperationCommand(operation.Id, operation.CurrentOutboxMessageId), Token);
        }

        internal async Task AssertNoTenantAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            await Assert.That(await new TenantRepository(Application(scope)).GetTenantBySlug("managed-tenant")).IsNull();
            await Assert.That(await Application(scope).Set<ExternalBinding>().CountAsync(Token)).IsEqualTo(0);
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
            services.AddScoped<IInstanceBootstrapStateReader, InstanceBootstrapStateReader>();
            services.AddSingleton<IDeploymentModeProvider, DeploymentModeProvider>();
            services.AddDbContext<ExploreDbContext>(options => Configure(options, _applicationPath));
            services.Replace(ServiceDescriptor.Scoped<ExploreDbContext>(provider =>
            {
                // This fixture owns each fresh scoped application context. Seal
                // its null Runtime selection before exposing it to any producer.
                var context = new ExploreDbContext(provider.GetRequiredService<DbContextOptions<ExploreDbContext>>());
                context.BindTenantLease();
                return context;
            }));
            services.AddSingleton<IGlobalExploreDbContextFactory>(_ => new GlobalExploreDbContextFactory(
                new PrimaryDatabaseConnectionOptions
                {
                    Provider = PrimaryDatabaseProvider.Sqlite,
                    Role = PrimaryDatabaseRole.Global,
                    Database = _applicationPath
                }));
            var identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
                .AddUserValidator<OptionalEmailLocalIdentityUserValidator>();
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => Configure(options, _identityPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            Provider = services.BuildIsolatedServiceProvider();
            await LocalIdentitySqliteTemplate.CopyAsync(_applicationPath,
                _topology == IdentityDatabaseTopology.External ? _identityPath : null, seedLookups: true, Token);
            await using (var seed = Provider.CreateAsyncScope())
            {
                var app = Application(seed);
                await new UserRepository(app).Create(new User
                {
                    Id = Administrator,
                    Pii = new UserPii { Email = string.Empty, FirstName = "Current", LastName = "Administrator" },
                    CreatedAt = DateTime.UtcNow
                });
                Role platformAdmin = (await new RoleRepository(app).GetByMasterCodeAsync("platform.admin"))!;
                await new PlatformUserRoleRepository(app).Create(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = Administrator,
                    User = null!,
                    Role = null!,
                    RoleId = platformAdmin.Id,
                    GrantedAt = DateTime.UtcNow
                });
                var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.MultiTenant, DateTime.UtcNow);
                bootstrap.CompleteInteractive(Administrator, DateTime.UtcNow);
                await new InstanceBootstrapStateRepository(app).Create(bootstrap);
                var plan = await new TenantPlanRepository(app).Create(new TenantPlan
                {
                    Id = Guid.CreateVersion7(),
                    Key = "managed",
                    DisplayName = "Managed",
                    CreatedAt = DateTime.UtcNow
                });
                await new TenantPlanRepository(app).CreateVersionAsync(new TenantPlanVersion
                {
                    Id = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002"),
                    TenantPlanId = plan.Id,
                    VersionNumber = 1,
                    TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
                    IsActiveForProvisioning = true,
                    CurrencyCode = "EUR",
                    BillingPeriod = "monthly",
                    CreatedAt = DateTime.UtcNow
                }, Token);
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
            await new UserRepository(application).Create(new User
            {
                Id = Receipt.LocalSubjectId,
                Pii = new UserPii { Email = string.Empty, FirstName = "Local", LastName = "Administrator" },
                CreatedAt = DateTime.UtcNow
            });
            await new ActorRepository(application).Create(new Actor
            {
                Id = Receipt.PersonalActorId,
                UserId = Receipt.LocalSubjectId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                Pii = new ActorPii { DisplayName = "Local Administrator" },
                CreatedAt = DateTime.UtcNow
            });
            await new UserExternalLoginRepository(application).Create(new UserExternalLogin
            {
                Id = Receipt.ExternalLoginId,
                UserId = Receipt.LocalSubjectId,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = Receipt.LocalSubjectId.ToString("D"),
                CreatedAt = DateTime.UtcNow
            });
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
            var registration = new ManagedControlPlaneRegistration
            {
                Id = Guid.CreateVersion7(),
                ManagedInstanceId = ManagedInstanceId,
                EventInstanceId = Guid.CreateVersion7(),
                ControlPlaneEndpoint = "https://control.example.test",
                ManagementApiVersion = ManagedControlPlaneContract.ManagementApiVersion,
                EventVersion = "test",
                DeploymentMode = DeploymentMode.MultiTenant,
                Status = ManagedControlPlaneRegistrationStatus.Registered,
                RegisteredAt = DateTime.UtcNow,
                RequestHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                EventToControlPlaneKeyId = Guid.CreateVersion7().ToString("N"),
                ControlPlaneToEventKeyId = Guid.CreateVersion7().ToString("N"),
                EventToControlPlaneSecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ControlPlaneToEventSecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                CredentialSecretBindingId = binding.Id,
                EventToControlPlaneCredentialExpiresAt = DateTime.UtcNow.AddDays(1),
                ControlPlaneToEventCredentialExpiresAt = DateTime.UtcNow.AddDays(1),
                CreatedAt = DateTime.UtcNow
            };
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

        private DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseMemoryCache(_metadataCache)
            .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance);

        public async ValueTask DisposeAsync()
        {
            if (Provider is not null) await Provider.DisposeAsync();
            _metadataCache.Dispose();
            foreach (string path in new[] { _applicationPath, _identityPath })
            { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
            _timeout.Dispose();
        }
    }

}
