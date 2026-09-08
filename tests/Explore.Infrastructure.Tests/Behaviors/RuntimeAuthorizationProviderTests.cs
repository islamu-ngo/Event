using Explore.Infrastructure.Tests.Authorization;
using Explore.Infrastructure.Tests.Infrastructure;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Application.SupportAccess;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Behaviors;

public class RuntimeAuthorizationProviderTests
{
    private static readonly Guid TestTenantId = Guid.Parse("d1b8e7d4-5c1f-4d1d-9f1b-6f5a6f6e9c21");

    [Test]
    public async Task CheckSettingAccessAsync_WithJsonSerializedCerbosMode_RoutesToCerbosProviderAfterInvalidation()
    {
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.UserId.Returns(Guid.NewGuid());
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var machinePrincipalAccessor = Substitute.For<IMachinePrincipalAccessor>();
        machinePrincipalAccessor.IsMachineCaller.Returns(false);
        machinePrincipalAccessor.Current.Returns((Explore.Application.Authentication.ApiKeyPrincipalContext?)null);

        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();
        var cerbosClient = Substitute.For<ICerbosClient>();
        var cerbosClientFactory = Substitute.For<ICerbosClientFactory>();
        var cerbosResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        cerbosResponse.Results.Add(CreateResultEntry(
            GovernanceSettingKeys.Security.AuthorizationProvider,
            "islamuevent_instance_setting",
            "update",
            Effect.Allow));
        cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(cerbosResponse));

        var repository = Substitute.For<ISystemSettingRepository>();
        repository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(new SystemSetting
            {
                Id = Guid.NewGuid(),
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = JsonSerializer.Serialize("cerbos"),
                ValueType = SettingValueType.String,
                IsLocked = true,
                CreatedAt = DateTime.UtcNow
            });

        var runtimeProvider = new RuntimeAuthorizationProvider(
            new CerbosAuthorizationService(
                cerbosClient,
                new CerbosPrincipalBuilder(
                    adminContext,
                    machinePrincipalAccessor,
                    Substitute.For<IEventAuthoritySnapshotService>(),
                    Substitute.For<IOrganizationMemberRepository>(),
                    Substitute.For<IGroupMemberRepository>()),
                adminContext,
                machinePrincipalAccessor,
                settingsResolver,
                tenantContext,
                cerbosClientFactory,
                Options.Create(new CerbosSettings { GrpcEndpoint = "http://localhost:3593", PlaintextMode = true }),
                Substitute.For<ILogger<CerbosAuthorizationService>>()),
            new FallbackAuthorizationService(
                adminContext,
                machinePrincipalAccessor,
                Substitute.For<IEventAuthoritySnapshotService>(),
                Substitute.For<IOrganizationMemberRepository>(),
                Substitute.For<IGroupMemberRepository>(),
                settingsResolver,
                tenantContext,
                Substitute.For<ILogger<FallbackAuthorizationService>>()),
            Substitute.For<ICerbosConfigResolver>(),
            repository,
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<ILogger<RuntimeAuthorizationProvider>>(),
            Options.Create(new AuthorizationProviderDeploymentOptions()));

        runtimeProvider.InvalidateInstanceMode();

        var result = await runtimeProvider.CheckSettingAccessAsync(
            GovernanceSettingKeys.Security.AuthorizationProvider,
            "update");

