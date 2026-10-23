
using Event.Persistence.IntegrationTests.Identity;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Infrastructure;
using Explore.Infrastructure.Authentication;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class LocalLifecycleDeliveryFixture : IAsyncDisposable
{
    internal LocalCredentialFirstUseTests.Fixture Native { get; }
    internal ControlledSmtp Smtp { get; } = new();
    internal bool UseRealSmtp { get; set; }
    internal CancellationToken CancellationToken => Native.CancellationToken;
    private readonly ServiceProvider executionProvider;
    private LocalLifecycleDeliveryFixture(LocalCredentialFirstUseTests.Fixture native, ServiceProvider executionProvider)
    {
        Native = native;
        this.executionProvider = executionProvider;
    }

    internal static async Task<LocalLifecycleDeliveryFixture> CreateAsync(IdentityDatabaseTopology topology, bool verified = true,
        IInterceptor? interceptor = null)
    {
        var native = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology, interceptor);
        try
        {
            await native.IssueReadySessionAsync(verified);
            await using (var scope = native.Provider.CreateAsyncScope())
            {
                await EmailDispatchSqliteFixture.ApplyEmailSettingsAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
                    [new(null, GovernanceSettingKeys.Email.DeliveryEnabled, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "true"),
                     new(null, GovernanceSettingKeys.Email.SmtpHost, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"smtp.instance.test\""),
                     new(null, GovernanceSettingKeys.Email.FromAddress, Explore.Application.Contracts.Persistence.EmailDeliverySettingMutationKind.SetValue, "\"events@instance.test\"")],
                    cancellationToken: native.CancellationToken);
            }
            return new LocalLifecycleDeliveryFixture(native,
                await CreateExecutionProviderAsync(native, topology, interceptor));
        }
        catch { await native.DisposeAsync(); throw; }
    }

    internal Scope Open(Func<string, CancellationToken, Task>? beforeLock = null) => new(this, beforeLock);

    private static async Task<ServiceProvider> CreateExecutionProviderAsync(LocalCredentialFirstUseTests.Fixture native,
        IdentityDatabaseTopology topology, IInterceptor? interceptor)
    {
        await using var original = native.Provider.CreateAsyncScope();
        var application = original.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string applicationConnection = application.Database.GetConnectionString()!;
        string identityConnection = native.Identity(original).Database.GetConnectionString()!;
        var writer = new LocalIdentityWriterDbContextFactory(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.LocalIdentityWriter,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = new SqliteConnectionStringBuilder(applicationConnection).DataSource
        }, topology == IdentityDatabaseTopology.External
            ? new ExternalIdentityDatabaseDescriptor(PrimaryDatabaseProvider.Sqlite, identityConnection,
                IdentityDatabaseConfiguration.DefaultSchema, null, null)
            : null, poolSize: 1, interceptors: interceptor is null ? null : [interceptor]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(writer);
        services.AddSingleton<TimeProvider>(native.Clock);
        services.AddSingleton(native.Provider.GetRequiredService<IDataProtectionProvider>());
        services.AddPooledDbContextFactory<ExploreDbContext>(options => options
            .UseSqlite(applicationConnection).UseSnakeCaseNamingConvention());
        services.AddScoped<ExploreDbContextExecutionLease>();
        var identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
            .AddUserValidator<OptionalEmailLocalIdentityUserValidator>().AddLocalLifecycleTokenProviders();
        if (topology == IdentityDatabaseTopology.External)
        {
            services.AddDbContext<ExternalIdentityDbContext>(options => options
                .UseSqlite(identityConnection).UseSnakeCaseNamingConvention());
            identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
        }
        else identity.AddEntityFrameworkStores<ExploreDbContext>();
        services.AddLocalIdentityExecutionContextSelection(topology);
        services.AddScoped(provider => new LocalIdentityCredentialStateStore(
            Identity(provider), provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<UserManager<LocalIdentityUser>>(), native.Clock,
            NativeIdentityRecognitionBoundary.For(provider.GetRequiredService<ExploreDbContext>(), native.Clock),
            executionLease: provider.GetRequiredService<LocalIdentityExecutionLease>()));
        services.AddScoped<ILocalIdentityLifecycleStore>(provider => new LocalIdentityLifecycleStore(
            Identity(provider), provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<UserManager<LocalIdentityUser>>(), native.Clock,
            provider.GetRequiredService<LocalIdentityCredentialStateStore>(),
            NativeIdentityRecognitionBoundary.For(provider.GetRequiredService<ExploreDbContext>(), native.Clock)));
        services.AddScoped<ILocalIdentityLifecycleDeliveryStore>(provider => new LocalIdentityLifecycleDeliveryStore(
            Identity(provider), provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<LocalIdentityCredentialStateStore>(), native.Clock,
            NativeIdentityRecognitionBoundary.For(provider.GetRequiredService<ExploreDbContext>(), native.Clock)));
        return services.BuildIsolatedServiceProvider(validateScopes: true);

        DbContext Identity(IServiceProvider provider) => topology == IdentityDatabaseTopology.External
            ? provider.GetRequiredService<ExternalIdentityDbContext>()
            : provider.GetRequiredService<ExploreDbContext>();
    }

    internal sealed class Scope : IAsyncDisposable, ITenantContext
    {
        private readonly AsyncServiceScope _scope;
        private readonly AsyncServiceScope executionScope;
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        internal ExploreDbContext Application { get; }
        internal DbContext Identity { get; }
        internal LocalIdentityLifecycleStore Lifecycle { get; }
        internal LocalIdentityLifecycleDeliveryStore Deliveries { get; }
        internal RelationalSettingMutationLock MutationLock { get; }
        internal LocalIdentityLifecycleDeliveryProcessor Processor { get; }
        internal DefaultAccountAuthorityLifecycleEmailService Router { get; }
        public Guid TenantId { get; } = Guid.Empty;

        internal Scope(LocalLifecycleDeliveryFixture fixture, Func<string, CancellationToken, Task>? beforeLock)
        {
            _scope = fixture.Native.Provider.CreateAsyncScope();
            Application = _scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            Identity = fixture.Native.Identity(_scope);
            var states = fixture.Native.StateStore(_scope);
            Lifecycle = new(Identity, Application, _scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), fixture.Native.Clock, states,
                NativeIdentityRecognitionBoundary.For(Application, fixture.Native.Clock));
            Deliveries = new(Identity, Application, states, fixture.Native.Clock,
                NativeIdentityRecognitionBoundary.For(Application, fixture.Native.Clock));
            MutationLock = new(Application, new EfCoreUnitOfWork(Application), beforeLock);
            var settings = new HierarchicalSettingsReader(SystemSettingRepository.CreateReadOnly(Application, MutationLock),
                TenantSettingRepository.CreateReadOnly(Application, MutationLock), new OrganizationSettingRepository(Application),
                new GroupSettingRepository(Application), UserPreferenceRepository.CreateReadOnly(Application), _cache,
                new AtprotoFederationSettingRepository(Application, null));
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.ArgAt<Guid?>(1) is not null) throw new InvalidOperationException("Lifecycle requested tenant credentials.");
                return SecretResolutionResult.Unconfigured;
            });
            var capabilities = new EmailDeliveryCapabilityResolver(settings, secrets, new SecretBindingRepository(Application));
            ILocalIdentityLifecycleSmtpTransport transport = fixture.UseRealSmtp
                ? new LocalIdentityLifecycleSmtpTransport(new SmtpEmailService(new SmtpConfigResolver(capabilities, this, settings),
                    NullLogger<SmtpEmailService>.Instance)) : fixture.Smtp;
            Processor = new(Lifecycle, Deliveries, MutationLock, settings, capabilities, transport,
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://instance.test" }).Build(),
                SystemSettingRepository.CreateReadOnly(Application, MutationLock),
                Options.Create(new EmailDispatchProcessorSettings()));
            executionScope = fixture.executionProvider.CreateAsyncScope();
            Router = new(new UserExternalLoginRepository(Application),
                [new LocalIdentityLifecycleEmailService(
                    executionScope.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>())]);
        }

        public async ValueTask DisposeAsync()
        {
            _cache.Dispose();
            try { await executionScope.DisposeAsync(); }
            finally { await _scope.DisposeAsync(); }
        }
    }

    internal sealed class ControlledSmtp : ILocalIdentityLifecycleSmtpTransport
    {
        internal List<LocalIdentityLifecycleTransport> Handoffs { get; } = [];
        internal SmtpDeliveryOutcome Outcome { get; set; } = SmtpDeliveryOutcome.Accepted;
        internal Func<LocalIdentityLifecycleTransport, Guid, CancellationToken, Task>? OnSend { get; set; }
        public async Task<EmailResult> SendAsync(LocalIdentityLifecycleTransport handoff, Guid attemptId,
            Uri callbackUri, SmtpConfiguration configuration, CancellationToken cancellationToken)
        {
            if (configuration.Host != "smtp.instance.test") throw new InvalidOperationException("Incorrect transport ownership.");
            Handoffs.Add(handoff);
            if (OnSend is not null) await OnSend(handoff, attemptId, cancellationToken);
            return Outcome == SmtpDeliveryOutcome.Accepted ? EmailResult.Ok() : EmailResult.Fail("controlled", outcome: Outcome);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await executionProvider.DisposeAsync(); }
        finally { await Native.DisposeAsync(); }
    }
}
