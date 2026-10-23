using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Services.Registration.Providers.Formbricks;
using Explore.Infrastructure.Services.Registration.Providers.GoogleForms;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Source-authored integration cases: real Local authentication, real selected physical owners,
/// real S/P/route/A/H/W sources and real secret resolution. Only provider HTTP is simulated.
/// </summary>
[NotInParallel]
public sealed class ProviderTransportDeferredCheckpointTests
{
    [Test]
    public async Task LowerMutationsCannotBeResolvedAsIndependentNativeOperations()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture);
        var assembly = typeof(PublishEventRegistrationProviderBindingCommand).Assembly;
        const string prefix = "Explore.Application.Features.RegistrationProviders.Commands.";
        foreach (string name in new[]
        {
            "ImportRegistrationProviderSchemaRevisionCommand",
            "ImportRegistrationProviderSchemaRevisionCommandHandler",
            "ImportRegistrationProviderSchemaRevisionCommandValidator"
        })
            await Assert.That(assembly.GetType(prefix + name)).IsNull();

        foreach (var type in new[]
        {
            typeof(ReplaceDraftRegistrationProviderMappingsCommand),
            typeof(PublishRegistrationProviderBindingCommand),
            typeof(ReplaceDraftRegistrationProviderMappingsCommandHandler),
            typeof(PublishRegistrationProviderBindingCommandHandler)
        })
        {
            await Assert.That(type.IsVisible).IsFalse();
            await Assert.That(request.Services.GetService(type)).IsNull();
            await Assert.That(type.GetInterfaces().Any(contract => contract.IsGenericType &&
                (contract.GetGenericTypeDefinition() == typeof(ICommand<>)
                    || contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))).IsFalse();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MappingParentPersistsOnlyInsideItsCurrentOriginalHumanOwner(bool expireBeforeMutation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        if (expireBeforeMutation)
            fixture.Clock.Advance(TimeSpan.FromDays(1));
        var handler = ActivatorUtilities.CreateInstance<ReplaceEventDraftRegistrationProviderMappingsCommandHandler>(
            request.Services);
        var command = new ReplaceEventDraftRegistrationProviderMappingsCommand(Tenant, seed.EventId, seed.Binding.Id,
            new()
            {
                FieldMappings = [new() { PlatformFieldKey = "attendee.name", ProviderFieldKey = "name", IsRequired = true }],
                OptionMappings = [new() { PlatformFieldKey = "attendee.name", PlatformOptionKey = "legal", ProviderOptionKey = "full" }]
            });
        if (expireBeforeMutation)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, Token));
        else
            await Assert.That((await handler.ExecuteAsync(command, Token)).IsSuccess).IsTrue();
        await Assert.That(request.Context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var binding = (await new Explore.Persistence.Repositories.RegistrationProviderRepository(observer)
            .GetBindingAsync(Tenant, seed.Binding.Id, Token))!;
        await Assert.That(binding.FieldMappings.Count).IsEqualTo(expireBeforeMutation ? 0 : 1);
        await Assert.That(binding.OptionMappings.Count).IsEqualTo(expireBeforeMutation ? 0 : 1);
        if (!expireBeforeMutation)
        {
            await Assert.That(binding.FieldMappings.Single().ProviderFieldKey).IsEqualTo("name");
            await Assert.That(binding.OptionMappings.Single().ProviderOptionKey).IsEqualTo("full");
        }
    }

    [Test]
    [Arguments("poll", false)]
    [Arguments("poll", true)]
    [Arguments("import", false)]
    [Arguments("import", true)]
    public async Task ActualOwnerSettlesTransportAndProductOnlyForCurrentOriginalHuman(string operation, bool expireDuringTransport)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        ProviderTransport? transport = null;
        await using var request = await Request.OpenAsync(fixture, providerTransport: () => transport!);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        await using (var writer = fixture.Native.CreateDatabase())
        {
            var tuple = GoogleFormsRegistrationProviderDescriptor.SupportedTuple;
            writer.Add(RegistrationProviderCapability.Create(seed.Binding, tuple.ProviderCode,
                tuple.DeploymentKind, tuple.ApiVersion, tuple.AdapterPolicyVersion,
                tuple.ConformanceEvidenceRevision, RegistrationProviderCapabilityCodes.Reconciliation));
            await writer.SaveChangesAsync(Token);
        }
        transport = new ProviderTransport(request.Context, fixture.Clock, google: true)
        {
            ExpireAfterAccess = expireDuringTransport
        };
        Task<BaseCommandResponse<Guid>> Execute() => operation == "poll"
            ? ActivatorUtilities.CreateInstance<PollRegistrationProviderReconciliationCommandHandler>(request.Services)
                .ExecuteAsync(new(Tenant, seed.EventId, seed.Binding.Id, seed.Now), Token)
            : ActivatorUtilities.CreateInstance<ImportExternalRegistrationProviderFormVersionCommandHandler>(request.Services)
                .ExecuteAsync(new(Tenant, seed.EventId, seed.Connection.Id, new()
                {
                    ProviderSurveyId = "checkpoint",
                    Key = "checkpoint-import",
                    Name = "Checkpoint import"
                }), Token);
        if (expireDuringTransport)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Execute);
        else
        {
            var response = await Execute();
            await Assert.That(response.IsSuccess).IsTrue();
        }
        await Assert.That(transport.RequestCount).IsEqualTo(2);
        await Assert.That(request.Context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.RegistrationProviderConnections.AsNoTracking()
            .SingleAsync(row => row.Id == seed.Connection.Id, Token);
        await Assert.That(persisted.LastAccessValidatedAt)
            .IsEqualTo(expireDuringTransport ? (DateTime?)null : transport.AccessAt);
        await Assert.That(persisted.LastCredentialRefreshAt)
            .IsEqualTo(expireDuringTransport ? (DateTime?)null : transport.RefreshAt);
        if (operation == "import")
        {
            var imported = await observer.RegistrationFormVersions.AsNoTracking()
                .Where(row => row.EventId == seed.EventId &&
                    row.SourceKindId == (int)RegistrationFormVersionSourceKindEnum.ExternalImported).ToListAsync(Token);
            await Assert.That(imported.Count).IsEqualTo(expireDuringTransport ? 0 : 1);
            if (!expireDuringTransport)
            {
                await Assert.That(imported[0].StatusId).IsEqualTo((int)RegistrationFormStatusEnum.Published);
                await Assert.That(imported[0].ExternalRegistrationProviderConnectionId).IsEqualTo(seed.Connection.Id);
                await Assert.That(imported[0].ExternalProviderSurveyId).IsEqualTo("checkpoint");
                await Assert.That(imported[0].DataSchemaArtifact).IsNotNull();
            }
        }
    }

    [Test]
    [Arguments("publish", "commit")]
    [Arguments("publish", "final-profile-drift")]
    [Arguments("publish", "final-time-drift")]
    [Arguments("publish", "lost-commit-ack")]
    [Arguments("poll", "commit")]
    [Arguments("poll", "final-profile-drift")]
    [Arguments("poll", "final-time-drift")]
    [Arguments("poll", "lost-commit-ack")]
    [Arguments("import", "commit")]
    [Arguments("import", "final-profile-drift")]
    [Arguments("import", "final-time-drift")]
    [Arguments("import", "lost-commit-ack")]
    public async Task GoogleRefreshAndAccessRemainUnwrittenUntilOriginalOwnerSettlement(string operation, string outcome)
    {
        var failure = new LostCommitAcknowledgment();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        await using var request = await Request.OpenAsync(fixture);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        ICommand<BaseCommandResponse<Guid>> command = operation switch
        {
            "publish" => new PublishEventRegistrationProviderBindingCommand(Tenant, seed.EventId, seed.Binding.Id),
            "poll" => new PollRegistrationProviderReconciliationCommand(Tenant, seed.EventId, seed.Binding.Id, seed.Now),
            "import" => new ImportExternalRegistrationProviderFormVersionCommand(Tenant, seed.EventId,
                seed.Connection.Id, new ImportExternalRegistrationProviderFormVersionRequestDto
                {
                    ProviderSurveyId = "checkpoint",
                    Key = "checkpoint",
                    Name = "Checkpoint import"
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var prepared = await request.Preparation.PrepareAsync(command, Token);
        var transport = new ProviderTransport(request.Context, fixture.Clock, google: true);
        using var http = new HttpClient(transport);
        var adapter = request.Google(http);

        var observation = await request.Journal.ObserveAsync(
            HumanEventProviderSourceTarget.From((ISecureRequest)command, seed.EventId), prepared, async ct =>
            {
                await AssertUnwrittenAsync(fixture, seed.Connection.Id);
                var result = await adapter.ReconcileAsync(seed.Reconciliation, ct);
                await AssertUnwrittenAsync(fixture, seed.Connection.Id);
                await Assert.That(request.Context.Database.CurrentTransaction).IsNull();
                return result;
            }, Token);
        await Assert.That(transport.RefreshAt > DateTime.UnixEpoch).IsTrue();
        await Assert.That(transport.AccessAt > transport.RefreshAt).IsTrue();
        await AssertUnwrittenAsync(fixture, seed.Connection.Id);
        // Distinguish observed timestamps from later application time without a timer or sleep.
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        if (outcome == "lost-commit-ack") failure.Owner = request.Context;
        int mutations = 0;
        async Task<bool> Mutate(RegistrationProviderReconciliationResult result, CancellationToken ct)
        {
            mutations++;
            await Assert.That(request.Context.Database.CurrentTransaction).IsNotNull();
            var applied = await request.Context.RegistrationProviderConnections.AsNoTracking()
                .SingleAsync(row => row.Id == seed.Connection.Id, ct);
            await Assert.That(applied.LastCredentialRefreshAt).IsEqualTo(transport.RefreshAt);
            await Assert.That(applied.LastAccessValidatedAt).IsEqualTo(transport.AccessAt);
            if (operation == "publish")
            {
                var response = await new PublishRegistrationProviderBindingCommandHandler(
                    request.Services.GetRequiredService<IRegistrationProviderRepository>())
                    .ExecuteAsync(new(Tenant, seed.Binding.Id, RegistrationProviderSchemaDriftClass.NoDrift,
                        fixture.Clock.GetUtcNow().UtcDateTime), ct);
                await Assert.That(response.IsSuccess).IsTrue();
            }
            if (outcome == "final-profile-drift")
                await request.Context.RegistrationProviderConnections.Where(row => row.Id == seed.Connection.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConformanceEvidenceRevision, "changed"), ct);
            if (outcome == "final-time-drift")
                request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(
                    _ =>
                    {
                        fixture.Clock.Advance(TimeSpan.FromDays(1));
                        return Task.CompletedTask;
                    }, _ => { });
            return true;
        }
        if (outcome == "lost-commit-ack")
            await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() =>
                request.Journal.ExecuteAsync(command, prepared, observation, Mutate, Token));
        else if (outcome == "final-profile-drift")
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                request.Journal.ExecuteAsync(command, prepared, observation, Mutate, Token));
        else if (outcome == "final-time-drift")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Journal.ExecuteAsync(command, prepared, observation, Mutate, Token));
        else
            await Assert.That(await request.Journal.ExecuteAsync(command, prepared, observation, Mutate, Token)).IsTrue();

        await Assert.That(mutations).IsEqualTo(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            request.Journal.ExecuteAsync(command, prepared, observation, Mutate, Token));
        await Assert.That(transport.RequestCount).IsEqualTo(2);
        await using var verification = fixture.Native.CreateDatabase();
        var persisted = await verification.RegistrationProviderConnections.AsNoTracking()
            .SingleAsync(row => row.Id == seed.Connection.Id, Token);
        await Assert.That(persisted.LastAccessValidatedAt)
            .IsEqualTo(outcome is "final-profile-drift" or "final-time-drift" ? (DateTime?)null : transport.AccessAt);
        await Assert.That(persisted.LastCredentialRefreshAt)
            .IsEqualTo(outcome is "final-profile-drift" or "final-time-drift" ? (DateTime?)null : transport.RefreshAt);
        await Assert.That(persisted.ConformanceEvidenceRevision).IsEqualTo(seed.Connection.ConformanceEvidenceRevision);
        if (operation == "publish")
        {
            var binding = await verification.RegistrationProviderBindings.AsNoTracking()
                .SingleAsync(row => row.Id == seed.Binding.Id, Token);
            bool committed = outcome is "commit" or "lost-commit-ack";
            await Assert.That(binding.StateId).IsEqualTo((int)(committed
                ? RegistrationProviderBindingStateEnum.Published : RegistrationProviderBindingStateEnum.Draft));
            await Assert.That(binding.PublishedMappingRevisionHash is not null).IsEqualTo(committed);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalSourcesRejectDriftBeforeOrAfterRealTransportWithoutCheckpointWrites(bool afterRemote)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        var command = new PollRegistrationProviderReconciliationCommand(Tenant, seed.EventId, seed.Binding.Id, seed.Now);
        var prepared = await request.Preparation.PrepareAsync(command, Token);
        using var transport = new ProviderTransport(request.Context, fixture.Clock, google: true);
        using var http = new HttpClient(transport);
        var adapter = request.Google(http);
        async Task Drift()
        {
            await using var writer = fixture.Native.CreateDatabase();
            await writer.RegistrationProviderConnections.Where(row => row.Id == seed.Connection.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConformanceEvidenceRevision, "drift"), Token);
        }
        if (!afterRemote) await Drift();
        await Assert.ThrowsAsync<InvalidOperationException>(() => request.Journal.ObserveAsync(
            HumanEventProviderSourceTarget.From(command, seed.EventId), prepared, async ct =>
            {
                var result = await adapter.ReconcileAsync(seed.Reconciliation, ct);
                await AssertUnwrittenAsync(fixture, seed.Connection.Id);
                await Drift();
                return result;
            }, Token));
        await Assert.That(transport.RequestCount).IsEqualTo(afterRemote ? 2 : 0);
        await AssertUnwrittenAsync(fixture, seed.Connection.Id);
    }

    [Test]
    public async Task HumanWithoutObservationAndUnknownPhysicalOwnerCannotPersistCheckpoints()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        var command = new PublishEventRegistrationProviderBindingCommand(Tenant, seed.EventId, seed.Binding.Id);
        var prepared = await request.Preparation.PrepareAsync(command, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            request.Checkpoints.RecordAccessValidatedAsync(Tenant, seed.Connection.Id, Token));
        await using var foreign = fixture.Native.CreateDatabase();
        var journal = new RegistrationProviderDeferredCheckpointJournal(() => foreign, () => request.Preparation);
        int remoteCalls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => journal.ObserveAsync(
            HumanEventProviderSourceTarget.From(command, seed.EventId), prepared, _ =>
            {
                remoteCalls++;
                return Task.FromResult(true);
            }, Token));
        await Assert.That(remoteCalls).IsEqualTo(0);
        await AssertUnwrittenAsync(fixture, seed.Connection.Id);
    }

    [Test]
    public async Task FailedRemoteReadDiscardsAlreadyObservedCredentialRefresh()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        var command = new PublishEventRegistrationProviderBindingCommand(Tenant, seed.EventId, seed.Binding.Id);
        var prepared = await request.Preparation.PrepareAsync(command, Token);
        using var transport = new ProviderTransport(request.Context, fixture.Clock, google: true)
        {
            InvalidManagementResponse = true
        };
        using var http = new HttpClient(transport);
        var target = HumanEventProviderSourceTarget.From(command, seed.EventId);
        await Assert.ThrowsAsync<JsonException>(() => request.Journal.ObserveAsync(target, prepared,
            ct => request.Google(http).ReconcileAsync(seed.Reconciliation, ct), Token));
        await Assert.That(transport.RefreshAt > DateTime.UnixEpoch).IsTrue();
        await Assert.That(transport.RequestCount).IsEqualTo(2);
        await AssertUnwrittenAsync(fixture, seed.Connection.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => request.Journal.ObserveAsync(target, prepared,
            ct => request.Google(http).ReconcileAsync(seed.Reconciliation, ct), Token));
        await Assert.That(transport.RequestCount).IsEqualTo(2);
    }

    [Test]
    [Arguments("command")]
    [Arguments("issuer")]
    [Arguments("capability")]
    [Arguments("connection")]
    [Arguments("tenant")]
    public async Task RetainedObservationCannotMoveToAnotherCustody(string substitution)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture);
        var seed = await SeedAsync(fixture, request.Variable, google: true);
        var command = new PublishEventRegistrationProviderBindingCommand(Tenant, seed.EventId, seed.Binding.Id);
        var prepared = await request.Preparation.PrepareAsync(command, Token);
        using var transport = new ProviderTransport(request.Context, fixture.Clock, google: true);
        using var http = new HttpClient(transport);
        var target = HumanEventProviderSourceTarget.From(command, seed.EventId);
        if (substitution is "tenant" or "connection")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => request.Journal.ObserveAsync(target, prepared,
                async ct =>
                {
                    await request.Checkpoints.RecordAccessValidatedAsync(
                        substitution == "tenant" ? Guid.CreateVersion7() : Tenant,
                        substitution == "connection" ? Guid.CreateVersion7() : seed.Connection.Id, ct);
                    return true;
                }, Token));
        }
        else
        {
            var observed = await request.Journal.ObserveAsync(target, prepared,
                ct => request.Google(http).ReconcileAsync(seed.Reconciliation, ct), Token);
            var journal = substitution == "issuer"
                ? new RegistrationProviderDeferredCheckpointJournal(() => request.Context, () => request.Preparation)
                : request.Journal;
            var supplied = substitution == "command" ? command with { } : command;
            var capability = substitution == "capability"
                ? await request.Preparation.PrepareAsync(command, Token) : prepared;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                journal.ExecuteAsync<RegistrationProviderReconciliationResult, bool>(supplied, capability, observed,
                    (_, _) => throw new InvalidOperationException("A foreign observation reached business work."), Token));
        }
        await AssertUnwrittenAsync(fixture, seed.Connection.Id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WorkerTransportKeepsItsExistingImmediateCheckpointSemantics(bool google)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.OpenAsync(fixture, human: false);
        var seed = await SeedAsync(fixture, request.Variable, google);
        using var transport = new ProviderTransport(request.Context, fixture.Clock, google);
        using var http = new HttpClient(transport);
        if (google)
            await request.Google(http).ReconcileAsync(seed.Reconciliation, Token);
        else
            await new FormbricksRegistrationProviderAdapter(http,
                request.Services.GetRequiredService<ISecretResolver>(), fixture.Clock)
                .ReconcileAsync(seed.Reconciliation, Token);
        await using var verification = fixture.Native.CreateDatabase();
        var connection = await verification.RegistrationProviderConnections.AsNoTracking()
            .SingleAsync(row => row.Id == seed.Connection.Id, Token);
        await Assert.That(connection.LastAccessValidatedAt).IsEqualTo(google ? transport.AccessAt : (DateTime?)null);
        await Assert.That(connection.LastCredentialRefreshAt).IsEqualTo(google ? transport.RefreshAt : (DateTime?)null);
        await Assert.That(transport.RequestCount).IsEqualTo(google ? 2 : 1);
    }

    private static Guid Tenant => PlatformDefaults.DefaultTenantId;
    private static CancellationToken Token => LocalIdentityLifecycleHttpFixture.CancellationToken;

    private static async Task AssertUnwrittenAsync(LocalIdentityLifecycleHttpFixture fixture, Guid connectionId)
    {
        await using var verification = fixture.Native.CreateDatabase();
        var row = await verification.RegistrationProviderConnections.AsNoTracking()
            .SingleAsync(row => row.Id == connectionId, Token);
        await Assert.That(row.LastCredentialRefreshAt).IsNull();
        await Assert.That(row.LastAccessValidatedAt).IsNull();
    }

    private sealed record Seed(Guid EventId, RegistrationProviderConnection Connection,
        RegistrationProviderBinding Binding, DateTime Now)
    {
        internal RegistrationProviderReconciliationRequest Reconciliation => new(Tenant, Binding, Connection,
            new(Connection.ProviderCode, Connection.ProviderDeploymentCode, Connection.ApiVersion,
                Connection.AdapterPolicyVersion, Connection.ConformanceEvidenceRevision), Now);
    }

    private static async Task<Seed> SeedAsync(LocalIdentityLifecycleHttpFixture fixture, string variable, bool google)
    {
        await using var context = fixture.Native.CreateDatabase();
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        Guid actor = await context.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.Id).SingleAsync(Token);
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = Tenant,
            Tenant = null!,
            Title = "Deferred provider checkpoint",
            ActorId = actor,
            Actor = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            SessionCount = 1,
            FirstSessionStartUtc = fixture.Clock.GetUtcNow().AddDays(1),
            LastSessionEndUtc = fixture.Clock.GetUtcNow().AddDays(1).AddHours(1),
            CreatedAt = now
        };
        var form = RegistrationForm.Create(Tenant, parent.Id, "checkpoint", "form", "Checkpoint form", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        form.AddVersion(version);
        var secret = SecretBinding.CreateEnvironmentVariable(SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant, Tenant, variable, qualifier: Guid.CreateVersion7().ToString("N"));
        secret.Id = Guid.CreateVersion7();
        string Generated() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable(variable, google
            ? JsonSerializer.Serialize(new { refresh_token = Generated(), client_id = Generated(), client_secret = Generated() })
            : Generated());
        var tuple = google ? GoogleFormsRegistrationProviderDescriptor.SupportedTuple
            : new RegistrationProviderTuple("FORMBRICKS", "CLOUD", "v1", "checkpoint", "checkpoint");
        var connection = RegistrationProviderConnection.Create(Tenant, "Checkpoint connection",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            tuple.ProviderCode, tuple.DeploymentKind, tuple.ApiVersion, tuple.AdapterPolicyVersion,
            tuple.ConformanceEvidenceRevision, google ? "https://forms.googleapis.com/v1" : "https://provider.example/api/v1",
            google ? "https://docs.google.com" : "https://provider.example", "checkpoint", secret.Id, null, now);
        var binding = RegistrationProviderBinding.Create(Tenant, connection.Id, form.Id, version.Id,
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Manual, RegistrationProviderTrustLevelEnum.FullSync, null, now);
        binding.SetDraftProvisionedSurvey("checkpoint", "1");
        context.AddRange(parent, form, secret, connection, binding);
        await context.SaveChangesAsync(Token);
        return new(parent.Id, connection, binding, now);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _host;
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        private Request(WebApplicationFactory<Program> host)
        {
            _host = host;
            _scope = host.Services.CreateAsyncScope();
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(Tenant);
        }
        internal string Variable { get; } = "EVENT_CHECKPOINT_TEST_" + Guid.CreateVersion7().ToString("N");
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();
        internal IHumanEventProviderMutationPreparation Preparation =>
            Services.GetRequiredService<IHumanEventProviderMutationPreparation>();
        internal IRegistrationProviderDeferredCheckpointJournal Journal =>
            Services.GetRequiredService<IRegistrationProviderDeferredCheckpointJournal>();
        internal IRegistrationProviderConnectionCheckpoint Checkpoints =>
            Services.GetRequiredService<IRegistrationProviderConnectionCheckpoint>();
        internal GoogleFormsRegistrationProviderAdapter Google(HttpClient http) => new(http,
            Services.GetRequiredService<ISecretResolver>(), Checkpoints,
            Services.GetRequiredService<IGooglePubSubOidcTokenValidator>());

        internal static async Task<Request> OpenAsync(LocalIdentityLifecycleHttpFixture fixture, bool human = true,
            Func<HttpMessageHandler>? providerTransport = null)
        {
            var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "local");
                if (providerTransport is not null)
                    services.AddHttpClient(GoogleFormsRegistrationProviderAdapter.HttpClientName)
                        .ConfigurePrimaryHttpMessageHandler(providerTransport);
            }));
            var request = new Request(host);
            if (human)
            {
                var http = new DefaultHttpContext { RequestServices = request.Services };
                http.Request.Headers.Authorization = "Bearer " + await fixture.SignInAsync();
                request._accessor.HttpContext = http;
                var authenticated = await http.AuthenticateAsync();
                await Assert.That(authenticated.Succeeded).IsTrue();
                http.User = authenticated.Principal!;
                await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(Token);
            }
            return request;
        }
        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(Variable, null);
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
            await _host.DisposeAsync();
        }
    }

    private sealed class ProviderTransport(ExploreDbContext context,
        LocalIdentityLifecycleHttpFixture.LifecycleClock clock, bool google) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal DateTime RefreshAt { get; private set; }
        internal DateTime AccessAt { get; private set; }
        internal bool InvalidManagementResponse { get; init; }
        internal bool ExpireAfterAccess { get; init; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Provider transport ran inside the local owner.");
            RequestCount++;
            clock.Advance(TimeSpan.FromSeconds(1));
            bool refresh = request.RequestUri!.Host == "oauth2.googleapis.com";
            if (refresh) RefreshAt = clock.GetUtcNow().UtcDateTime;
            else AccessAt = clock.GetUtcNow().UtcDateTime;
            if (!refresh && ExpireAfterAccess) clock.Advance(TimeSpan.FromDays(1));
            string json = refresh
                ? JsonSerializer.Serialize(new { access_token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) })
                : InvalidManagementResponse ? "{" : google
                    ? request.RequestUri!.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal)
                        ? """{"responses":[]}"""
                        : """{"settings":{"publishSettings":{"publishState":{"isPublished":true,"isAcceptingResponses":true}}},"items":[{"itemId":"name","title":"Name","questionItem":{"question":{"required":false,"textQuestion":{"paragraph":false}}}}]}"""
                    : """{"data":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class LostCommitAcknowledgment : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Owner is null || !ReferenceEquals(eventData.Context, Owner)) return Task.CompletedTask;
            Owner = null;
            throw new SqliteException("Checkpoint commit acknowledgment was lost.", 5);
        }
    }
}
