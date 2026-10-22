using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Identity;
using Explore.Infrastructure.Authentication;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalBootstrapConvergenceTests
{
    [Test]
    public async Task ConfiguredSync_CoLocatedIdentityFenceRetainsEnrollmentThroughPhysicalCommit()
    {
        await using var scenario = await CompletionFixture.CreateAsync(colocatedIdentityFence: true);
        if (scenario.Context.BindTenantLease() is not null)
            throw new InvalidOperationException("Configured enrollment requires the original instance-scoped test owner.");
        scenario.Context.Tenants.Add(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            FullName = "Provisioning community",
            Slug = PlatformDefaults.DefaultTenantSlug,
            TenantStatusId = (int)TenantStatusEnum.Provisioning,
            TenantStatus = null!
        });
        scenario.Context.Set<TenantSettingsDocument>().AddRange(
            TenantBrandingSettingsDocumentDefaults.Create(PlatformDefaults.DefaultTenantId, "Provisioning community"),
            TenantDirectoryOperatorIdentityDocumentDefaults.Create(PlatformDefaults.DefaultTenantId));
        await scenario.Context.SaveChangesAsync();
        scenario.Context.ChangeTracker.Clear();
        var gate = new EventDiscoveryEnrollmentGate(scenario.Context);
        bool originalCommitObserved = false;
        scenario.AfterCommit = () =>
        {
            gate.RequireCurrent();
            originalCommitObserved = scenario.Context.IdentityFenceOwnsTransaction
                && scenario.Context.Database.CurrentTransaction is not null;
        };
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var cacheServices = services.BuildServiceProvider();

        var response = await scenario.SyncAsync(cacheServices.GetRequiredService<HybridCache>());

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(originalCommitObserved).IsTrue();
        await Assert.That(scenario.Context.Database.CurrentTransaction).IsNull();
        var bootstrap = await scenario.ReadBootstrapAsync();
        await Assert.That(bootstrap!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        var login = await new UserExternalLoginRepository(scenario.Context).GetByProviderAndKey(scenario.Account);
        await Assert.That(login).IsNotNull();
        await Assert.That(login!.UserId).IsEqualTo(response.Id);
        await Assert.That(bootstrap.CompletedByUserId).IsEqualTo(login.UserId);
        await Assert.That(await new PlatformUserRoleRepository(scenario.Context).IsUserPlatformAdmin(login.UserId)).IsTrue();
        await Assert.That(await scenario.CountDefaultTenantDiscoveryRevisionsAsync()).IsEqualTo(1);
        await using var nextAttempt = await gate.AcquireAsync();
        gate.RequireCurrent();
    }

    /// <summary>
    /// The request fixture owns JWT validation; the bootstrap fixture owns the new application's
    /// database. Enrollment starts without an application user, just as a first OIDC login does.
    /// Neither authentication receipts nor settings admissions are constructed by this fixture.
    /// </summary>
    private sealed class CompletionFixture : IAsyncDisposable
    {
        private readonly Fixture database;
        private readonly AsyncServiceScope scope;
        private readonly CurrentHumanPrincipalFixture? human;
        private readonly SqliteConnection privacyConnection = new("Data Source=:memory:");
        private readonly bool interactive;
        private readonly bool colocatedIdentityFence;
        private readonly CompletionObservation observation;
        private readonly IOptions<InstanceOperatorIdentityOptions> operatorOptions;
        private readonly List<string> events = [];
        private readonly EfCoreUnitOfWork owner;
        private readonly ConfiguredAdministratorBootstrapProvider provider;
        private readonly ObservedSetupSecret setup;
        private readonly ObservedDeployment deployment;
        private readonly ObservedMetadata metadata;
        private readonly IAuthenticationProviderDispatcher dispatcher;
        private IdentityAuthorityEvidence? identityEvidence;
        private InstanceOnboardingCompletionOperation operation = null!;
        private PrivacyIdentityFenceOperation fence = null!;

        private CompletionFixture(Fixture database, CompletionObservation observation,
            CurrentHumanPrincipalFixture? human, bool interactive, AuthenticationProviderKind providerKind,
            bool legalIdentity, DeploymentMode mode, bool colocatedIdentityFence)
        {
            this.database = database;
            this.observation = observation;
            this.human = human;
            this.interactive = interactive;
            this.colocatedIdentityFence = colocatedIdentityFence;
            scope = database.Provider.CreateAsyncScope();
            Context = database.Application(scope);
            owner = new EfCoreUnitOfWork(Context);
            BootstrapRepository = new InstanceBootstrapStateRepository(Context);
            Users = new UserRepository(Context);
            Settings = SystemSettingRepository.CreateReadOnly(Context, new RelationalSettingMutationLock(Context, owner));
            Configuration["Deployment:Mode"] = mode.ToString();
            Configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "7";
            Configuration["PublicBaseUrl"] = "https://example.test";
            operatorOptions = Options.Create(legalIdentity
                ? new InstanceOperatorIdentityOptions
                {
                    OperatorId = UserId,
                    PublicName = "Completion Operator",
                    LegalName = "Completion Operator ASBL",
                    OfficialOrigin = "https://example.test",
                    OperatorKindCode = "registered_organization",
                    JurisdictionCountryCode = "BE",
                    RegistrationIdentifier = "BE 0123.456.789",
                    PublicContactEmail = "contact@example.test",
                    WebsiteUrl = "https://example.test",
                    LegalNoticeUrl = "https://example.test/legal",
                    TermsUrl = "https://example.test/terms",
                    PrivacyUrl = "https://example.test/privacy"
                }
                : new InstanceOperatorIdentityOptions());
            provider = new ConfiguredAdministratorBootstrapProvider(Configuration, operatorOptions, BootstrapRepository);
            setup = new ObservedSetupSecret(scope.ServiceProvider.GetRequiredService<ISetupSecretProvider>(), events);
            deployment = new ObservedDeployment(scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>(), events);
            metadata = new ObservedMetadata(events);
            dispatcher = new RuntimeAuthenticationProviderDispatcher(Settings,
                scope.ServiceProvider.GetRequiredService<IMemoryCache>(),
                Options.Create(new AuthenticationProviderDeploymentOptions()));
            Generation = new InstanceOnboardingGenerationReader(Settings, deployment, BootstrapRepository, Configuration);
            observation.Events = events;
        }

        internal ExploreDbContext Context { get; }
        internal IServiceProvider RequestServices => human!.Services;
        internal InstanceBootstrapStateRepository BootstrapRepository { get; }
        internal UserRepository Users { get; }
        internal SystemSettingRepository Settings { get; }
        internal IInstanceOnboardingGenerationReader Generation { get; }
        internal IConfigurationRoot Configuration => database.Configuration;
        internal Guid UserId => database.Subject;
        internal ProviderAccountKey Account { get; private set; } = null!;
        internal IReadOnlyList<string> PostCommitEffects => events.Where(item => item != "commit").ToArray();
        internal IReadOnlyList<string> EventSequence => events;
        internal IReadOnlyList<string> CommittedWrites => observation.CommittedWrites;
        internal IReadOnlyList<string> AttemptedWrites => observation.PendingWrites;
        internal bool JwtCancellationWasRequested => metadata.Canceled;
        internal int ExternalReadinessCallsInsideTransaction { get; private set; }
        internal int ExternalReadinessCallsOutsideTransaction { get; private set; }
        internal int? FailAtWrite { set => observation.FailAtWrite = value; }
        internal Action? BeforeCommit { set => observation.BeforeCommit = value; }
        internal Action? AfterCommit { get; set; }
        internal Func<Task>? OnMetadataRefresh { set => metadata.BeforeReload = value; }

        internal static async Task<CompletionFixture> CreateAsync(
            bool interactive = false, AuthenticationProviderKind providerKind = AuthenticationProviderKind.Keycloak,
            bool legalIdentity = true, DeploymentMode mode = DeploymentMode.SingleTenant,
            bool colocatedIdentityFence = false)
        {
            var observation = new CompletionObservation();
            var identityObservation = new CompletionIdentityCommitObservation();
            var database = await Fixture.CreateAsync(IdentityDatabaseTopology.External,
                [observation, new CompletionTransactionObservation(observation)], [identityObservation]);
            CurrentHumanPrincipalFixture? human = providerKind == AuthenticationProviderKind.Keycloak
                ? new CurrentHumanPrincipalFixture(database.Subject, Guid.CreateVersion7())
                : null;
            var fixture = new CompletionFixture(database, observation, human, interactive, providerKind, legalIdentity, mode,
                colocatedIdentityFence);
            try
            {
                await fixture.InitializeAsync();
                identityObservation.AfterCommit = async () =>
                {
                    var bootstrap = await fixture.ReadBootstrapAsync();
                    if (bootstrap is not null
                        && (await fixture.ReadLocalProvisioningAsync(bootstrap.Id))?.Receipt.Stage
                            == LocalCredentialOperationStage.ChangeRequired)
                        fixture.events.Add("credential-activation");
                };
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        private async Task InitializeAsync()
        {
            IHttpContextAccessor contexts;
            IEnumerable<IHumanAuthenticationLifetimeObserver> lifetimes;
            if (human is not null)
            {
                await human.InitializeAsync();
                contexts = human.Services.GetRequiredService<IHttpContextAccessor>();
                var principal = contexts.HttpContext!.User;
                var identity = principal.GetProviderIdentity()
                    ?? throw new InvalidOperationException("The signed OIDC fixture must expose its provider account.");
                Account = identity.AccountKey;
                identityEvidence = identity.AuthorityEvidence;
                Configuration["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "keycloak";
                Configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = identity.Subject;
                Configuration["Keycloak:Authority"] = identity.AuthorityEvidence!.Issuer;
                Configuration["Keycloak:ClientId"] = "event";
                contexts.HttpContext.Request.Headers["X-Setup-Secret"] = Configuration["SETUP_SECRET"];
                lifetimes = human.Services.GetServices<IHumanAuthenticationLifetimeObserver>();
            }
            else
            {
                Account = new ProviderAccountKey(AuthenticationProviderKind.Local, UserId.ToString("D"));
                contexts = new HttpContextAccessor();
                lifetimes = [];
            }

            Context.BindTenantLease();
            if (interactive)
                await BootstrapRepository.Create(InstanceBootstrapState.CreateInteractivePending(
                    Guid.CreateVersion7(), await deployment.GetConfiguredOnboardingModeAsync(), DateTime.UtcNow));
            else
                await new ConfiguredAdministratorBootstrapStartupRunner(provider, BootstrapRepository, owner, TimeProvider.System,
                    new EventDiscoveryEnrollmentGate(Context))
                    .PrepareAsync(database.Token);
            await setup.InitializeAsync(database.Token);

            var privacyOptions = new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>();
            if (colocatedIdentityFence)
                EmbeddedPrivacyErasureAuthorityDbContextFactory.ConfigureCoLocated(privacyOptions,
                    new PrimaryDatabaseConnectionOptions
                    {
                        Provider = PrimaryDatabaseProvider.Sqlite,
                        Role = PrimaryDatabaseRole.Runtime,
                        Database = new SqliteConnectionStringBuilder(Context.Database.GetConnectionString()).DataSource
                    });
            else
            {
                await privacyConnection.OpenAsync();
                privacyOptions.UseSqlite(privacyConnection).UseSnakeCaseNamingConvention();
            }
            privacyOptions.AddInterceptors(new CompletionPrivacyCommitObservation(() => AfterCommit?.Invoke()));
            var privacyFactory = new CompletionPrivacyFactory(privacyOptions.Options);
            await using (var privacy = privacyFactory.CreateDbContext())
            {
                if (colocatedIdentityFence) await privacy.Database.MigrateAsync();
                else await privacy.Database.EnsureCreatedAsync();
            }
            fence = new PrivacyIdentityFenceOperation(new EmbeddedPrivacyErasureAuthorityRepository(
                privacyFactory, TimeProvider.System, Options.Create(new PrivacyErasureOptions()),
                applicationContext: colocatedIdentityFence ? Context : null));
            var mutationLock = new RelationalSettingMutationLock(Context, owner);
            var receipts = scope.ServiceProvider.GetRequiredService<SetupReceiptCustody>().Source;
            var authority = new TrustedSetupSettingsAuthority(contexts, lifetimes, setup,
                receipts,
                dispatcher, [provider], TimeProvider.System);
            var writer = new TrustedSetupSettingsWriter(Context, authority, mutationLock, owner);
            var logins = new UserExternalLoginRepository(Context);
            var emails = new UserIdentityEmailRepository(Context);
            var privacyState = new PrivacyErasureStateRepository(Context);
            var tenants = new TenantRepository(Context);
            operation = new InstanceOnboardingCompletionOperation(BootstrapRepository,
                new PlatformUserRoleRepository(Context),
                new RoleRepository(Context), Users, new ActorRepository(Context),
                logins, emails, privacyState,
                new IdentityAccountResolver(Users, logins, emails, privacyState, Options.Create(new IdentityCorrelationOptions())),
                new IdentityEmailSynchronizationOperation(emails),
                Settings, [provider], setup, new ObservedAudit(events), deployment, metadata,
                NullLogger<InstanceOnboardingCompletionOperation>.Instance, owner, Generation, Configuration,
                fence, new Explore.Persistence.Services.EventDiscoveryEnrollmentGate(Context), receipts, operatorOptions, writer);
            SaveProfile = new SaveInstanceOnboardingProfileCommandHandler(BootstrapRepository, Settings,
                setup, new ObservedAudit(events), owner, Configuration,
                new Explore.Persistence.Services.EventDiscoveryEnrollmentGate(Context), writer);
            ResetObservation();
        }

        internal SaveInstanceOnboardingProfileCommandHandler SaveProfile { get; private set; } = null!;

        internal Task<BaseCommandResponse<Guid>> SyncAsync(HybridCache cache)
        {
            human!.EnterRequest();
            observation.Reset();
            var tenantContext = new TenantContextAccessor(human.Services.GetRequiredService<IHttpContextAccessor>());
            tenantContext.SetTenant(PlatformDefaults.DefaultTenantId);
            var logins = new UserExternalLoginRepository(Context);
            var emails = new UserIdentityEmailRepository(Context);
            var lifecycle = new TenantLifecycleAccessService(new TenantRepository(Context), human.Admin,
                BootstrapRepository, new TenantLookupSource(
                    scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>()));
            return new SyncUserCommandHandler(Users, logins,
                new IdentityAccountResolver(Users, logins, emails, new PrivacyErasureStateRepository(Context),
                    Options.Create(new IdentityCorrelationOptions())),
                emails, new IdentityEmailSynchronizationOperation(emails), new ActorRepository(Context),
                tenantContext, lifecycle, new Explore.Persistence.Services.IdentitySynchronizationBootstrapReader(Context), operation, cache, owner,
                NullLogger<SyncUserCommandHandler>.Instance, fence, new EventDiscoveryEnrollmentGate(Context),
                new Explore.Persistence.Security.HumanSecuritySourceGate(Context))
                .ExecuteAsync(new SyncUserCommand
                {
                    AccountKey = Account,
                    AuthorityEvidence = identityEvidence,
                    UserDto = new()
                    {
                        Email = "adapter@example.test",
                        FirstName = "Adapter",
                        LastName = "User",
                        AuthProvider = "keycloak"
                    }
                }, database.Token);
        }

        // HttpContextAccessor uses AsyncLocal: install in the caller before returning its task.
        internal Task<BaseCommandResponse<Guid>> ClaimAsync(Guid? userId = null, CancellationToken cancellationToken = default)
        {
            human?.EnterRequest();
            observation.Reset();
            return new ClaimConfiguredInstanceAdministratorCommandHandler(operation).ExecuteAsync(new()
            {
                AuthenticatedAccount = Account,
                UserId = userId ?? UserId,
                Email = "adapter@example.test",
                FirstName = "Adapter",
                LastName = "User",
                EmailVerified = identityEvidence?.EmailVerified,
                AuthorityEvidence = identityEvidence
            }, cancellationToken);
        }

        internal Task<BaseCommandResponse<Guid>> CompleteProvisionedLocalAsync() =>
            new LocalAdministratorBootstrapOperation(BootstrapRepository, provider, new BootstrapLocalScopes(database),
                database.Secrets, operation, setup, deployment, owner, TimeProvider.System, dispatcher,
                new EventDiscoveryEnrollmentGate(Context))
                .CompleteConfiguredAsync(Account, database.Token);

        internal async Task<CompleteInstanceOnboardingCommand> InteractiveCommandAsync()
        {
            var smtp = NSubstitute.Substitute.For<ISmtpConfigResolver>();
            smtp.ResolveAsync(NSubstitute.Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (Context.Database.CurrentTransaction is null) ExternalReadinessCallsOutsideTransaction++;
                else ExternalReadinessCallsInsideTransaction++;
                return Task.FromResult<SmtpConfiguration?>(null);
            });
            await new GetOnboardingPreflightQueryHandler(BootstrapRepository, deployment, setup,
                new TenantRepository(Context), Settings, Configuration, dispatcher, smtpConfigResolver: smtp)
                .QueryAsync(new(), database.Token);
            return new()
            {
                UserId = UserId,
                Email = "interactive@example.test",
                FirstName = "Interactive",
                LastName = "Admin",
                AuthProvider = "keycloak",
                AuthProviderId = Account.Value,
                AuthorityEvidence = identityEvidence,
                Settings = new CompleteInstanceOnboardingRequest
                {
                    ExpectedJourneyGeneration = await Generation.ReadCurrentAsync(database.Token),
                    DeploymentMode = await deployment.GetConfiguredOnboardingModeAsync(),
                    SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Completion site" }
                }
            };
        }

        internal Task<BaseCommandResponse<Guid>> CompleteInteractiveAsync(CompleteInstanceOnboardingCommand command)
        {
            human!.EnterRequest();
            observation.Reset();
            return new CompleteInstanceOnboardingCommandHandler(BootstrapRepository, Users, deployment, operation)
                .ExecuteAsync(command, database.Token);
        }

        internal Task<BaseCommandResponse<Guid>> SaveProfileAsync(SelfHostOnboardingProfileDto profile)
        {
            human!.EnterRequest();
            return SaveProfile.ExecuteAsync(new() { Profile = profile }, database.Token);
        }

        internal Task<InstanceBootstrapState?> ReadBootstrapAsync() => BootstrapRepository.GetCurrent(database.Token);
        internal async Task<int> CountDefaultTenantDiscoveryRevisionsAsync()
        {
            // Observe committed metadata through the configured Global reader; the command's
            // original null tenant lease remains unchanged and cannot see tenant-filtered rows.
            await using var observer = await scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>()
                .CreateDbContextAsync(database.Token);
            return await observer.Set<EventDiscoveryRevision>().AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.Tenant])
                .CountAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId, database.Token);
        }
        internal Task<string> ReadSettingsAsync() => database.ReadSettingsAsync();
        internal Task<Tenant?> ReadTenantAsync() =>
            new TenantRepository(Context).GetById(Explore.Domain.Constants.PlatformDefaults.DefaultTenantId);
        internal async Task<LocalCredentialProvisioningSnapshot?> ReadLocalProvisioningAsync(Guid operationId)
        {
            await using var observer = database.Provider.CreateAsyncScope();
            database.Application(observer).BindTenantLease();
            return await database.Store(observer).ReadProvisioningAsync(operationId, database.Token);
        }
        internal Task AssertLocalCompletedAsync() => database.AssertCompletedAsync();

        internal void ResetObservation()
        {
            events.Clear();
            observation.Reset();
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            if (human is not null) await human.DisposeAsync();
            await privacyConnection.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private sealed class CompletionPrivacyFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class CompletionObservation : SaveChangesInterceptor
    {
        internal List<string> Events { get; set; } = [];
        internal List<string> CommittedWrites { get; } = [];
        internal List<string> PendingWrites { get; } = [];
        internal bool Enabled { get; private set; }
        internal int? FailAtWrite { get; set; }
        internal Action? BeforeCommit { get; set; }
        private int writes;

        internal void Reset()
        {
            Enabled = true;
            writes = 0;
            PendingWrites.Clear();
            CommittedWrites.Clear();
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                foreach (var entry in eventData.Context!.ChangeTracker.Entries()
                    .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
                {
                    string? category = entry.Entity switch
                    {
                        Tenant => "tenant",
                        PlatformUserRole => "platform-role",
                        TenantUserRoleGrant => "tenant-role",
                        SystemSetting => "system-setting",
                        InstanceBootstrapState => "bootstrap",
                        User => "user",
                        Actor => "actor",
                        UserExternalLogin => "external-login",
                        TenantSettingsDocument document => document.DocumentKey,
                        _ => null
                    };
                    if (category is not null) PendingWrites.Add(category);
                }
                writes++;
                // Keep both original failure positions: before the first persisted effect,
                // and late in the graph after tenant, user, login and platform-role writes.
                if (FailAtWrite == 1 && writes == 1
                    || FailAtWrite == 6 && eventData.Context.ChangeTracker.Entries<TenantUserRoleGrant>()
                        .Any(entry => entry.State == EntityState.Added))
                    throw new InjectedBootstrapFailure();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class CompletionTransactionObservation(CompletionObservation observation) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (observation.Enabled) observation.BeforeCommit?.Invoke();
            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (observation.Enabled)
            {
                observation.CommittedWrites.AddRange(observation.PendingWrites);
                observation.PendingWrites.Clear();
                observation.Events.Add("commit");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CompletionPrivacyCommitObservation(Action afterCommit) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            afterCommit();
            return Task.CompletedTask;
        }
    }

    private sealed class CompletionIdentityCommitObservation : DbTransactionInterceptor
    {
        internal Func<Task>? AfterCommit { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => AfterCommit?.Invoke() ?? Task.CompletedTask;
    }

    private sealed class ObservedSetupSecret(ISetupSecretProvider inner, List<string> events) : ISetupSecretProvider
    {
        public bool IsSetupModeActive => inner.IsSetupModeActive;
        public bool IsSetupSecretRequired => inner.IsSetupSecretRequired;
        public bool IsFromEnvironmentVariable => inner.IsFromEnvironmentVariable;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
        public bool ValidateSecret(string? secret) => inner.ValidateSecret(secret);
        public Task<SetupSecretValidationOutcome> ValidateSecretAsync(string? secret, CancellationToken cancellationToken = default) =>
            inner.ValidateSecretAsync(secret, cancellationToken);
        public Task<bool> IsSetupModeActiveAsync(CancellationToken cancellationToken = default) =>
            inner.IsSetupModeActiveAsync(cancellationToken);
        public void Lock() { inner.Lock(); events.Add("secret-lock"); }
    }

    private sealed class ObservedDeployment(IDeploymentModeProvider inner, List<string> events) : IDeploymentModeProvider
    {
        public Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default) => inner.GetCurrentModeAsync(ct);
        public Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default) => inner.GetConfiguredOnboardingModeAsync(ct);
        public Task<bool> IsSingleTenantAsync(CancellationToken ct = default) => inner.IsSingleTenantAsync(ct);
        public async Task InvalidateCacheAsync() { await inner.InvalidateCacheAsync(); events.Add("deployment-cache"); }
    }

    private sealed class ObservedMetadata(List<string> events) : IJwtAuthorityRefreshNotifier
    {
        internal bool Canceled { get; private set; }
        internal Func<Task>? BeforeReload { get; set; }
        public async Task ReloadAsync(CancellationToken ct = default)
        {
            if (BeforeReload is not null) await BeforeReload();
            Canceled = ct.IsCancellationRequested;
            events.Add("jwt-reload");
        }
    }

    private sealed class ObservedAudit(List<string> events) : IInstanceBootstrapAuditLogger
    {
        public void Log(InstanceBootstrapAuditEvent auditEvent) => events.Add("audit");
    }
}
