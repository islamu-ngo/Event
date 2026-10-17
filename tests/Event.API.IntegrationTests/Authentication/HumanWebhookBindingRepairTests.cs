using System.Data;
using System.Data.Common;
using System.Globalization;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Real Local credentials, persisted requester sources, provider ownership classification and
/// selected physical settlement. Only Svix and Cerbos transports are substituted.
/// </summary>
[NotInParallel]
public sealed class HumanWebhookBindingRepairTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task AdmittedRepairOrBoundedProviderRefusalCommitsOnlyItsOwnEffect(
        bool existing, bool rejected)
    {
        var interceptor = new AuditBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing);
        int remoteCalls = 0;
        var client = Substitute.For<ISvixWebhookClient>();
        client.GetApplicationAsync("repair-application", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            remoteCalls++;
            if (request.Database.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Remote ownership must run without an owner.");
            return Application(seed, rejected);
        });
        interceptor.AfterSave = async (database, _) =>
        {
            await Assert.That(database).IsSameReferenceAs(request.Database);
            await Assert.That(database.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
        };
        var result = await Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsEqualTo(!rejected);
        await Assert.That(remoteCalls).IsEqualTo(1);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, !rejected, rejected);
        await Assert.That(request.Database.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("grant", false)]
    [Arguments("credential", false)]
    [Arguments("consumer", false)]
    [Arguments("binding", false)]
    [Arguments("bootstrap", false)]
    [Arguments("profile", false)]
    [Arguments("support", false)]
    [Arguments("support_extension", false)]
    [Arguments("grant", true)]
    [Arguments("credential", true)]
    [Arguments("consumer", true)]
    [Arguments("binding", true)]
    [Arguments("bootstrap", true)]
    [Arguments("profile", true)]
    [Arguments("support", true)]
    [Arguments("support_extension", true)]
    public async Task DriftDuringRemoteAwaitWithholdsRepairAndRejectedHistory(string drift, bool rejected)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing: true);
        Guid? support = drift is "support" or "support_extension" ? await StartSupportAsync(fixture, request) : null;
        var client = Substitute.For<ISvixWebhookClient>();
        int remoteCalls = 0;
        client.GetApplicationAsync("repair-application", Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            remoteCalls++;
            await Assert.That(request.Database.Database.CurrentTransaction).IsNull();
            await using var writer = fixture.Native.CreateDatabase();
            await DriftAsync(writer, fixture, options, seed, drift, support, CancellationToken.None);
            return Application(seed, rejected);
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None));
        await Assert.That(remoteCalls).IsEqualTo(1);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Set<WebhookAuditEvent>().CountAsync(row => row.EffectiveScopeId == seed.Consumer.OwnerId))
            .IsEqualTo(0);
        var binding = await observer.WebhookConsumerProviderBindings.AsNoTracking().SingleAsync(row => row.Id == seed.Binding!.Id);
        await Assert.That(binding.VerificationState).IsEqualTo(WebhookProviderBindingVerificationState.LegacyUnverified);
        await Assert.That(request.Database.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("grant", false)]
    [Arguments("credential", false)]
    [Arguments("consumer", false)]
    [Arguments("binding", false)]
    [Arguments("bootstrap", false)]
    [Arguments("profile", false)]
    [Arguments("support", false)]
    [Arguments("expire_after_reads", false)]
    [Arguments("grant", true)]
    [Arguments("credential", true)]
    [Arguments("consumer", true)]
    [Arguments("profile", true)]
    [Arguments("support", true)]
    [Arguments("expire_after_reads", true)]
    public async Task FinalOwnWriteDriftRollsBackBindingAndEvenRejectedAudit(string drift, bool rejected)
    {
        var interceptor = new AuditBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing: true);
        Guid? support = drift == "support" ? await StartSupportAsync(fixture, request) : null;
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        var client = Substitute.For<ISvixWebhookClient>();
        int remoteCalls = 0;
        client.GetApplicationAsync("repair-application", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            remoteCalls++;
            return Application(seed, rejected);
        });
        interceptor.AfterSave = async (database, token) =>
        {
            await Assert.That(database).IsSameReferenceAs(request.Database);
            await Assert.That(database.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                .IsEqualTo(IsolationLevel.Serializable);
            if (drift == "expire_after_reads")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await database.WebhookConsumers.AsNoTracking().SingleAsync(row => row.Id == seed.Consumer.Id, finalToken);
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                }, _ => { });
            else
                await DriftAsync(database, fixture, options, seed, drift, support, token);
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None));
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await Assert.That(remoteCalls).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, repaired: false, rejected: false);
        await Assert.That(request.Database.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task MissingOriginalGrantDeniesBeforeAnyRemoteOperation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing: false, grant: false);
        int remoteCalls = 0;
        var client = Substitute.For<ISvixWebhookClient>();
        client.GetApplicationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            remoteCalls++;
            return Application(seed, rejected: false);
        });
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None));
        await Assert.That(remoteCalls).IsEqualTo(0);
        await AssertPersistedAsync(fixture, seed, repaired: false, rejected: false);
    }

    [Test]
    public async Task ProviderUnavailableDoesNotBecomeBusinessEvidence()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing: false);
        var client = Substitute.For<ISvixWebhookClient>();
        client.GetApplicationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SvixApplicationBindingResult>(new InvalidOperationException("provider unavailable")));
        var result = await Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await AssertPersistedAsync(fixture, seed, repaired: false, rejected: false);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualOwnerConflictNeverReplaysRemoteProofOrManufacturesRejectedAudit(bool rejected)
    {
        var interceptor = new AuditBoundary();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing: false);
        int remoteCalls = 0;
        var client = Substitute.For<ISvixWebhookClient>();
        client.GetApplicationAsync("repair-application", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            remoteCalls++;
            return Application(seed, rejected);
        });
        interceptor.AfterSave = (_, _) =>
            throw new SqliteException("Conflict after actual repair audit persistence.", 5);
        await Assert.ThrowsAsync<SqliteException>(() =>
            Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None));
        await Assert.That(remoteCalls).IsEqualTo(1);
        await Assert.That(interceptor.SavedAudits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, repaired: false, rejected: false);
        await Assert.That(request.Database.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LostActualCommitAcknowledgementNeverRepeatsProviderOrBusinessEvidence(bool rejected)
    {
        var interceptor = new LostAcknowledgement();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var options = ProviderOptions();
        var seed = await SeedAsync(fixture, options, existing: false);
        int remoteCalls = 0;
        var client = Substitute.For<ISvixWebhookClient>();
        client.GetApplicationAsync("repair-application", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            remoteCalls++;
            return Application(seed, rejected);
        });
        interceptor.Owner = request.Database;
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() =>
            Handler(request, fixture, options, client).ExecuteAsync(Command(seed), CancellationToken.None));
        await Assert.That(remoteCalls).IsEqualTo(1);
        await Assert.That(interceptor.Commits).IsEqualTo(1);
        await AssertPersistedAsync(fixture, seed, repaired: !rejected, rejected: rejected);
        await Assert.That(request.Database.Database.CurrentTransaction).IsNull();
    }

    private static RepairWebhookProviderBindingCommand Command(Seed seed) => new()
    {
        ConsumerId = seed.Consumer.Id,
        ExternalApplicationId = "repair-application",
        ReasonCode = "maintenance"
    };

    private static RepairWebhookProviderBindingCommandHandler Handler(Request request,
        LocalIdentityLifecycleHttpFixture fixture, WebhookOptions options, ISvixWebhookClient client)
    {
        var services = request.Services;
        var cerbosOptions = Options.Create(new CerbosSettings { GrpcEndpoint = "http://cerbos.webhook.test:3593" });
        var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
            services, Substitute.For<ICerbosClient>(), Substitute.For<ICerbosClientFactory>(), cerbosOptions);
        var registration = new ServiceCollection();
        registration.AddHumanWebhookResourceSources(IdentityDatabaseTopology.Colocated);
        var descriptor = registration.Single(row => row.ServiceType == typeof(IHumanWebhookResourceSourceFactory));
        var sources = (IHumanWebhookResourceSourceFactory)descriptor.ImplementationFactory!(services);
        var preparation = new HumanWebhookMutationPreparation(
            services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), fixture.Clock, cerbos,
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "local" }), cerbosOptions,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(), () => sources,
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>());
        var authority = new SvixWebhookProviderBindingAuthorityService(client, new OptionsMonitor(options), fixture.Clock);
        return ActivatorUtilities.CreateInstance<RepairWebhookProviderBindingCommandHandler>(
            services, preparation, authority);
    }

    private static WebhookOptions ProviderOptions() => new()
    {
        Provider = WebhookOptions.ProviderSvix,
        Svix = new()
        {
            BaseUrl = "https://svix.repair.test",
            Environment = SvixConformanceProfileRegistry.SelfHostedEnvironment,
            ProviderVersion = SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            CapabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion
        }
    };

    private sealed record Seed(WebhookConsumer Consumer, InstanceBootstrapState Bootstrap,
        WebhookConsumerProviderBinding? Binding, Guid GrantId);

    private static async Task<Seed> SeedAsync(LocalIdentityLifecycleHttpFixture fixture, WebhookOptions options,
        bool existing, bool grant = true)
    {
        await using var database = fixture.Native.CreateDatabase();
        var bootstrap = await database.InstanceBootstrapStates.AsNoTracking().SingleAsync();
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, fixture.Binding.LocalSubjectId),
            "repair-consumer", WebhookProviderMode.Svix, fixture.Clock.GetUtcNow().UtcDateTime);
        database.Add(consumer);
        Guid grantId = Guid.CreateVersion7();
        if (grant)
        {
            int role = await database.Roles.Where(row => row.MasterCode == "platform.admin"
                && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = grantId,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = role,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        }
        WebhookConsumerProviderBinding? binding = null;
        if (existing)
        {
            var profile = new SvixWebhookProviderBindingAuthorityService(Substitute.For<ISvixWebhookClient>(),
                new OptionsMonitor(options), fixture.Clock).ResolveCurrentProfile().Profile!;
            binding = WebhookConsumerProviderBinding.CreateLegacyUnverified(consumer.TenantId, consumer.Id,
                bootstrap.Id, profile.ProviderEnvironment, "old-application", profile.CapabilityProfile);
            database.Add(binding);
        }
        await database.SaveChangesAsync();
        return new(consumer, bootstrap, binding, grantId);
    }

    private static SvixApplicationBindingResult Application(Seed seed, bool rejected) =>
        new("repair-application", rejected ? "wrong-owner" :
            WebhookConsumerProviderBinding.CreateApplicationUid(seed.Bootstrap.Id, seed.Consumer.Id),
            new Dictionary<string, string>
            {
                ["islamu.consumer_id"] = seed.Consumer.Id.ToString("D"),
                ["islamu.owner_id"] = seed.Consumer.OwnerId.ToString("D"),
                ["islamu.owner_kind_id"] = seed.Consumer.ConsumerKindId.ToString(CultureInfo.InvariantCulture),
                ["islamu.tenant_id"] = seed.Consumer.TenantId!.Value.ToString("D")
            });

    private static async Task DriftAsync(ExploreDbContext database, LocalIdentityLifecycleHttpFixture fixture,
        WebhookOptions options, Seed seed, string drift, Guid? support, CancellationToken token)
    {
        switch (drift)
        {
            case "grant":
                await database.PlatformUserRoles.Where(row => row.Id == seed.GrantId).ExecuteDeleteAsync(token);
                break;
            case "credential":
                await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.NewGuid().ToString()), token);
                break;
            case "consumer":
                await database.WebhookConsumers.Where(row => row.Id == seed.Consumer.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConfigurationVersion, 9), token);
                break;
            case "binding":
                await database.WebhookConsumerProviderBindings.Where(row => row.Id == seed.Binding!.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderVersion, "unproved-version"), token);
                break;
            case "bootstrap":
                await database.InstanceBootstrapStates.Where(row => row.Id == seed.Bootstrap.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, InstanceBootstrapStatus.Pending), token);
                break;
            case "profile":
                options.Svix.ProviderVersion = "unproved-version";
                break;
            case "support":
                await database.SupportAccessSessions.Where(row => row.Id == support)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
                break;
            case "support_extension":
                var expiresAt = await database.SupportAccessSessions.Where(row => row.Id == support)
                    .Select(row => row.ExpiresAtUtc).SingleAsync(token);
                await database.SupportAccessSessions.Where(row => row.Id == support)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiresAt.AddMinutes(10)), token);
                fixture.Clock.Advance(expiresAt - fixture.Clock.GetUtcNow());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift));
        }
    }

    private static async Task<Guid> StartSupportAsync(LocalIdentityLifecycleHttpFixture fixture, Request request)
    {
        await using var database = fixture.Native.CreateDatabase();
        foreach (var key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
        {
            var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) database.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
            else setting.Value = "true";
        }
        var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
            SupportAccessModeEnum.Write, "maintenance", "Provider binding repair", "fixture-ticket",
            fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddMinutes(5));
        database.SupportAccessSessions.Add(session);
        await database.SaveChangesAsync();
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = session.Id.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>()
            .GetCurrentAsync()).IsActive).IsTrue();
        return session.Id;
    }

    private static async Task AssertPersistedAsync(LocalIdentityLifecycleHttpFixture fixture, Seed seed,
        bool repaired, bool rejected)
    {
        await using var observer = fixture.Native.CreateDatabase();
        var bindings = await observer.WebhookConsumerProviderBindings.AsNoTracking()
            .Where(row => row.WebhookConsumerId == seed.Consumer.Id).ToListAsync();
        await Assert.That(bindings.Count).IsEqualTo(repaired || seed.Binding is not null ? 1 : 0);
        if (bindings.Count != 0)
        {
            var binding = bindings.Single();
            await Assert.That(binding.VerificationState).IsEqualTo(repaired
                ? WebhookProviderBindingVerificationState.Verified : WebhookProviderBindingVerificationState.LegacyUnverified);
            await Assert.That(binding.ConcurrencyVersion).IsEqualTo(repaired ? 2L : 1L);
            await Assert.That(binding.VerificationFence).IsEqualTo(repaired ? 2L : 1L);
            await Assert.That(binding.ExternalApplicationId).IsEqualTo(repaired ? "repair-application" : "old-application");
            if (seed.Binding is not null) await Assert.That(binding.Id).IsEqualTo(seed.Binding.Id);
        }
        var audits = await observer.Set<WebhookAuditEvent>().AsNoTracking()
            .Where(row => row.EffectiveScopeId == seed.Consumer.OwnerId).ToListAsync();
        await Assert.That(audits.Count).IsEqualTo(repaired || rejected ? 1 : 0);
        if (audits.Count != 0)
        {
            var audit = audits.Single();
            await Assert.That(audit.Outcome).IsEqualTo(repaired ? WebhookAuditOutcome.Succeeded : WebhookAuditOutcome.Rejected);
            await Assert.That(audit.PrincipalReference).IsEqualTo($"user:{fixture.Binding.LocalSubjectId:D}");
        }
    }

    private sealed class AuditBoundary : SaveChangesInterceptor
    {
        private bool _savingAudit;
        internal Func<ExploreDbContext, CancellationToken, Task>? AfterSave { get; set; }
        internal int SavedAudits { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _savingAudit = AfterSave is not null && eventData.Context!.ChangeTracker
                .Entries<WebhookAuditEvent>().Any(entry => entry.State == EntityState.Added);
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_savingAudit)
            {
                _savingAudit = false;
                SavedAudits++;
                await AfterSave!((ExploreDbContext)eventData.Context!, cancellationToken);
            }
            return result;
        }
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal ExploreDbContext Database => Services.GetRequiredService<ExploreDbContext>();
        internal DefaultHttpContext Context { get; }
        internal Request(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            Context.Request.Path = "/human-webhook-binding-repair";
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            _accessor.HttpContext = Context;
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }
        internal async Task AuthenticateAsync(string token)
        {
            Context.Request.Headers.Authorization = "Bearer " + token;
            var authentication = await Context.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            Context.User = authentication.Principal!;
        }
        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed class LostAcknowledgement : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal int Commits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, Owner)) return Task.CompletedTask;
            Commits++;
            throw new SqliteException("Lost acknowledgement after real repair owner commit.", 5);
        }
    }

    private sealed class OptionsMonitor(WebhookOptions options) : IOptionsMonitor<WebhookOptions>
    {
        public WebhookOptions CurrentValue => options;
        public WebhookOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<WebhookOptions, string?> listener) => null;
    }
}
