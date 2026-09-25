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
using Explore.Secrets.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

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
        var result = instanceOnly
            ? await package.Service.PublishInstanceAsync(oneTimeCredentials: package.Credentials)
            : await package.Service.PublishAsync(oneTimeCredentials: package.Credentials);
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
        await Assert.ThrowsAsync<OperationCanceledException>(() => package.Service.PublishInstanceAsync(cancellation.Token, package.Credentials));
        await Assert.That((await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!.IsUsable).IsFalse();
    }

    [Test]
    public async Task SuccessfulUploadIsNotConvergenceEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.BindAndActivateAsync();
        await using var package = new Package(fixture, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await package.Service.PublishInstanceAsync(oneTimeCredentials: package.Credentials);
        await Assert.That(result.Succeeded).IsTrue();
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
        await fixture.Database.SetInstanceAsync(GovernanceSettingKeys.Cerbos.GrpcEndpoint, "HTTPS://ALIAS.EXAMPLE.TEST:443/");
        var second = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(second.GrpcEndpoint).IsEqualTo(Alias);
        await Assert.That(second.DeploymentId).IsEqualTo(first.DeploymentId);
        await Assert.That(second.ActivationEpoch).IsEqualTo(first.ActivationEpoch);
        await fixture.Control.BeginPublicationAsync(Endpoint, default);
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
        await fixture.Database.SetInstanceAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, true);
        await fixture.Database.SetTenantAsync(GovernanceSettingKeys.Cerbos.Mode, "custom_endpoint");
        await fixture.Database.SetTenantAsync(GovernanceSettingKeys.Cerbos.CustomEndpoint, "https://unbound.example.test");
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
        await fixture.Database.SetInstanceAsync(GovernanceSettingKeys.Security.AuthorizationProvider, "local");
        var local = (await fixture.Reader.ReadAsync(fixture.Database.TenantId, default))!;
        await Assert.That(local.Mode).IsEqualTo(Explore.Application.Contracts.Services.EventResourceProviderMode.Local);
        await Assert.That(local.IsUsable).IsTrue();
        await fixture.BindAndActivateAsync();
        await fixture.Database.SetInstanceAsync(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos");
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
        fixture.Admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        await Assert.ThrowsAsync<AuthorizationException>(() => fixture.Control.BeginAsync(fixture.DeploymentId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Settings.UpsertAsync(new SystemSetting
        {
            SettingKey = EventResourceProviderBindingDocument.SettingKey, Value = "{}"
        }));
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
        await fixture.Database.SetInstanceAsync(GovernanceSettingKeys.Security.AuthorizationProvider, "local");
        await fixture.Database.SetInstanceAsync(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "invalid");
        await Assert.That(await fixture.Reader.ReadAsync(fixture.Database.TenantId, default)).IsNull();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(SmtpSettingsDatabase database)
        {
            Database = database;
            Settings = new(database.Context, database.MutationLock);
            var activations = new EventResourceProviderActivationRepository(database.Context);
            Admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
            Control = new(Settings, activations, database.MutationLock, new EfCoreUnitOfWork(database.Context), Admin, new FixedTimeProvider());
            Reader = new(Settings, new TenantSettingRepository(database.Context, database.MutationLock), activations,
                Options.Create(new AuthorizationProviderDeploymentOptions()), Options.Create(new CerbosSettings { GrpcEndpoint = Endpoint }),
                new ConfigurationBuilder().Build(), NullLogger<EventResourceProviderSnapshotReader>.Instance);
        }

        public SmtpSettingsDatabase Database { get; }
        public IAdminContext Admin { get; } = Substitute.For<IAdminContext>();
        public SystemSettingRepository Settings { get; }
        public EventResourceProviderControlPlane Control { get; }
        public EventResourceProviderSnapshotReader Reader { get; }
        public Guid DeploymentId { get; } = Guid.CreateVersion7();

        public static async Task<Fixture> CreateAsync()
        {
            var database = await SmtpSettingsDatabase.CreateAsync();
            database.Context.Add(new SettingValueTypeLookup { Id = (int)SettingValueType.Json, MasterCode = "Json", FullName = "Json" });
            await database.Context.SaveChangesAsync();
            await database.SetInstanceAsync(GovernanceSettingKeys.Security.AuthorizationProvider, "cerbos");
            await database.SetInstanceAsync(GovernanceSettingKeys.Cerbos.GrpcEndpoint, Endpoint);
            return new(database);
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
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private sealed class Package : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"resource-policy-{Guid.CreateVersion7():N}");
        private readonly HttpClient _client;
        private readonly Handler _handler;
        public Package(Fixture fixture, Func<CancellationToken, Task<HttpResponseMessage>> response)
        {
            Directory.CreateDirectory(Path.Combine(_path, "_schemas"));
            File.WriteAllText(Path.Combine(_path, "islamuevent_event_resource.yaml"),
                "apiVersion: api.cerbos.dev/v1\nresourcePolicy:\n  resource: islamuevent_event_resource\n  version: default\n  rules: []\n");
            File.WriteAllText(Path.Combine(_path, "_schemas", "islamuevent_event_resource.json"), "{\"type\":\"object\"}");
            var options = Options.Create(new CerbosPolicyPackageOptions { PoliciesPath = _path });
            var resolver = Substitute.For<ICerbosConfigResolver>();
            resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
            {
                Endpoint = Alias, Mode = CerbosMode.CustomEndpoint, IsInstanceDefault = false,
                AdminEndpoint = "https://admin.example.test"
            });
            _handler = new Handler(response);
            _client = new(_handler, disposeHandler: false);
            var factory = Substitute.For<IHttpClientFactory>();
            factory.CreateClient(Arg.Any<string>()).Returns(_client);
            Service = new(options, Options.Create(new CerbosAdminApiSettings { Endpoints = ["https://admin.example.test"] }),
                resolver, Substitute.For<ISecretResolver>(), new CerbosAdminEndpointValidator(options), factory,
                NullLogger<CerbosPolicyPackageService>.Instance, fixture.Control, Options.Create(new CerbosSettings { GrpcEndpoint = Endpoint }));
        }
        public PolicyPackageAdminCredentials Credentials { get; } = new(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        public CerbosPolicyPackageService Service { get; }
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
}