        await Assert.That(result).IsTrue();
        await cerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    /// <summary>
    /// There is no "both" provider mode. In Cerbos mode every check in the batch must be decided by the
    /// PDP, including the pre-create and handler-adjacent capabilities that were previously routed to the
    /// local evaluator. That carve-out made the local evaluator a second production authority: tightening
    /// a Cerbos rule for those capabilities would have had no effect, silently.
    /// </summary>
    [Test]
    [Arguments(ResourceKinds.AiConversation, AuthorizationActions.View, "conversation")]
    [Arguments(ResourceKinds.Event, AuthorizationActions.Create, "create")]
    [Arguments(ResourceKinds.Organization, AuthorizationActions.Create, "create")]
    [Arguments(ResourceKinds.EventSession, AuthorizationActions.Create, "create")]
    [Arguments(ResourceKinds.StorageObject, AuthorizationActions.Create, "CreateStorageUploadSessionCommand")]
    public async Task IsAllowedBatchAsync_InCerbosMode_RoutesEveryCapabilityToThePdp(
        string resourceKind,
        string action,
        string resourceId)
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));

        // The PDP denies. If any of these capabilities were still answered locally, an instance admin
        // would be allowed and this assertion would fail.
        var cerbosResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        cerbosResponse.Results.Add(CreateResultEntry(resourceId, resourceKind, action, Effect.Deny));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(cerbosResponse));

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
            [TestAuthorizationRequest.Create(resourceKind, resourceId, action)]);

        await Assert.That(results).IsEquivalentTo([false]);
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task AuthorizeBatchAsync_InCerbosMode_ReturnsThePdpDecisionWithoutAdminApiRevisionLookup()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));

        var cerbosResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        cerbosResponse.Results.Add(CreateResultEntry(
            ResourceId,
            ResourceKinds.Event,
            AuthorizationActions.Update,
            Effect.Allow));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(cerbosResponse));

        var results = await fixture.RuntimeProvider.AuthorizeBatchAsync(
            [TestAuthorizationRequest.Create(ResourceKinds.Event, ResourceId, AuthorizationActions.Update)]);

        await Assert.That(results[0].Outcome).IsEqualTo(AuthorizationDecisionOutcome.Allow);
        await Assert.That(results[0].Provider.ProviderId)
            .IsEqualTo(AuthorizationProviderMetadata.Cerbos.ProviderId);
        await Assert.That(results[0].Provider.ObservedRevision).IsNull();
    }

    /// <summary>
    /// Task 3.2: every decision the PEP returns must be counted, with the reason code and deciding
    /// provider that actually produced it. A decision that escapes the emission point is a decision that
    /// never appears in the operator's allow/deny rates, which is where a policy regression shows up first.
    /// </summary>
    [Test]
    public async Task AuthorizeBatchAsync_EmitsOneBoundedMeasurementPerDecision()
    {
        using var capture = new AuthorizationMetricsCapture(expectedCount: 2);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        using var metrics = new BusinessMetrics(meterFactory);

        var fixture = CreateRuntimeProviderFixture(metrics: metrics);
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));

        var cerbosResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        cerbosResponse.Results.Add(CreateResultEntry(ResourceId, ResourceKinds.Event, AuthorizationActions.View, Effect.Allow));
        cerbosResponse.Results.Add(CreateResultEntry(ResourceId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(cerbosResponse));

        await fixture.RuntimeProvider.AuthorizeBatchAsync(
        [
            TestAuthorizationRequest.Create(ResourceKinds.Event, ResourceId, AuthorizationActions.View),
            TestAuthorizationRequest.Create(ResourceKinds.Event, ResourceId, AuthorizationActions.Update)
        ]);

        var counts = await capture.CountsAsync();

        await Assert.That(counts).Count().IsEqualTo(2);
        await Assert.That(counts.Select(count => count["action"]?.ToString()))
            .IsEquivalentTo(new[] { AuthorizationActions.View, AuthorizationActions.Update });

        // Both decisions came from the PDP and are counted at the single runtime emission point.
        await Assert.That(counts.Single(count => Equals(count["action"], AuthorizationActions.Update))["reason_code"]?.ToString())
            .IsEqualTo(AuthorizationDecisionReasonCodes.Allowed);
        await Assert.That(counts.Single(count => Equals(count["action"], AuthorizationActions.View))["outcome"]?.ToString())
            .IsEqualTo("allowed");
    }

    private sealed class AuthorizationMetricsCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _sync = new();
        private readonly List<IReadOnlyDictionary<string, object?>> _counts = [];
        private readonly int _expectedCount;
        private readonly TaskCompletionSource _expectedCountReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AuthorizationMetricsCapture(int expectedCount)
        {
            _expectedCount = expectedCount;
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == BusinessMetrics.MeterName
                        && instrument.Name == "explore.authorization.decisions")
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };

            _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            {
                var captured = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
                lock (_sync)
                {
                    _counts.Add(captured);
                    if (_counts.Count >= _expectedCount)
                    {
                        _expectedCountReached.TrySetResult();
                    }
                }
            });

            _listener.Start();
        }

        public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CountsAsync()
        {
            await _expectedCountReached.Task.WaitAsync(TimeSpan.FromSeconds(1));
            return Snapshot();
        }

        public void Dispose() => _listener.Dispose();

        private IReadOnlyList<IReadOnlyDictionary<string, object?>> Snapshot()
        {
            lock (_sync)
            {
                return [.. _counts];
            }
        }
    }

    private const string ResourceId = "11111111-1111-1111-1111-111111111111";

    /// <summary>
    /// A batch that mixes a previously carved-out capability with an ordinary one must reach the PDP as a
    /// single request. Two provider calls would mean the batch was split across two decision authorities.
    /// </summary>
    [Test]
    public async Task IsAllowedBatchAsync_InCerbosMode_SendsMixedBatchAsOneRequest()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));

        var eventId = Guid.NewGuid().ToString("D");
        var cerbosResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        cerbosResponse.Results.Add(CreateResultEntry("create", ResourceKinds.Event, AuthorizationActions.Create, Effect.Deny));
        cerbosResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.View, Effect.Deny));

        Cerbos.Api.V1.Request.CheckResourcesRequest? captured = null;
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                captured = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                return new CheckResourcesResponse(cerbosResponse);
            });

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(ResourceKinds.Event, "create", AuthorizationActions.Create),
            TestAuthorizationRequest.Create(ResourceKinds.Event, eventId, AuthorizationActions.View),
        ]);

        await Assert.That(results).IsEquivalentTo([false, false]);
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Resources).Count().IsEqualTo(2);
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithInstanceCerbosUnavailable_DeniesInsteadOfFallingBackToLocalRbac()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_tenant",
            TestTenantId.ToString(),
            "create");

        await Assert.That(result).IsFalse();
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task AuthorizeBatchAsync_WithInstanceCerbosUnavailable_DeniesSettingChecksWithSafeMetadata()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var results = await fixture.RuntimeProvider.AuthorizeBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.InstanceSetting,
                "storage",
                AuthorizationActions.InstanceSettings.Update,
                new Dictionary<string, object> { ["settingKey"] = "storage" }),
            TestAuthorizationRequest.Create(
                ResourceKinds.InstanceSetting,
                "storage",
                AuthorizationActions.InstanceSettings.View,
                new Dictionary<string, object> { ["settingKey"] = "storage" })
        ]);

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results.All(result =>
            result.Outcome == AuthorizationDecisionOutcome.Deny &&
            result.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable &&
            result.Provider == AuthorizationProviderMetadata.Cerbos &&
            result.Provider.ObservedRevision is null)).IsTrue();
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
        var errorLog = fixture.RuntimeLogger.ReceivedCalls()
            .Single(call => Equals(call.GetArguments()[0], LogLevel.Error))
            .GetArguments()[2]
            ?.ToString();
        await Assert.That(ProviderUnavailableLogStateIsSafe(errorLog)).IsTrue();
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithInstanceCerbosUnavailable_KeepsNonSettingChecksFailClosed()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_tenant",
            TestTenantId.ToString(),
            "create");

        await Assert.That(result).IsFalse();
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_InLocalMode_AllowsOwnerScopedAiConversations()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.AiConversation,
                "GetAiConversationListQuery",
                AuthorizationActions.AiConversations.View,
                null),
            TestAuthorizationRequest.Create(
                ResourceKinds.AiConversation,
                "CreateAiConversationCommand",
                AuthorizationActions.AiConversations.Create,
                null)
        ]);

        await Assert.That(results).IsEquivalentTo([true, true]);
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_InLocalMode_AllowsEventPreCreate()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.Event,
                CreateEventCommand.PreCreateResourceId,
                AuthorizationActions.Create,
                facts: new PreCreateAuthorizationFacts(Guid.Empty))
        ]);

        await Assert.That(results).IsEquivalentTo([true]);
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_InLocalMode_AllowsOrganizationPreCreate()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.Organization,
                CreateOrganizationCommand.PreCreateResourceId,
                AuthorizationActions.Create,
                new Dictionary<string, object>
                {
                    ["authorizationPhase"] = CreateOrganizationCommand.PreCreateAuthorizationPhase
                })
        ]);

        await Assert.That(results).IsEquivalentTo([true]);
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_InLocalMode_AllowsEventSessionPreCreateForTenantAdmin()
    {
        var fixture = CreateRuntimeProviderFixture();
        var eventId = Guid.NewGuid();
        var categoryId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        fixture.AdminContext.UserId.Returns(userId);
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsTenantAdminAsync(TestTenantId, Arg.Any<CancellationToken>()).Returns(true);
        fixture.AdminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([TestTenantId]);
        fixture.AdminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.EventSession,
                eventId.ToString(),
                AuthorizationActions.Create,
                new Dictionary<string, object>
                {
                    ["tenantId"] = TestTenantId.ToString(),
                    ["eventId"] = eventId.ToString(),
                    ["authorizationPhase"] = AuthorizationPhases.PreCreate
                }),
            TestAuthorizationRequest.Create(
                ResourceKinds.Category,
                categoryId,
                AuthorizationActions.Create,
                new Dictionary<string, object>
                {
                    ["tenantId"] = TestTenantId.ToString()
                },
                new AuthorizationScope(TenantId: TestTenantId.ToString()))
        ]);

        // A tenant administrator may pre-create a session and create a category in their own tenant.
        await Assert.That(results).IsEquivalentTo([true, true]);
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AuthorizeBatchAsync_InLocalMode_UsesTypedStorageUploadContract(
        bool canonicalResourceId)
    {
        var fixture = CreateRuntimeProviderFixture();
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        fixture.AdminContext.UserId.Returns(userId);
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsOrganizationAdminAsync(organizationId, Arg.Any<CancellationToken>()).Returns(true);

        var results = await fixture.RuntimeProvider.AuthorizeBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.StorageObject,
                canonicalResourceId
                    ? nameof(CreateStorageUploadSessionCommand)
                    : "019ecd1d-6b34-7b05-9945-970edd3c1440",
                AuthorizationActions.Create,
                facts: new StorageUploadIntentFacts(
                    userId,
                    TestTenantId,
                    StorageOwningResourceKinds.OrganizationTenant,
                    Guid.NewGuid(),
                    organizationId))
        ]);

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(results[0].Outcome).IsEqualTo(
            canonicalResourceId ? AuthorizationDecisionOutcome.Allow : AuthorizationDecisionOutcome.Deny);
        await Assert.That(results[0].ReasonCode).IsEqualTo(
            canonicalResourceId ? AuthorizationDecisionReasonCodes.Allowed : AuthorizationDecisionReasonCodes.Denied);
        await Assert.That(results[0].Provider).IsEqualTo(AuthorizationProviderMetadata.Local);
        await Assert.That(results[0].Provider.ObservedRevision).IsNull();
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_InLocalMode_AllowsSelfProfileUpdateOnly()
    {
        var fixture = CreateRuntimeProviderFixture();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        fixture.AdminContext.UserId.Returns(userId);
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsTenantAdminAsync(TestTenantId, Arg.Any<CancellationToken>()).Returns(false);

        var attributes = new Dictionary<string, object>
        {
            ["tenantId"] = TestTenantId.ToString()
        };

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(ResourceKinds.User, userId.ToString(), AuthorizationActions.Update, attributes),
            TestAuthorizationRequest.Create(ResourceKinds.User, otherUserId.ToString(), AuthorizationActions.Update, attributes)
        ]);

        await Assert.That(results).IsEquivalentTo([true, false]);
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedAsync_WhenProviderModeReadFails_UsesCerbosFailClosedPathAndSafeLogMetadata()
    {
        var fixture = CreateRuntimeProviderFixture();
        var secretMessage = "authorization provider setting failed for token abc123";
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns<SystemSetting?>(_ => throw new InvalidOperationException(secretMessage));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_tenant",
            TestTenantId.ToString(),
            "create");

        await Assert.That(result).IsFalse();
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
        fixture.RuntimeLogger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => ProviderModeFailureLogStateIsRedacted(state, secretMessage)),
            Arg.Is<Exception?>(ex => ex == null),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithByoClosedPdpUnavailable_ActivatesSafeModeAndDeniesNonInstanceAdmin()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsTenantAdminAsync(TestTenantId, Arg.Any<CancellationToken>()).Returns(true);
        fixture.CerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(CreateByoConfiguration());
        fixture.ByoCerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_category",
            Guid.NewGuid().ToString(),
            "create");

        await Assert.That(result).IsFalse();
        await Assert.That(fixture.LocalProvider.SafeMode).IsTrue();
        await fixture.ByoCerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_WhenByoConfigResolutionFails_ActivatesSafeModeInsteadOfUsingLocalRbac()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsTenantAdminAsync(TestTenantId, Arg.Any<CancellationToken>()).Returns(true);
        fixture.CerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>())
            .Returns<CerbosConfiguration?>(_ => throw new InvalidOperationException("tenant-secret resolver failure"));

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_category",
            Guid.NewGuid().ToString(),
            "create");

        await Assert.That(result).IsFalse();
        await Assert.That(fixture.LocalProvider.SafeMode).IsTrue();
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithByoClosedBlankEndpoint_ActivatesSafeModeInsteadOfInstanceFallback()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsTenantAdminAsync(TestTenantId, Arg.Any<CancellationToken>()).Returns(true);
        fixture.CerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new CerbosConfiguration
        {
            Endpoint = string.Empty,
            Mode = CerbosMode.CustomEndpoint,
            IsInstanceDefault = false
        });
        fixture.ByoCerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_category",
            Guid.NewGuid().ToString(),
            "create");

        await Assert.That(result).IsFalse();
        await Assert.That(fixture.LocalProvider.SafeMode).IsTrue();
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithByoClosedFailure_DeniesMachineCallerAfterSafeModeActivates()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns((Guid?)null);
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.MachinePrincipalAccessor.IsMachineCaller.Returns(true);
        fixture.MachinePrincipalAccessor.Current.Returns(new ApiKeyPrincipalContext(
            KeyId: $"key-{Guid.NewGuid():N}",
            TenantId: TestTenantId,
            OwnerType: ExternalApiKeyOwnerType.Tenant,
            OwnerId: TestTenantId,
            Scopes: [ExternalApiKeyScopes.AdminTenant]));
        fixture.CerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(CreateByoConfiguration());
        fixture.ByoCerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            "islamuevent_category",
            Guid.NewGuid().ToString(),
            "create",
            new Dictionary<string, object> { ["tenantId"] = TestTenantId });

        await Assert.That(result).IsFalse();
        await Assert.That(fixture.LocalProvider.SafeMode).IsTrue();
        await fixture.ByoCerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task AuthorizeAsyncSupportBoundaryWarningExcludesResourceAndSessionIdentifiers()
    {
        const string sentinelLocationId = "019d2f35-47d8-7b2d-96d3-570cc42f8c11";
        var sentinelSupportSessionId = Guid.Parse("019d2f35-866c-790c-8dcd-eed429c4f322");
        var logger = new TestListLogger<RuntimeAuthorizationProvider>();
        var fixture = CreateRuntimeProviderFixture(runtimeLogger: logger);
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSupportContext(
                SupportAccessModeEnum.ReadOnly,
                TestTenantId,
                sessionId: sentinelSupportSessionId));

        var decision = await fixture.RuntimeProvider.AuthorizeAsync(TestAuthorizationRequest.Create(
            ResourceKinds.Location,
            sentinelLocationId,
            AuthorizationActions.Locations.ApproveTenantAddress,
            new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") }));

        await Assert.That(decision.IsAllowed).IsFalse();
        var warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        await Assert.That(warning.State.Select(property => property.Key).ToHashSet(StringComparer.Ordinal).SetEquals(
            ["ResourceKind", "Action", "Reason", "{OriginalFormat}"])).IsTrue();
        await Assert.That(LogEntryExcludes(
            warning,
            sentinelLocationId,
            sentinelSupportSessionId.ToString("D"),
            "ResourceId",
            "SupportAccessSessionId")).IsTrue();
        await Assert.That(warning.Arguments.Select(argument => argument?.ToString() ?? string.Empty)).IsEquivalentTo(
            [ResourceKinds.Location, AuthorizationActions.Locations.ApproveTenantAddress, "support_access_read_only"]);
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithReadOnlySupportAccess_DeniesWriteEvenWhenInstanceAdmin()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSupportContext(SupportAccessModeEnum.ReadOnly, TestTenantId));

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Category,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Create,
            new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithWriteSupportAccess_DeniesCrossTenantResource()
    {
        var fixture = CreateRuntimeProviderFixture();
        var otherTenantId = Guid.NewGuid();

        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSupportContext(SupportAccessModeEnum.Write, TestTenantId));

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Category,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Update,
            new Dictionary<string, object> { ["tenantId"] = otherTenantId.ToString("D") });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithInactiveForwardedSupportAccess_DeniesTenantResource()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(SupportAccessContext.InactiveForwarded);

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Category,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.View,
            new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") });

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithReadOnlySupportAccess_FiltersWritesFromHalStyleBatch()
    {
        var fixture = CreateRuntimeProviderFixture();
        var actorUserId = Guid.NewGuid();
        var categoryId = Guid.NewGuid().ToString("D");

        fixture.AdminContext.UserId.Returns(actorUserId);
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.AdminContext.IsInstanceAdminAsync(actorUserId, Arg.Any<CancellationToken>()).Returns(true);
        fixture.AdminContext.GetAdminTenantIdsAsync(actorUserId, Arg.Any<CancellationToken>()).Returns([]);
        fixture.AdminContext.GetAdminOrganizationIdsAsync(actorUserId, Arg.Any<CancellationToken>()).Returns([]);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSupportContext(SupportAccessModeEnum.ReadOnly, TestTenantId, actorUserId));

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var cerbosResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        cerbosResponse.Results.Add(CreateResultEntry(
            categoryId,
            ResourceKinds.Category,
            AuthorizationActions.View,
            Effect.Allow));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                return new CheckResourcesResponse(cerbosResponse);
            });

        var results = await fixture.RuntimeProvider.IsAllowedBatchAsync(
        [
            TestAuthorizationRequest.Create(
                ResourceKinds.Category,
                categoryId,
                AuthorizationActions.View,
                new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") }),
            TestAuthorizationRequest.Create(
                ResourceKinds.Category,
                categoryId,
                AuthorizationActions.Create,
                new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") })
        ]);

        await Assert.That(results).IsEquivalentTo([true, false]);
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.Resources).Count().IsEqualTo(1);

        // The support-access boundary is enforced here, not delegated to the policy: the write check is
        // dropped before the batch is sent, and the read check carries only its own resource facts.
        var forwarded = capturedRequest.Resources[0];
        await Assert.That(forwarded.Actions).IsEquivalentTo([AuthorizationActions.View]);
        await Assert.That(forwarded.Resource.Attr.Keys).IsEquivalentTo(["tenantId"]);
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    [Arguments(SupportAccessModeEnum.ReadOnly)]
    [Arguments(SupportAccessModeEnum.Write)]
    public async Task IsAllowedBatchAsync_WithForwardedSupportAccess_DoesNotBoundaryDenySupportSessionResource(
        SupportAccessModeEnum mode)
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSupportContext(mode, TestTenantId));

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.SupportAccessSession,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.SupportAccessSessions.Stop,
            new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") });

        await Assert.That(result).IsTrue();
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task IsAllowedBatchAsync_WithActiveSupportAccessMissingRequiredFacts_DeniesBeforeProvider(
        bool missingTargetTenant)
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.SupportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSupportContext(SupportAccessModeEnum.Write, missingTargetTenant ? null : TestTenantId));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var attributes = missingTargetTenant
            ? new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") }
            : [];

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Category,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.View,
            attributes);

        await Assert.That(result).IsFalse();
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(true, null)]
    [Arguments(true, "")]
    [Arguments(true, "\"\"")]
    [Arguments(true, "unsupported")]
    public async Task IsAllowedBatchAsync_WithMissingOrUnsupportedProviderMode_UsesLocalAuthorization(
        bool settingExists,
        string? storedValue)
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.AdminContext.IsTenantAdminAsync(TestTenantId, Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(settingExists ? CreateAuthorizationProviderSettingValue(storedValue) : null);
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Category,
            Guid.NewGuid().ToString("D"),
            AuthorizationActions.Create,
            new Dictionary<string, object> { ["tenantId"] = TestTenantId.ToString("D") });

        await Assert.That(result).IsTrue();
        await fixture.CerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    [Test]
    public async Task CheckSettingAccessAsync_WithInstanceCerbosUnavailable_DeniesInsteadOfUsingLocalParity()
    {
        var fixture = CreateRuntimeProviderFixture();
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.CheckSettingAccessAsync(
            GovernanceSettingKeys.Security.AuthorizationProvider,
            AuthorizationActions.InstanceSettings.Update);

        await Assert.That(result).IsFalse();
        await fixture.CerbosClient.Received(1).CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(),
            Arg.Any<Metadata>());
    }

    private static SupportAccessContext CreateSupportContext(
        SupportAccessModeEnum mode,
        Guid? targetTenantId,
        Guid? actorUserId = null,
        Guid? sessionId = null)
    {
        return new SupportAccessContext(
            true,
            sessionId ?? Guid.NewGuid(),
            actorUserId ?? Guid.NewGuid(),
            targetTenantId,
            null,
            mode,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(10),
            "support",
            "TICKET-1",
            WasForwarded: true);
    }

    private static Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry CreateResultEntry(
        string resourceId,
        string resourceKind,
        string action,
        Effect effect)
    {
        var entry = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
        {
            Resource = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry.Types.Resource
            {
                Id = resourceId,
                Kind = resourceKind
            }
        };
        entry.Actions.Add(action, effect);
        return entry;
    }

    private static RuntimeProviderFixture CreateRuntimeProviderFixture(
        string? deploymentProvider = null,
        BusinessMetrics? metrics = null,
        ILogger<RuntimeAuthorizationProvider>? runtimeLogger = null)
    {
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var machinePrincipalAccessor = Substitute.For<IMachinePrincipalAccessor>();
        machinePrincipalAccessor.IsMachineCaller.Returns(false);
        machinePrincipalAccessor.Current.Returns((Explore.Application.Authentication.ApiKeyPrincipalContext?)null);

        var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(TestTenantId);

        var cerbosClient = Substitute.For<ICerbosClient>();
        var byoCerbosClient = Substitute.For<ICerbosClient>();
        var cerbosClientFactory = Substitute.For<ICerbosClientFactory>();
        cerbosClientFactory.GetOrCreate(Arg.Any<string>()).Returns(byoCerbosClient);

        var cerbosConfigResolver = Substitute.For<ICerbosConfigResolver>();
        cerbosConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns((CerbosConfiguration?)null);

        var supportAccessSessionService = Substitute.For<ISupportAccessSessionService>();
        supportAccessSessionService.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(SupportAccessContext.Inactive);

        var systemSettingRepository = Substitute.For<ISystemSettingRepository>();
        systemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("local"));

        var localProvider = new FallbackAuthorizationService(
            adminContext,
            machinePrincipalAccessor,
            Substitute.For<IEventAuthoritySnapshotService>(),
            Substitute.For<IOrganizationMemberRepository>(),
            Substitute.For<IGroupMemberRepository>(),
            settingsResolver,
            tenantContext,
            Substitute.For<ILogger<FallbackAuthorizationService>>());

        var cerbosProvider = new CerbosAuthorizationService(
            cerbosClient,
            new CerbosPrincipalBuilder(
                adminContext,
                machinePrincipalAccessor,
                Substitute.For<IEventAuthoritySnapshotService>(),
                Substitute.For<IOrganizationMemberRepository>(),
                Substitute.For<IGroupMemberRepository>()),
            adminContext,
            machinePrincipalAccessor,
            settingsResolver,
            tenantContext,
            cerbosClientFactory,
            Options.Create(new CerbosSettings { GrpcEndpoint = "http://localhost:3593", PlaintextMode = true }),
            Substitute.For<ILogger<CerbosAuthorizationService>>());

        var effectiveRuntimeLogger = runtimeLogger ?? Substitute.For<ILogger<RuntimeAuthorizationProvider>>();

        var runtimeProvider = new RuntimeAuthorizationProvider(
            cerbosProvider,
            localProvider,
            cerbosConfigResolver,
            systemSettingRepository,
            new MemoryCache(new MemoryCacheOptions()),
            effectiveRuntimeLogger,
            Options.Create(new AuthorizationProviderDeploymentOptions
            {
                Provider = deploymentProvider
            }),
            supportAccessSessionService,
            metrics);

        return new RuntimeProviderFixture(
            runtimeProvider,
            localProvider,
            adminContext,
            cerbosConfigResolver,
            systemSettingRepository,
            cerbosClient,
            byoCerbosClient,
            machinePrincipalAccessor,
            supportAccessSessionService,
            effectiveRuntimeLogger);
    }

    private static SystemSetting CreateAuthorizationProviderSetting(string provider)
    {
        return CreateAuthorizationProviderSettingValue(JsonSerializer.Serialize(provider));
    }

    private static SystemSetting CreateAuthorizationProviderSettingValue(string? value)
    {
        return new SystemSetting
        {
            Id = Guid.NewGuid(),
            SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
            Value = value,
            ValueType = SettingValueType.String,
            IsLocked = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static CerbosConfiguration CreateByoConfiguration()
    {
        return new CerbosConfiguration
        {
            Endpoint = "https://tenant-cerbos.example:3593",
            Mode = CerbosMode.CustomEndpoint,
            IsInstanceDefault = false
        };
    }

    private static RpcException CreateUnavailableRpcException()
    {
        return new RpcException(new Status(StatusCode.Unavailable, "tenant-secret unavailable"));
    }

    private static bool LogEntryExcludes(TestLogEntry entry, params string[] forbiddenValues)
    {
        var observable = string.Join('|',
            entry.Message,
            string.Join('|', entry.State.Select(property => property.Key)),
            string.Join('|', entry.Arguments.Select(argument => argument?.ToString())));
        return forbiddenValues.All(value => !observable.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProviderModeFailureLogStateIsRedacted(object state, string exceptionMessage)
    {
        var rendered = state.ToString();
        return rendered is not null
            && rendered.Contains("FailureType=InvalidOperationException", StringComparison.Ordinal)
            && !rendered.Contains(exceptionMessage, StringComparison.Ordinal)
            && !rendered.Contains("abc123", StringComparison.Ordinal);
    }

    private static bool ProviderUnavailableLogStateIsSafe(string? rendered)
    {
        return rendered is not null
            && rendered.Contains("FailureType=RpcException", StringComparison.Ordinal)
            && !rendered.Contains("storage", StringComparison.OrdinalIgnoreCase)
            && !rendered.Contains("settingKey", StringComparison.Ordinal)
            && !rendered.Contains(TestTenantId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && !rendered.Contains("tenant-secret unavailable", StringComparison.Ordinal);
    }

    [Test]
    public async Task IsAllowedAsync_WithDeploymentCerbos_OverridesStoredLocalProvider()
    {
        var fixture = CreateRuntimeProviderFixture("cerbos");
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        fixture.CerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns<CheckResourcesResponse>(_ => throw CreateUnavailableRpcException());

        var result = await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Tenant,
            TestTenantId.ToString(),
            AuthorizationActions.Create);

        await Assert.That(result).IsFalse();
        await fixture.CerbosClient.Received(1)
            .CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedAsync_WithDeploymentLocal_OverridesStoredCerbosProvider()
    {
        var fixture = CreateRuntimeProviderFixture("local");
        fixture.SystemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider)
            .Returns(CreateAuthorizationProviderSetting("cerbos"));
        fixture.AdminContext.UserId.Returns(Guid.NewGuid());
        fixture.AdminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);

        await fixture.RuntimeProvider.IsAllowedAsync(
            ResourceKinds.Tenant,
            TestTenantId.ToString(),
            AuthorizationActions.Create);

        await fixture.CerbosClient.DidNotReceive()
            .CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    private sealed record RuntimeProviderFixture(
        RuntimeAuthorizationProvider RuntimeProvider,
        FallbackAuthorizationService LocalProvider,
        IAdminContext AdminContext,
        ICerbosConfigResolver CerbosConfigResolver,
        ISystemSettingRepository SystemSettingRepository,
        ICerbosClient CerbosClient,
        ICerbosClient ByoCerbosClient,
        IMachinePrincipalAccessor MachinePrincipalAccessor,
        ISupportAccessSessionService SupportAccessSessionService,
        ILogger<RuntimeAuthorizationProvider> RuntimeLogger);
}
