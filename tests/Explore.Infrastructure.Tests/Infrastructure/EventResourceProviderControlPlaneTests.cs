using System.Net;
using System.Security.Cryptography;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Exceptions;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Explore.Persistence.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel]
public sealed class EventResourceProviderControlPlaneTests
{
    private const string Endpoint = "https://pdp.example.test";
    private const string Alias = "https://alias.example.test";
    private const string Scope = "resource-policy";
    private const string Version = "default";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedPublicationCommitsClosureBeforeNetworkAndNeverReactivates(bool instanceOnly)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        var old = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        var networkObserved = new TaskCompletionSource<(EventResourceProviderActivationStateEnum State, long Epoch)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var package = new Package(fixture, async token =>
        {
            await using var observer = fixture.CreateContext();
            var state = (await new EventResourceProviderActivationRepository(observer).GetAsync(fixture.DeploymentId, token))!;
            networkObserved.TrySetResult((state.State, state.Epoch));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var result = await package.PublishAsync(instanceOnly);
        await Assert.That(result.Succeeded).IsFalse();
        var observed = await networkObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(observed.State).IsEqualTo(EventResourceProviderActivationStateEnum.Transitioning);
        await Assert.That(observed.Epoch).IsGreaterThan(old.ActivationEpoch);
        var current = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(current.IsUsable).IsFalse();
        await Assert.That(current.ActivationEpoch).IsGreaterThan(old.ActivationEpoch);
    }

    [Test]
    public async Task CancelledPublicationLeavesCommittedFenceClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        using var cancellation = new CancellationTokenSource();
        await using var package = new Package(fixture, token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await Assert.ThrowsAsync<OperationCanceledException>(() => package.PublishAsync(true, cancellation.Token));
        await Assert.That((await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!.IsUsable).IsFalse();
    }

    [Test]
    public async Task SuccessfulUploadIsNotConvergenceEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        await using var package = new Package(fixture, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await package.PublishAsync(true);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That((await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!.IsUsable).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedOrUnknownClosureNeverGrantsRemotePublication(bool unknownCommit)
    {
        var settlement = new LostClosureAcknowledgement();
        await using var fixture = await Fixture.CreateAsync(settlement);
        await fixture.BindAndActivateAsync();
        int remoteWrites = 0;
        await using var package = new Package(fixture, _ =>
        {
            Interlocked.Increment(ref remoteWrites);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        if (unknownCommit) settlement.Armed = true;
        else await fixture.RevokeAdministratorAsync();
        var result = await package.PublishAsync(true);
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(remoteWrites).IsEqualTo(0);
        if (unknownCommit)
            await Assert.That((await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!.IsUsable).IsFalse();
    }

    [Test]
    public async Task StaleCompletionAndPartialEvidenceCannotReopenDeployment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.BindAndActivateAsync();
        var newer = await fixture.Control.BeginAsync(fixture.DeploymentId, default);
        await Assert.That(await fixture.ActivateAsync(original)).IsFalse();
        await Assert.That((await fixture.Control.ReadBindingsAsync(default)).Revision).IsEqualTo(newer.BindingRevision);
        await Assert.That(await fixture.Control.ActivateAsync(fixture.DeploymentId, newer.OperationId, newer.Epoch,
            Scope, Version, true, 2, 1, true, default)).IsFalse();
        await Assert.That(await fixture.ActivateAsync(newer)).IsFalse();
        var recovery = await fixture.Control.BeginAsync(fixture.DeploymentId, default);
        await Assert.That(await fixture.ActivateAsync(recovery)).IsTrue();
    }

    [Test]
    public async Task TransitionAndReactivationChangesSnapshotEvenWhenRouteAndPolicyReturnToSameValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        var before = await fixture.Reader.ReadAsync(fixture.Database.TenantId, default);
        var operation = await fixture.Control.BeginAsync(fixture.DeploymentId, default);
        await Assert.That(await fixture.ActivateAsync(operation)).IsTrue();
        var after = await fixture.Reader.ReadAsync(fixture.Database.TenantId, default);
        await Assert.That(after!.IsUsable).IsTrue();
        await Assert.That(after.GrpcEndpoint).IsEqualTo(before!.GrpcEndpoint);
        await Assert.That(after.ActivationEpoch).IsGreaterThan(before.ActivationEpoch);
        await Assert.That(after == before).IsFalse();
    }

    [Test]
    public async Task EndpointAliasesShareOneFenceAndCannotBeReassigned()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        var first = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        fixture.Cerbos.GrpcEndpoint = "HTTPS://ALIAS.EXAMPLE.TEST:443/";
        var second = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(second.GrpcEndpoint).IsEqualTo(Alias);
        await Assert.That(second.DeploymentId).IsEqualTo(first.DeploymentId);
        await Assert.That(second.ActivationEpoch).IsEqualTo(first.ActivationEpoch);
        await using var package = new Package(fixture, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        await Assert.That((await package.PublishAsync(true)).Succeeded).IsTrue();
        await Assert.That((await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!.IsUsable).IsFalse();
        var document = await fixture.Control.ReadBindingsAsync(default);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Control.BindAsync(
            new(Guid.CreateVersion7(), [Alias], Scope, Version), document.Revision, default));
    }

    [Test]
    public async Task FreshRouteReadsIgnoreTrackedSettingsAndObserveTenantLockPrecedence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, true);
        await fixture.SetTenantAsync(GovernanceSettingKeys.Cerbos.Mode, "custom_endpoint");
        await fixture.SetTenantAsync(GovernanceSettingKeys.Cerbos.CustomEndpoint, "https://unbound.example.test");
        await Assert.That(await fixture.Reader.ReadAsync(fixture.Database.TenantId, default)).IsNull();
        var tracked = await fixture.Database.Context.SystemSettings.SingleAsync(
            value => value.SettingKey == GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled);
        await using (var writer = fixture.CreateContext())
        {
            var current = await writer.SystemSettings.SingleAsync(value => value.Id == tracked.Id);
            current.Value = "false";
            current.IsLocked = true;
            await writer.SaveChangesAsync();
        }
        await Assert.That(tracked.Value).IsEqualTo("true");
        var effective = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(effective.IsUsable).IsTrue();
        await Assert.That(effective.GrpcEndpoint).IsEqualTo(Endpoint);
    }

    [Test]
    public async Task LocalRouteNeedsNoBindingButRemoteAndCorruptBindingsFailClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.That(await fixture.Reader.ReadAsync(fixture.Database.TenantId, default)).IsNull();
        fixture.Deployment.Provider = "local";
        var local = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(local.Mode).IsEqualTo(Explore.Application.Contracts.Services.EventResourceProviderMode.Local);
        await Assert.That(local.IsUsable).IsTrue();
        await fixture.BindAndActivateAsync();
        fixture.Deployment.Provider = "cerbos";
        await using (var writer = fixture.CreateContext())
        {
            var document = await writer.SystemSettings.SingleAsync(setting => setting.SettingKey == EventResourceProviderBindingDocument.SettingKey);
            document.Value = "{}";
            await writer.SaveChangesAsync();
        }
        await Assert.That(await fixture.Reader.ReadAsync(fixture.Database.TenantId, default)).IsNull();
    }

    [Test]
    public async Task RevokedAdministratorAndGenericSettingMutationCannotChangeBindings()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        await fixture.RevokeAdministratorAsync();
        await Assert.ThrowsAsync<AuthorizationException>(() => fixture.Control.BeginAsync(fixture.DeploymentId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Authenticated.RunAsync(null, session =>
            session.Services.GetRequiredService<ISystemSettingRepository>().UpsertInCurrentTransactionAsync(new SystemSetting
            {
                SettingKey = EventResourceProviderBindingDocument.SettingKey,
                Value = "{}"
            })));
        await Assert.That((await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!.IsUsable).IsTrue();
    }

    [Test]
    public async Task BindingDeclarationChangeClosesAuthorityAndRequiresMatchingPolicyEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        var before = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        var document = await fixture.Control.ReadBindingsAsync(default);
        var changed = await fixture.Control.BindAsync(new(fixture.DeploymentId, [Endpoint, Alias], "", "next"), document.Revision, default);
        var after = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(after.Scope).IsEqualTo("");
        await Assert.That(after.PolicyVersion).IsEqualTo("next");
        await Assert.That(after.ActivationEpoch).IsGreaterThan(before.ActivationEpoch);
        await Assert.That(after.ActivationState).IsEqualTo(EventResourceProviderActivationStateEnum.Transitioning);
        await Assert.That(await fixture.ActivateAsync(changed)).IsFalse();
        var recovery = await fixture.Control.BeginAsync(fixture.DeploymentId, default);
        await Assert.That(await fixture.Control.ActivateAsync(fixture.DeploymentId, recovery.OperationId, recovery.Epoch,
            "", "next", true, 2, 2, true, default)).IsTrue();
    }

    [Test]
    public async Task MalformedCustomizationCannotDowngradeAnExplicitLocalRoute()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Deployment.Provider = "local";
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, false);
        var corrupt = await fixture.Database.Context.SystemSettings.SingleAsync(
            row => row.SettingKey == GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled);
        corrupt.Value = "\"invalid\"";
        await fixture.Database.Context.SaveChangesAsync();
        await Assert.That(await fixture.Reader.ReadAsync(fixture.Database.TenantId, default)).IsNull();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(SmtpSettingsDatabase database, AuthenticatedSettingsFixture authenticated)
        {
            Database = database;
            Authenticated = authenticated;
            var settings = SystemSettingRepository.CreateReadOnly(database.Context, database.MutationLock);
            var activations = new EventResourceProviderActivationRepository(database.Context);
            Control = new(this);
            Reader = new(settings, TenantSettingRepository.CreateReadOnly(database.Context, database.MutationLock), activations,
                Options.Create(Deployment), Options.Create(Cerbos),
                new ConfigurationBuilder().Build(), NullLogger<EventResourceProviderSnapshotReader>.Instance);
        }

        public SmtpSettingsDatabase Database { get; }
        public AuthenticatedSettingsFixture Authenticated { get; }
        public SelectedControl Control { get; }
        public EventResourceProviderSnapshotReader Reader { get; }
        public AuthorizationProviderDeploymentOptions Deployment { get; } = new() { Provider = "cerbos" };
        public CerbosSettings Cerbos { get; } = new() { GrpcEndpoint = Endpoint };
        public Guid DeploymentId { get; } = Guid.CreateVersion7();

        public static async Task<Fixture> CreateAsync(IInterceptor? interceptor = null)
        {
            var database = await SmtpSettingsDatabase.CreateAsync();
            if (!await database.Context.Set<SettingValueTypeLookup>().AnyAsync(row => row.Id == (int)SettingValueType.Json))
            {
                database.Context.Add(new SettingValueTypeLookup { Id = (int)SettingValueType.Json, MasterCode = "Json", FullName = "Json" });
                await database.Context.SaveChangesAsync();
            }
            var authenticated = await AuthenticatedSettingsFixture.CreateAsync(
                () => AuthenticatedPolicyPackageTestService.CreateContextFor(database), interceptor);
            var fixture = new Fixture(database, authenticated);
            return fixture;
        }

        public ExploreDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = Database.Context.Database.GetDbConnection().DataSource
            });
            return new(options.UseSnakeCaseNamingConvention().Options);
        }

        public async Task<EventResourceProviderOperation> BindAndActivateAsync()
        {
            var operation = await Control.BindAsync(new(DeploymentId, [Endpoint, Alias], Scope, Version), Guid.Empty, default);
            await Assert.That(await ActivateAsync(operation)).IsTrue();
            return operation;
        }

        public Task<bool> ActivateAsync(EventResourceProviderOperation operation) => Control.ActivateAsync(
            operation.DeploymentId, operation.OperationId, operation.Epoch, Scope, Version, true, 2, 2, true, default);
        public Task SetInstanceAsync<T>(string key, T value) =>
            AuthenticatedPolicyPackageTestService.SetValueAsync(Authenticated, null, key, value);
        public Task SetTenantAsync<T>(string key, T value) =>
            AuthenticatedPolicyPackageTestService.SetValueAsync(Authenticated, Database.TenantId, key, value);
        public async Task RevokeAdministratorAsync()
        {
            await using var writer = CreateContext();
            await writer.PlatformUserRoles.Where(row => row.UserId == Authenticated.ActorId).ExecuteDeleteAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Authenticated.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class SelectedControl(Fixture fixture)
    {
        public Task<EventResourceProviderBindingDocument> ReadBindingsAsync(CancellationToken token) =>
            fixture.Authenticated.RunAsync(null, session =>
                session.Services.GetRequiredService<EventResourceProviderControlPlane>().ReadBindingsAsync(token), cancellationToken: token);
        public Task<EventResourceProviderOperation> BindAsync(EventResourceDeploymentBinding binding, Guid revision, CancellationToken token) =>
            fixture.Authenticated.RunAsync(null, session =>
                session.Services.GetRequiredService<EventResourceProviderControlPlane>().BindAsync(new(binding, revision), token), cancellationToken: token);
        public Task<EventResourceProviderOperation> BeginAsync(Guid deployment, CancellationToken token) =>
            fixture.Authenticated.RunAsync(null, session =>
                session.Services.GetRequiredService<EventResourceProviderControlPlane>().BeginAsync(new(deployment), token), cancellationToken: token);
        public Task<bool> ActivateAsync(Guid deployment, Guid operation, long epoch, string scope, string version,
            bool stopped, int reachable, int declared, bool confirmed, CancellationToken token) =>
            fixture.Authenticated.RunAsync(null, session =>
                session.Services.GetRequiredService<EventResourceProviderControlPlane>().ActivateAsync(
                    new(deployment, operation, epoch, scope, version, stopped, reachable, declared, confirmed), token), cancellationToken: token);
    }

    private sealed class Package : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"resource-policy-{Guid.CreateVersion7():N}");
        private readonly HttpClient _client;
        private readonly Handler _handler;
        private readonly Fixture _fixture;
        private readonly IOptions<CerbosPolicyPackageOptions> _options;
        private readonly ICerbosConfigResolver _resolver;
        private readonly IHttpClientFactory _factory;
        public Package(Fixture fixture, Func<CancellationToken, Task<HttpResponseMessage>> response)
        {
            _fixture = fixture;
            Directory.CreateDirectory(Path.Combine(_path, "_schemas"));
            File.WriteAllText(Path.Combine(_path, "islamuevent_event_resource.yaml"),
                "apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: islamuevent_event_resource\n  version: default\n  rules: []\n");
            File.WriteAllText(Path.Combine(_path, "_schemas", "islamuevent_event_resource.json"), "{\"type\":\"object\"}");
            _options = Options.Create(new CerbosPolicyPackageOptions { PoliciesPath = _path });
            var resolver = Substitute.For<ICerbosConfigResolver>();
            resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
            {
                Endpoint = Alias,
                Mode = CerbosMode.CustomEndpoint,
                IsInstanceDefault = false,
                AdminEndpoint = "https://admin.example.test"
            });
            _handler = new Handler(response);
            _client = new(_handler, disposeHandler: false);
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(Arg.Any<string>()).Returns(_client);
            _resolver = resolver;
            _factory = factory;
        }
        public PolicyPackageAdminCredentials Credentials { get; } = new(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        public async Task<Explore.Application.Authorization.PolicyPackagePublishResult> PublishAsync(
            bool managed, CancellationToken token = default)
        {
            if (!managed)
            {
                await _fixture.SetInstanceAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, true);
                await _fixture.SetInstanceAsync(GovernanceSettingKeys.Cerbos.Mode, "custom_endpoint");
                await _fixture.SetInstanceAsync(GovernanceSettingKeys.Cerbos.CustomEndpoint, Alias);
                await _fixture.SetInstanceAsync(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint, "https://admin.example.test");
            }
            return await _fixture.Authenticated.RunAsync(null, session =>
            {
                var selected = new ResourceProviderTestServices(session.Services);
                var publisher = new CerbosPolicyPackageService(_options,
                    Options.Create(new CerbosAdminApiSettings { Endpoints = ["https://admin.example.test"] }),
                    _resolver, new CerbosAdminEndpointValidator(), _factory, NullLogger<CerbosPolicyPackageService>.Instance,
                    selected.Control, Options.Create(_fixture.Cerbos), selected,
                    Options.Create(new AuthorizationProviderDeploymentOptions { Provider = managed ? "cerbos" : null }));
                selected.Publisher = publisher;
                return publisher.PublishAsync(AuthenticatedPolicyPackageTestService.Command(Credentials), token);
            }, cancellationToken: token);
        }
        public ValueTask DisposeAsync()
        {
            _client.Dispose();
            _handler.Dispose();
            Directory.Delete(_path, true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(cancellationToken);
    }

    private sealed class LostClosureAcknowledgement : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return Task.CompletedTask;
            Armed = false;
            throw new Microsoft.Data.Sqlite.SqliteException("Lost resource closure commit acknowledgement.", 5);
        }
    }
}
