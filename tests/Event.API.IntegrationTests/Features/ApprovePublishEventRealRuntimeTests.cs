using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Lifecycle;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.Settings;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Runtime)]
[ClassDataSource<RealRuntimeApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("RealRuntimeDb")]
public sealed class ApprovePublishEventRealRuntimeTests(RealRuntimeApiFixture fixture)
{
    private const string OperationName = "ApprovePublishEvent";

    [Test]
    public async Task MatchingTenantAdministratorApprovesAndPublishesReadyDraftExactlyOnce()
    {
        await fixture.ResetDatabaseAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        TenantScenarioSeed.TenantScenarioResult tenant;
        Guid eventId;
        Guid concurrencyStamp;
        bool effectiveRequireApproval;
        bool readyToPublish;

        using (var arrangeScope = fixture.Factory.Services.CreateScope())
        {
            IServiceProvider services = arrangeScope.ServiceProvider;
            var context = services.GetRequiredService<ExploreDbContext>();
            tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
            (eventId, concurrencyStamp) = await SeedReadyDraftAsync(context, tenant, timeout.Token);

            var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var mutationBoundary = services.GetRequiredService<IPublicationPolicyMutationBoundary>();
            PublicationPolicyMutationResult mutation = await unitOfWork.ExecuteInTransactionAsync(
                token => mutationBoundary.ApplyTenantAsync(
                    new PublicationPolicyTenantMutationRequest(
                        tenant.TenantId,
                        tenant.ActorId,
                        DateTime.UtcNow,
                        [new PublicationPolicySettingMutation(
                            GovernanceSettingKeys.Events.RequireApproval,
                            PublicationPolicyMutationKind.Set,
                            SettingValueSerializer.Serialize(true),
                            tenant.TenantId,
                            IsLocked: null)],
                        PublicationPolicyLockedSystemBehavior.Reject),
                    token),
                timeout.Token);
            await Assert.That(mutation.Success).IsTrue().Because(mutation.Message);
            settings.InvalidateCache(SettingScope.Tenant, tenant.TenantId);

            effectiveRequireApproval = await settings.ResolveAsync<bool>(
                GovernanceSettingKeys.Events.RequireApproval,
                new SettingContext(TenantId: tenant.TenantId),
                timeout.Token);

            var policyProvider = services.GetRequiredService<IEventLifecyclePolicyProvider>();
            EventLifecyclePolicy policy = await policyProvider.GetEffectivePolicyAsync(
                tenant.TenantId,
                ValidationProfile.EventPublish,
                timeout.Token);
            var readiness = services.GetRequiredService<IEventLifecycleReadinessEvaluator>();
            var draft = await context.Events.SingleAsync(value => value.Id == eventId, timeout.Token);
            readyToPublish = policy.RequiresApproval && readiness.Evaluate(draft, policy.Profile, policy).IsReady;
        }

        var payload = new PublishEventRequestDto { ExpectedConcurrencyStamp = concurrencyStamp };

        using var ordinaryPublishRequest = fixture.CreateTenantAdminRequest(
            HttpMethod.Post,
            $"/api/event/{eventId:D}/publish",
            tenant.TenantId,
            tenant.UserId);
        ordinaryPublishRequest.Headers.Accept.ParseAdd("application/problem+json");
        ordinaryPublishRequest.Content = JsonContent.Create(payload);
        using HttpResponseMessage ordinaryPublishResponse = await fixture.Client.SendAsync(
            ordinaryPublishRequest,
            timeout.Token);
        string? ordinaryPublishCode = await ReadProblemCodeAsync(ordinaryPublishResponse, timeout.Token);

        using var approvalRequest = fixture.CreateTenantAdminRequest(
            HttpMethod.Post,
            $"/api/event/{eventId:D}/approve-publish",
            tenant.TenantId,
            tenant.UserId);
        approvalRequest.Content = JsonContent.Create(payload);
        using HttpResponseMessage approvalResponse = await fixture.Client.SendAsync(approvalRequest, timeout.Token);
        BaseCommandResponse<Guid>? approvalBody = approvalResponse.StatusCode == HttpStatusCode.OK
            ? await approvalResponse.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>(timeout.Token)
            : null;

        EndpointContractObservables endpointContract = ReadEndpointContract(fixture);

        using var verifyScope = fixture.Factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var persistedEvent = await verifyContext.Events
            .IgnoreQueryFilters()
            .SingleAsync(value => value.Id == eventId, timeout.Token);
        int notificationOutboxCount = await verifyContext.OutboxMessages
            .IgnoreQueryFilters()
            .CountAsync(
                value => value.AggregateId == eventId
                    && value.EventType == EventPublishedOutboxMessageFactory.EventPublishedNotificationFanoutRequestedEventType,
                timeout.Token);

        var actual = new ApprovalPublicationObservables(
            EffectiveRequireApproval: effectiveRequireApproval,
            ReadyToPublish: readyToPublish,
            OrdinaryPublishStatus: ordinaryPublishResponse.StatusCode,
            OrdinaryPublishProblemCode: ordinaryPublishCode,
            ApprovalStatus: approvalResponse.StatusCode,
            ApprovalSucceeded: approvalBody?.IsSuccess == true,
            ApprovalEventId: approvalBody?.Id,
            PersistedStatus: (EventStatusEnum)persistedEvent.EventStatusId,
            NotificationOutboxCount: notificationOutboxCount,
            Endpoint: endpointContract);
        var expected = new ApprovalPublicationObservables(
            EffectiveRequireApproval: true,
            ReadyToPublish: true,
            OrdinaryPublishStatus: HttpStatusCode.BadRequest,
            OrdinaryPublishProblemCode: "event_publish_approval_required",
            ApprovalStatus: HttpStatusCode.OK,
            ApprovalSucceeded: true,
            ApprovalEventId: eventId,
            PersistedStatus: EventStatusEnum.Published,
            NotificationOutboxCount: 1,
            Endpoint: EndpointContractObservables.Required);

        await Assert.That(actual).IsEqualTo(expected);
    }

