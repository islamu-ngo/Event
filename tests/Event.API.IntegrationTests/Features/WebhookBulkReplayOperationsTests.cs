using System.Reflection;
using System.Security.Claims;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class WebhookBulkReplayOperationsTests
{
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _actorUserId = Guid.CreateVersion7();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IResourceAssembler<WebhookBulkReplayOperationDto, WebhookBulkReplayOperationDto> _assembler =
        Substitute.For<IResourceAssembler<WebhookBulkReplayOperationDto, WebhookBulkReplayOperationDto>>();

    public WebhookBulkReplayOperationsTests()
    {
        _tenantContext.TenantId.Returns(_tenantId);
    }

    [Test]
    public async Task Routes_UseAnonymousReadsAndAuthorizedWrites()
    {
        var controller = typeof(WebhookBulkReplaysController);
        string[] readActions =
        [
            nameof(WebhookBulkReplaysController.Preview),
            nameof(WebhookBulkReplaysController.GetOperations),
            nameof(WebhookBulkReplaysController.GetOperation)
        ];

        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        foreach (var actionName in readActions)
        {
            var action = controller.GetMethod(actionName)!;
            await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
            await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
                .IsEqualTo(EndpointClass.Public);
        }

        await Assert.That(typeof(PreviewWebhookBulkReplayQuery)
            .GetCustomAttribute<AuthorizeResourceAttribute>()?.Action)
            .IsEqualTo(AuthorizationActions.Webhooks.BulkReplay);
        await Assert.That(typeof(ScheduleWebhookBulkReplayCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()?.Action)
            .IsEqualTo(AuthorizationActions.Webhooks.BulkReplay);
        await Assert.That(typeof(CancelWebhookBulkReplayCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()?.Action)
            .IsEqualTo(AuthorizationActions.Webhooks.BulkReplay);
    }

    [Test]
    public async Task Links_ExposeCancelOnlyWhileQueued()
    {
        var policy = new WebhookBulkReplayDetailLinkPolicy();
        var queuedLinks = policy.GetLinks(CreateOperation("QUEUED"), null).ToArray();
        var completedLinks = policy.GetLinks(CreateOperation("COMPLETED"), null).ToArray();

        await Assert.That(queuedLinks.Any(link => link.Rel == LinkRelations.Cancel)).IsTrue();
        await Assert.That(completedLinks.Any(link => link.Rel == LinkRelations.Cancel)).IsFalse();
        await Assert.That(queuedLinks.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(queuedLinks.Any(link => link.Rel == LinkRelations.Collection)).IsTrue();
    }

    [Test]
    public async Task Preview_NormalizesOffsetlessGeneratedClientDatesAsUtc()
    {
        var fromBoundary = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Unspecified);
        var toBoundary = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var expectedFromUtc = DateTime.SpecifyKind(fromBoundary, DateTimeKind.Utc);
        var expectedToUtc = DateTime.SpecifyKind(toBoundary, DateTimeKind.Utc);
        _mediator.Send(Arg.Any<PreviewWebhookBulkReplayQuery>(), Arg.Any<CancellationToken>())
            .Returns(WebhookBulkReplayPreviewResult.Succeeded(new WebhookBulkReplayPreviewDto
            {
                Filter = new WebhookBulkReplayFilterDto
                {
                    FromUtc = expectedFromUtc,
                    ToUtc = expectedToUtc,
                    MaxItems = 100
                }
            }));
        var controller = CreateController();

        var result = await controller.Preview(
            fromBoundary,
            toBoundary,
            maxItems: 100,
            cancellationToken: CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await _mediator.Received(1).Send(
            Arg.Is<PreviewWebhookBulkReplayQuery>(query =>
                query.FromUtc == expectedFromUtc &&
                query.FromUtc.Kind == DateTimeKind.Utc &&
                query.ToUtc == expectedToUtc &&
                query.ToUtc.Kind == DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Schedule_MapsServerOwnedScopeAndReturnsAcceptedOperationLocation()
    {
        var operationId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<ScheduleWebhookBulkReplayCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                operationId,
                "Webhook bulk replay operation queued."));
        var controller = CreateController();
        var operationKey = Guid.CreateVersion7();
        var fromUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.Schedule(new ScheduleWebhookBulkReplayRequestDto
        {
            OperationKey = operationKey,
            ReasonCode = "operator.incident-recovery",
            Filter = new WebhookBulkReplayFilterDto
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                EventType = "event.published",
                MaxItems = 25
            }
        }, CancellationToken.None);

        var accepted = result.Result as AcceptedAtRouteResult;
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.RouteName).IsEqualTo(RouteNames.GetWebhookBulkReplayById);
        await Assert.That(accepted.RouteValues!["operationId"]).IsEqualTo(operationId);
        await _mediator.Received(1).Send(
            Arg.Is<ScheduleWebhookBulkReplayCommand>(command =>
                command.TenantId == _tenantId &&
                command.ActorUserId == _actorUserId &&
                command.OperationKey == operationKey &&
                command.FromUtc == fromUtc &&
                command.ToUtc == toUtc &&
                command.EventType == "event.published" &&
                command.MaxItems == 25),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Schedule_NormalizesOffsetlessGeneratedClientDatesAsUtc()
    {
        var operationId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<ScheduleWebhookBulkReplayCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                operationId,
                "Webhook bulk replay operation queued."));
        var controller = CreateController();
        var fromBoundary = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Unspecified);
        var toBoundary = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var expectedFromUtc = DateTime.SpecifyKind(fromBoundary, DateTimeKind.Utc);
        var expectedToUtc = DateTime.SpecifyKind(toBoundary, DateTimeKind.Utc);

        await controller.Schedule(new ScheduleWebhookBulkReplayRequestDto
        {
            OperationKey = Guid.CreateVersion7(),
            ReasonCode = "operator.generated-client",
            Filter = new WebhookBulkReplayFilterDto
            {
                FromUtc = fromBoundary,
                ToUtc = toBoundary,
                MaxItems = 100
            }
        }, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<ScheduleWebhookBulkReplayCommand>(command =>
                command.FromUtc == expectedFromUtc &&
                command.FromUtc.Kind == DateTimeKind.Utc &&
                command.ToUtc == expectedToUtc &&
                command.ToUtc.Kind == DateTimeKind.Utc),
            Arg.Any<CancellationToken>());
    }

    private WebhookBulkReplaysController CreateController()
    {
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(_actorUserId);
        userContext.GetRequiredUserId().Returns(_actorUserId);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        services.AddSingleton(userContext);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", _actorUserId.ToString("D"))],
                authenticationType: "TestAuth"))
        };
        return new WebhookBulkReplaysController(_mediator, _tenantContext, _assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private WebhookBulkReplayOperationDto CreateOperation(string statusCode) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = _tenantId,
            OperationKey = Guid.CreateVersion7(),
            StatusId = statusCode == "QUEUED" ? 1 : 3,
            StatusCode = statusCode,
            StatusName = statusCode,
            Filter = new WebhookBulkReplayFilterDto
            {
                FromUtc = DateTime.UtcNow.AddDays(-1),
                ToUtc = DateTime.UtcNow,
                MaxItems = 10
            },
            ReasonCode = "operator.recovery",
            ConcurrencyVersion = 1,
            QueuedAt = DateTime.UtcNow
        };
}
