
using System.Data.Common;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using Explore.Domain.Constants;
using Explore.Application.Constants;
using Explore.Application.Configuration;
using Explore.Infrastructure.Authentication;
using Microsoft.Extensions.Options;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Infrastructure.Identity;
using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalBootstrapConvergenceTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConfiguredBootstrapCreatesOneVerifiedChangeRequiredAdministrator(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.RunAsync();
        await fixture.AssertCompletedAsync();
    }

    [Test]
    public async Task ConcurrentReplicasConvergeOnTheSameOperationAndSubject()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        fixture.Secrets.Barrier = new SecretReadBarrier();
        Task first = Task.Run(fixture.RunAsync);
        Task second = Task.Run(fixture.RunAsync);
        await fixture.Secrets.Barrier.Entered.Task.WaitAsync(fixture.Token);
        fixture.Secrets.Barrier.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(fixture.Token);
        await fixture.AssertCompletedAsync();
    }

    [Test]
    [Arguments(SecretResolutionStatus.Unconfigured)]
    [Arguments(SecretResolutionStatus.Unavailable)]
    public async Task SelectedSecretAuthorityFailureCannotUseConfigurationPassword(SecretResolutionStatus status)
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        fixture.Secrets.Status = status;
        fixture.Configuration["INSTANCE_BOOTSTRAP_LOCAL_PASSWORD"] = NewPassword();
        await Assert.That(fixture.RunAsync).Throws<ConfiguredAdministratorBootstrapException>();
        await using var scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityPage identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
        await Assert.That(identities.TotalCount).IsEqualTo(0);
        var current = await new InstanceBootstrapStateRepository(fixture.Application(scope)).GetCurrent(fixture.Token);
        await Assert.That(current!.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    [Test]
    public async Task IdentityCommitCrashRetriesWithoutReplacingTheOriginalPassword()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        fixture.IdentityCommitFault.Armed = true;
        await Assert.That(fixture.RunAsync).Throws<InjectedBootstrapFailure>();
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            var identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
            await Assert.That(identities.Items.Single().CredentialState).IsEqualTo(LocalCredentialState.ProvisioningPending);
            await Assert.That(identities.Items.Single().HasExactBinding).IsFalse();
        }
        fixture.Secrets.Password = NewPassword();
        await fixture.RunAsync();
        await fixture.AssertCompletedAsync();
    }

    [Test]
    public async Task ApplicationCommitCrashReconcilesActivationWithoutLeftoverConfigurationOrSecrets()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        fixture.ApplicationCommitFault.Armed = true;
        await Assert.That(() => fixture.RunAsync(prepare: false)).Throws<InjectedBootstrapFailure>();
        fixture.Configuration["INSTANCE_BOOTSTRAP_MODE"] = "invalid-after-completion";
        fixture.Secrets.Status = SecretResolutionStatus.Unavailable;
        await fixture.RunAsync(prepare: false);
        await fixture.AssertCompletedAsync();
    }

    [Test]
    public async Task CompletedSetupCannotReplayCredentialIssuance()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.RunAsync();
        fixture.Configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Guid.CreateVersion7().ToString("D");
        fixture.Configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2";
        fixture.Secrets.Password = NewPassword();
        await fixture.RunAsync();
        await fixture.AssertCompletedAsync();
    }

    [Test]
    public async Task ConcurrentSuppliedEmailCannotCreateTwoDifferentLocalSubjects()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<LocalCredentialCreateResult> Create(string username) => Task.Run(async () =>
        {
            await using var scope = fixture.Provider.CreateAsyncScope();
            await start.Task.WaitAsync(fixture.Token);
            return await fixture.Store(scope).CreateBootstrapPendingAsync(new LocalCredentialCreateRequest(
                Guid.CreateVersion7(), fixture.Subject, "same@example.test", "Initial", "Administrator", username),
                Guid.CreateVersion7(), fixture.OriginalPassword, fixture.Token);
        });
        Task<LocalCredentialCreateResult> first = Create("first-operator");
        Task<LocalCredentialCreateResult> second = Create("second-operator");
        start.SetResult();
        LocalCredentialCreateResult[] results = await Task.WhenAll(first, second).WaitAsync(fixture.Token);
        await Assert.That(results.Count(result => result.Outcome == LocalCredentialCreateOutcome.Created)).IsEqualTo(1);
        await Assert.That(results.Count(result => result.Outcome == LocalCredentialCreateOutcome.Conflict)).IsEqualTo(1);
    }

    [Test]
    public async Task ConfiguredBootstrapWithoutEmailKeepsTheCanonicalUsernameAndExactBinding()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        fixture.Configuration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = null;
        await fixture.RunAsync();
        await fixture.AssertCompletedAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
        await Assert.That(identities.Items.Single().Email).IsNull();
    }

    [Test]
    public async Task SetupAuthorityCreatesEmailLessAdministratorAndCompletesBeforeReplacement()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        Guid operationId = Guid.CreateVersion7();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: ApiAuthenticationSchemeNames.SetupSecret));
        var result = await fixture.Operation(scope).CompleteInteractiveAsync(operationId, "wizard-operator", fixture.OriginalPassword,
            null, null, null, WizardSettings(), principal, fixture.Token);
        await Assert.That(result.IsSuccess).IsTrue();
        var receipt = await fixture.Store(scope).ReadOperationAsync(operationId, fixture.Token);
        await Assert.That(receipt!.Receipt.LocalSubjectId).IsNotEqualTo(operationId);
        await fixture.AssertCompletedAsync(receipt.Receipt.LocalSubjectId);
        var second = await fixture.Operation(scope).CompleteInteractiveAsync(Guid.CreateVersion7(), "another-operator", NewPassword(),
            null, null, null, WizardSettings(), principal, fixture.Token);
        await Assert.That(second.IsSuccess).IsFalse();
        await fixture.AssertCompletedAsync(receipt.Receipt.LocalSubjectId);
    }

    [Test]
    public async Task ConfiguredPasswordBeyondLoginLimitCannotCreateAnUnusableAdministrator()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        fixture.Secrets.Password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(80))}";
        await Assert.That(fixture.RunAsync).Throws<ConfiguredAdministratorBootstrapException>();
        await using var scope = fixture.Provider.CreateAsyncScope();
        await Assert.That((await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token)).TotalCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("Bearer")]
    [Arguments("Cookies")]
    [Arguments("")]
    public async Task OrdinaryOrAnonymousPrincipalCannotEnrollThroughSetup(string authenticationType)
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: authenticationType));
        var result = await fixture.Operation(scope).CompleteInteractiveAsync(Guid.CreateVersion7(), "operator", fixture.OriginalPassword,
            null, null, null, WizardSettings(), principal, fixture.Token);
        await Assert.That(result.IsSuccess).IsFalse();
        var identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
        await Assert.That(identities.TotalCount).IsEqualTo(0);
        await Assert.That(await new InstanceBootstrapStateRepository(fixture.Application(scope)).GetCurrent(fixture.Token)).IsNull();
    }

    [Test]
    public async Task FailureDuringApplicationGrantRollsBackLinkageAndRetainsPendingCredential()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        fixture.GrantFault.Armed = true;
        await Assert.That(fixture.RunAsync).Throws<InjectedBootstrapFailure>();
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            var identities = await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token);
            await Assert.That(identities.Items.Single().CredentialState).IsEqualTo(LocalCredentialState.ProvisioningPending);
            await Assert.That(identities.Items.Single().HasExactBinding).IsFalse();
            await Assert.That(await new UserRepository(fixture.Application(scope)).GetById(fixture.Subject)).IsNull();
        }
        await fixture.RunAsync();
        await fixture.AssertCompletedAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Review_NonLocalPrimaryDeniesCoreEnrollmentBeforeMutation(bool configured)
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var application = fixture.Application(scope);
        var unitOfWork = new EfCoreUnitOfWork(application);
        IVisitorAccessSettingsWriter settings = new VisitorAccessSettingsWriter(application,
            new RelationalSettingMutationLock(application, unitOfWork), unitOfWork,
            new EventParticipationConfigurationRepository(application), fixture.Configuration);
        (await settings.ApplyAsync(
            [new(null, GovernanceSettingKeys.Authentication.PrimaryProviderId,
                VisitorAccessSettingMutationKind.SetValue, JsonSerializer.Serialize((int)AuthenticationProviderKind.Keycloak))],
            actorUserId: null, fixture.Token)).EnsureAccepted();
        if (configured)
        {
            await fixture.PrepareAsync();
            var result = await fixture.Operation(scope).CompleteConfiguredAsync(
                new Explore.Application.Authentication.ProviderAccountKey(AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token);
            await Assert.That(result.IsSuccess).IsFalse();
        }
        else
        {
            var result = await fixture.Operation(scope).CompleteInteractiveAsync(Guid.CreateVersion7(), "operator", fixture.OriginalPassword,
                null, null, null, WizardSettings(), SetupPrincipal(), fixture.Token);
            await Assert.That(result.IsSuccess).IsFalse();
        }
        await Assert.That((await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token)).TotalCount).IsEqualTo(0);
        await Assert.That(await application.PlatformUserRoles.AnyAsync(fixture.Token)).IsFalse();
        var bootstrap = await new InstanceBootstrapStateRepository(application).GetCurrent(fixture.Token);
        await Assert.That(bootstrap?.Status == InstanceBootstrapStatus.Completed).IsFalse();
        if (!configured) await Assert.That(bootstrap).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Review_DefinitivelyInvalidNativePolicyDoesNotReserveTheWizardOperation(bool invalidUsername)
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await using var scope = fixture.Provider.CreateAsyncScope();
        string password = invalidUsername ? fixture.OriginalPassword : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var rejected = await fixture.Operation(scope).CompleteInteractiveAsync(Guid.CreateVersion7(),
            invalidUsername ? "operator name" : "operator", password,
            null, null, null, WizardSettings(), SetupPrincipal(), fixture.Token);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(await new InstanceBootstrapStateRepository(fixture.Application(scope)).GetCurrent(fixture.Token)).IsNull();
        await Assert.That((await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token)).TotalCount).IsEqualTo(0);
        Guid freshOperation = Guid.CreateVersion7();
        var accepted = await fixture.Operation(scope).CompleteInteractiveAsync(freshOperation, "fresh-operator", fixture.OriginalPassword,
            null, null, null, WizardSettings(), SetupPrincipal(), fixture.Token);
        await Assert.That(accepted.IsSuccess).IsTrue();
        var receipt = await fixture.Store(scope).ReadOperationAsync(freshOperation, fixture.Token);
        await fixture.AssertCompletedAsync(receipt!.Receipt.LocalSubjectId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Review_RetryableCreationConflictConvergesWithFreshTrackingAndReceipt(bool afterCommit)
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        if (afterCommit)
        {
            fixture.IdentityCommitFault.Armed = true;
            fixture.IdentityCommitFault.Retryable = true;
        }
        else fixture.CreationFault.FailuresRemaining = 1;
        await fixture.RunAsync(prepare: false);
        await fixture.AssertCompletedAsync();
        if (!afterCommit) await Assert.That(fixture.CreationFault.FailuresObserved).IsEqualTo(1);
    }

    [Test]
    public async Task Review_RepeatedCreationConflictsStopAtTheExistingBoundedRetryLimit()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        fixture.CreationFault.FailuresRemaining = 6;
        await Assert.That(() => fixture.RunAsync(prepare: false)).Throws<SqliteException>();
        await Assert.That(fixture.CreationFault.FailuresObserved).IsEqualTo(4);
        await using var scope = fixture.Provider.CreateAsyncScope();
        await Assert.That((await fixture.Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), fixture.Token)).TotalCount).IsEqualTo(0);
    }

    private static ClaimsPrincipal SetupPrincipal() => new(new ClaimsIdentity(authenticationType: ApiAuthenticationSchemeNames.SetupSecret));

    private static CompleteInstanceOnboardingRequest WizardSettings() => new()
    {
        DeploymentMode = DeploymentMode.MultiTenant,
        SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Wizard Operator", Locale = "en", TimeZone = "UTC" }
    };

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"local-bootstrap-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"local-bootstrap-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private IdentityDatabaseTopology _topology;
        internal ServiceProvider Provider { get; private set; } = null!;
        internal Guid Subject { get; } = Guid.CreateVersion7();
        internal IConfigurationRoot Configuration { get; private set; } = null!;
        internal DeploymentSecrets Secrets { get; } = new();
        internal string OriginalPassword { get; private set; } = null!;
        internal CommitFault IdentityCommitFault { get; } = new();
        internal CommitFault ApplicationCommitFault { get; } = new();
        internal GrantWriteFault GrantFault { get; } = new();
        internal CreationConflictFault CreationFault { get; } = new();
        internal CancellationToken Token => _timeout.Token;

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync();
            var fixture = new Fixture { _topology = topology };
            try { await fixture.InitializeAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal ExploreDbContext Application(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal LocalIdentityCredentialStateStore Store(AsyncServiceScope scope) => new(
            _topology == IdentityDatabaseTopology.External ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : Application(scope),
            Application(scope), scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), TimeProvider.System);

        private ConfiguredAdministratorBootstrapProvider BootstrapProvider(AsyncServiceScope scope) => new(
            Configuration,
            InstanceOperatorIdentity.Create(new InstanceOperatorIdentityOptions
            {
                OperatorId = Subject,
                PublicName = "Bootstrap Operator",
                LegalName = "Bootstrap Operator ASBL",
                OfficialOrigin = "https://example.test",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                RegistrationIdentifier = "BE 0123.456.789",
                PublicContactEmail = "contact@example.test",
                WebsiteUrl = "https://example.test",
                LegalNoticeUrl = "https://example.test/legal",
                TermsUrl = "https://example.test/terms",
                PrivacyUrl = "https://example.test/privacy"
            }), new InstanceBootstrapStateRepository(Application(scope)));

        internal async Task PrepareAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            await new ConfiguredAdministratorBootstrapStartupRunner(BootstrapProvider(scope),
                new InstanceBootstrapStateRepository(Application(scope)), new EfCoreUnitOfWork(Application(scope)), TimeProvider.System)
                .PrepareAsync(Token);
        }

        internal Task RunAsync() => RunAsync(prepare: true);
        internal async Task RunAsync(bool prepare)
        {
            if (prepare) await PrepareAsync();
            await using var scope = Provider.CreateAsyncScope();
            await new LocalAdministratorBootstrapRunner(BootstrapProvider(scope),
                new InstanceBootstrapStateRepository(Application(scope)), Operation(scope)).RunAsync(Token);
        }

        internal LocalAdministratorBootstrapOperation Operation(AsyncServiceScope scope)
        {
            var application = Application(scope);
            var unitOfWork = new EfCoreUnitOfWork(application);
            var bootstrap = new InstanceBootstrapStateRepository(application);
            var provider = BootstrapProvider(scope);
            var platformRoles = new PlatformUserRoleRepository(application);
            var tenantRoles = new TenantUserRoleGrantRepository(application);
            var tenants = new TenantRepository(application);
            var documents = new TenantSettingsDocumentRepository(application);
            var logins = new UserExternalLoginRepository(application);
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            var setup = scope.ServiceProvider.GetRequiredService<ISetupSecretProvider>();
            var deployment = scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>();
            var completion = new InstanceOnboardingCompletionOperation(bootstrap, platformRoles, tenantRoles,
                new TenantUserRepository(application), new RoleRepository(application), new UserRepository(application),
                new ActorRepository(application), logins, tenants, new TenantCreationService(tenants, documents), documents,
                new SystemSettingRepository(application, new RelationalSettingMutationLock(application, unitOfWork)),
                [provider], setup, new InstanceBootstrapAuditLogger(NullLogger<InstanceBootstrapAuditLogger>.Instance),
                new AdminContext(new HttpContextAccessor(), platformRoles, tenantRoles,
                    new OrganizationMemberRepository(application), new GroupMemberRepository(application), logins, cache,
                    NullLogger<AdminContext>.Instance), deployment, new RuntimeMetadataRefresh(),
                new TenantBrandingSettingsDocumentProvisioningService(tenants, documents, new TypedSettingsDocumentResolver(documents, cache)),
                NullLogger<InstanceOnboardingCompletionOperation>.Instance, unitOfWork);
            return new LocalAdministratorBootstrapOperation(bootstrap, provider, Store(scope), Secrets, completion,
                setup, deployment, unitOfWork, TimeProvider.System,
                new RuntimeAuthenticationProviderDispatcher(
                    new SystemSettingRepository(application, new RelationalSettingMutationLock(application, unitOfWork)),
                    cache, Options.Create(new AuthenticationProviderDeploymentOptions())));
        }

        internal async Task AssertCompletedAsync(Guid? expectedSubject = null)
        {
            Guid subject = expectedSubject ?? Subject;
            await using var scope = Provider.CreateAsyncScope();
            var current = await new InstanceBootstrapStateRepository(Application(scope)).GetCurrent(Token);
            await Assert.That(current!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            await Assert.That(current.CompletedByUserId).IsEqualTo(subject);
            var identities = await Store(scope).ListAsync(new LocalIdentityListRequest(1, 10), Token);
            await Assert.That(identities.TotalCount).IsEqualTo(1);
            var identity = identities.Items.Single();
            await Assert.That(identity.LocalSubjectId).IsEqualTo(subject);
            await Assert.That(identity.CredentialState).IsEqualTo(LocalCredentialState.ChangeRequired);
            await Assert.That(identity.CurrentOperationId).IsEqualTo(current.Id);
            await Assert.That(identity.HasExactBinding).IsTrue();
            await Assert.That(identity.EmailVerified).IsTrue();
            await Assert.That(await new PlatformUserRoleRepository(Application(scope)).IsUserPlatformAdmin(subject)).IsTrue();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            var user = await manager.FindByIdAsync(subject.ToString("D"));
            await Assert.That(user).IsNotNull();
            await Assert.That(await manager.CheckPasswordAsync(user!, OriginalPassword)).IsTrue();
        }

        private async Task InitializeAsync()
        {
            OriginalPassword = Secrets.Password;
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
                ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "local",
                ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Subject.ToString("D"),
                ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
                ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "bootstrap@example.test",
                ["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = "Initial",
                ["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = "Administrator",
                ["Deployment:Mode"] = "MultiTenant",
                ["SETUP_SECRET"] = NewPassword(),
                ["SETUP_SECRET_FILE"] = _applicationPath + ".setup"
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddDistributedMemoryCache();
            services.AddSingleton<IConfiguration>(Configuration);
            services.Configure<DeploymentSettings>(Configuration.GetSection("Deployment"));
            services.AddSingleton<ISetupSecretProvider, SetupSecretProvider>();
            services.AddSingleton<IDeploymentModeProvider, DeploymentModeProvider>();
            services.AddScoped<IInstanceBootstrapStateRepository, InstanceBootstrapStateRepository>();
            services.AddDbContext<ExploreDbContext>(options =>
            {
                Configure(options, _applicationPath, ApplicationCommitFault);
                options.AddInterceptors(GrantFault);
            });
            var identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
                .AddUserValidator<OptionalEmailLocalIdentityUserValidator>();
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options =>
                {
                    Configure(options, _identityPath, IdentityCommitFault);
                    options.AddInterceptors(CreationFault);
                });
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            Provider = services.BuildIsolatedServiceProvider();
            await LocalIdentitySqliteTemplate.CopyAsync(_applicationPath,
                _topology == IdentityDatabaseTopology.External ? _identityPath : null, seedLookups: true, Token);
        }

        private void Configure(DbContextOptionsBuilder options, string path, CommitFault fault) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseMemoryCache(_metadataCache)
            .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, fault);

        public async ValueTask DisposeAsync()
        {
            if (Provider is not null) await Provider.DisposeAsync();
            _metadataCache.Dispose();
            foreach (string path in new[] { _applicationPath, _identityPath })
            { File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm"); }
            _timeout.Dispose();
        }
    }

    private sealed class DeploymentSecrets : ISecretResolver
    {
        internal string Password { get; set; } = NewPassword();
        internal SecretResolutionStatus Status { get; set; } = SecretResolutionStatus.Resolved;
        internal SecretReadBarrier? Barrier { get; set; }
        public async Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId = null, CancellationToken cancellationToken = default)
        {
            if (Barrier is not null) await Barrier.WaitAsync(cancellationToken);
            return Status switch
            {
                SecretResolutionStatus.Unconfigured => SecretResolutionResult.Unconfigured,
                SecretResolutionStatus.Unavailable => SecretResolutionResult.Unavailable,
                _ => SecretResolutionResult.Resolved(new ResolvedSecret(settingKey, Password,
                    SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow))
            };
        }
        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId,
            string qualifier, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SecretReadBarrier
    {
        private int _entered;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task WaitAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref _entered) == 2) Entered.TrySetResult();
            await Release.Task.WaitAsync(token);
        }
    }

    // API-owned OIDC metadata reload is an external host callback; this fixture has no OIDC provider.
    private sealed class RuntimeMetadataRefresh : IJwtAuthorityRefreshNotifier
    {
        public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class GrantWriteFault : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<PlatformUserRole>().Any(entry => entry.State == EntityState.Added))
            { Armed = false; throw new InjectedBootstrapFailure(); }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CreationConflictFault : SaveChangesInterceptor
    {
        internal int FailuresRemaining { get; set; }
        internal int FailuresObserved { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (FailuresRemaining > 0 && eventData.Context!.ChangeTracker.Entries<LocalIdentityCredentialOperation>()
                    .Any(entry => entry.State == EntityState.Added))
            {
                FailuresRemaining--;
                FailuresObserved++;
                throw new SqliteException("injected creation conflict", 5);
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class InjectedBootstrapFailure : Exception;
    private sealed class CommitFault : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Retryable { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                if (Retryable) throw new SqliteException("injected commit acknowledgement conflict", 5);
                throw new InjectedBootstrapFailure();
            }
            return Task.CompletedTask;
        }
    }
}