    private static async Task<(Guid EventId, Guid ConcurrencyStamp)> SeedReadyDraftAsync(
        ExploreDbContext context,
        TenantScenarioSeed.TenantScenarioResult tenant,
        CancellationToken cancellationToken)
    {
        DateTimeOffset sessionStart = DateTimeOffset.UtcNow.AddDays(7);
        var draft = new EventBuilder()
            .WithTitle("Approval Publication Contract Event")
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(EventStatusEnum.Draft)
            .WithVisibility(VisibilityTypeEnum.Public)
            .Build();
        var session = new Explore.Domain.EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = draft.Id,
            Event = draft,
            TenantId = tenant.TenantId,
            Tenant = null!,
            Title = "Approval Publication Contract Session",
            SortOrder = 1,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        session.Reschedule(
            UtcInstantRange.Create(sessionStart, sessionStart.AddHours(1)),
            "UTC",
            new EventScheduleProjectionCalculator());
        draft.Sessions.Add(session);
        draft.RecalculateScheduleSummaryFromSessions();

        context.Events.Add(draft);
        await context.SaveChangesAsync(cancellationToken);
        Guid persistedStamp = await context.Events
            .Where(value => value.Id == draft.Id)
            .Select(value => value.ConcurrencyStamp)
            .SingleAsync(cancellationToken);
        return (draft.Id, persistedStamp);
    }

    private static async Task<string?> ReadProblemCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("code", out JsonElement code)
            ? code.GetString()
            : null;
    }

    private static EndpointContractObservables ReadEndpointContract(RealRuntimeApiFixture runtimeFixture)
    {
        EndpointDataSource dataSource = runtimeFixture.Factory.Services.GetRequiredService<EndpointDataSource>();
        RouteEndpoint? endpoint = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == OperationName);
        if (endpoint is null)
        {
            return EndpointContractObservables.Missing;
        }

        IReadOnlyList<ProducesResponseTypeAttribute> responses = endpoint.Metadata
            .GetOrderedMetadata<ProducesResponseTypeAttribute>();
        return new EndpointContractObservables(
            IsMapped: true,
            RouteTemplate: endpoint.RoutePattern.RawText,
            HttpMethod: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.SingleOrDefault(),
            OperationName: endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
            RequiresAuthentication: endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null,
            Classification: endpoint.Metadata.GetMetadata<EndpointClassificationAttribute>()?.Class,
            RateLimitPolicy: endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName,
            TimeoutPolicy: endpoint.Metadata.GetMetadata<RequestTimeoutAttribute>()?.PolicyName,
            HasSuccessResponse: HasResponse(responses, StatusCodes.Status200OK, typeof(BaseCommandResponse<Guid>)),
            HasValidationProblem: HasResponse(responses, StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails)),
            HasUnauthorizedProblem: HasResponse(responses, StatusCodes.Status401Unauthorized, typeof(ProblemDetails)),
            HasForbiddenProblem: HasResponse(responses, StatusCodes.Status403Forbidden, typeof(ProblemDetails)),
            HasNotFoundProblem: HasResponse(responses, StatusCodes.Status404NotFound, typeof(ProblemDetails)),
            HasConflictProblem: HasResponse(responses, StatusCodes.Status409Conflict, typeof(ProblemDetails)));
    }

    private static bool HasResponse(
        IEnumerable<ProducesResponseTypeAttribute> responses,
        int statusCode,
        Type responseType) => responses.Any(response =>
            response.StatusCode == statusCode && response.Type == responseType);

    private sealed record ApprovalPublicationObservables(
        bool EffectiveRequireApproval,
        bool ReadyToPublish,
        HttpStatusCode OrdinaryPublishStatus,
        string? OrdinaryPublishProblemCode,
        HttpStatusCode ApprovalStatus,
        bool ApprovalSucceeded,
        Guid? ApprovalEventId,
        EventStatusEnum PersistedStatus,
        int NotificationOutboxCount,
        EndpointContractObservables Endpoint);

    private sealed record EndpointContractObservables(
        bool IsMapped,
        string? RouteTemplate,
        string? HttpMethod,
        string? OperationName,
        bool RequiresAuthentication,
        EndpointClass? Classification,
        string? RateLimitPolicy,
        string? TimeoutPolicy,
        bool HasSuccessResponse,
        bool HasValidationProblem,
        bool HasUnauthorizedProblem,
        bool HasForbiddenProblem,
        bool HasNotFoundProblem,
        bool HasConflictProblem)
    {
        public static EndpointContractObservables Missing { get; } = new(
            false, null, null, null, false, null, null, null,
            false, false, false, false, false, false);

        public static EndpointContractObservables Required { get; } = new(
            IsMapped: true,
            RouteTemplate: "api/Event/{id:guid}/approve-publish",
            HttpMethod: HttpMethods.Post,
            OperationName: ApprovePublishEventRealRuntimeTests.OperationName,
            RequiresAuthentication: true,
            Classification: EndpointClass.Authenticated,
            RateLimitPolicy: RateLimitingExtensions.WritePolicy,
            TimeoutPolicy: RequestTimeoutExtensions.DefaultPolicy,
            HasSuccessResponse: true,
            HasValidationProblem: true,
            HasUnauthorizedProblem: true,
            HasForbiddenProblem: true,
            HasNotFoundProblem: true,
            HasConflictProblem: true);
    }
}
