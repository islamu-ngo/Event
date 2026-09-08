using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventSessionSpeakerControllerTests
{
    [Test]
    public async Task ManagementRoutes_UseStableCanonicalRouteNames()
    {
        await AssertRoute(
            nameof(EventSessionSpeakerController.GetBySession),
            typeof(HttpGetAttribute),
            "management/by-session/{eventSessionId:guid}",
            RouteNames.GetEventSessionSpeakersBySession);
        await AssertRoute(
            nameof(EventSessionSpeakerController.Create),
            typeof(HttpPostAttribute),
            "management/by-session/{eventSessionId:guid}",
            RouteNames.CreateEventSessionSpeaker);
        await AssertRoute(
            nameof(EventSessionSpeakerController.Update),
            typeof(HttpPatchAttribute),
            "management/{id:guid}",
            RouteNames.UpdateEventSessionSpeaker);
        await AssertRoute(
            nameof(EventSessionSpeakerController.Delete),
            typeof(HttpDeleteAttribute),
            "management/by-session/{eventSessionId:guid}/{id:guid}",
            RouteNames.DeleteEventSessionSpeaker);
    }

    [Test]
    public async Task DetailEditLink_UsesOnlyRelationshipIdForCanonicalPatchRoute()
    {
        var assignmentId = Guid.NewGuid();
        var policy = new EventSessionSpeakerDetailLinkPolicy();
        var edit = policy.GetLinks(new EventSessionSpeakerDto
        {
            Id = assignmentId,
            EventSessionId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            ActorId = Guid.NewGuid()
        }, null)
            .Single(link => link.Rel == LinkRelations.Edit);

        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateEventSessionSpeaker);
        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(edit.RouteValues!.GetType().GetProperty("id")?.GetValue(edit.RouteValues))
            .IsEqualTo(assignmentId);
        await Assert.That(edit.RouteValues.GetType().GetProperty("eventSessionId")).IsNull();
    }

    [Test]
    public async Task CollectionEditLink_UsesOnlyRelationshipIdForCanonicalPatchRoute()
    {
        var assignmentId = Guid.NewGuid();
        var edit = new EventSessionSpeakerCollectionLinkPolicy()
            .GetItemLinks(new EventSessionSpeakerListDto
            {
                Id = assignmentId,
                EventSessionId = Guid.NewGuid(),
                EventId = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                ActorId = Guid.NewGuid()
            }, null)
            .Single(link => link.Rel == LinkRelations.Edit);

        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Patch);
        await Assert.That(edit.RouteValues!.GetType().GetProperty("id")?.GetValue(edit.RouteValues))
            .IsEqualTo(assignmentId);
        await Assert.That(edit.RouteValues.GetType().GetProperty("eventSessionId")).IsNull();
    }

    [Test]
    public async Task Create_StampsSessionAndTenantContextFromRouteAuthorizationContext()
    {
        var eventSessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var clientSuppliedSessionId = Guid.NewGuid();

        using var mediator = new EventSessionSpeakerMediatorStub(request => request switch
        {
            GetEventSessionAuthorizationContextRequest contextRequest => new EventSessionAuthorizationContextDto
            {
                Id = contextRequest.EventSessionId,
                EventId = eventId,
                TenantId = tenantId
            },
            CreateEventSessionSpeakerCommand => BaseCommandResponse.Success(
                Guid.NewGuid(),
                "Speaker assignment created successfully."),
            _ => throw new InvalidOperationException($"Unexpected request: {request.GetType().Name}")
        });
        await using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/eventsessionspeaker/management/by-session/{eventSessionId:D}",
            new CreateEventSessionSpeakerDto
            {
                ActorId = actorId,
                EventSessionId = clientSuppliedSessionId
            });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Created);
        var command = mediator.LastRequest as CreateEventSessionSpeakerCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.EventId).IsEqualTo(eventId);
        await Assert.That(command.TenantId).IsEqualTo(tenantId);
        await Assert.That(command.SpeakerDto.ActorId).IsEqualTo(actorId);
        await Assert.That(command.SpeakerDto.EventSessionId).IsEqualTo(eventSessionId);
        await Assert.That(command.SpeakerDto.EventSessionId).IsNotEqualTo(clientSuppliedSessionId);
    }

    [Test]
    public async Task Update_WhenIfMatchIsMissing_ReturnsValidationProblemDetails()
    {
        using var mediator = new EventSessionSpeakerMediatorStub(_ => throw new InvalidOperationException("Mediator should not run when If-Match is missing."));
        await using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            $"/api/eventsessionspeaker/management/{Guid.NewGuid():D}",
            new UpdateEventSessionSpeakerDto
            {
                Actor = new UpdateEventSessionSpeakerActorDto { ActorId = Guid.NewGuid() }
            });

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            System.Net.HttpStatusCode.BadRequest,
            "Event session speaker validation failed");
        await Assert.That(mediator.LastRequest).IsNull();
    }

    [Test]
    public async Task Update_WhenIfMatchIsUnquoted_ReturnsValidationProblemDetails()
    {
        using var mediator = new EventSessionSpeakerMediatorStub(_ => throw new InvalidOperationException("Mediator should not run when If-Match is unquoted."));
        await using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            $"/api/eventsessionspeaker/management/{Guid.NewGuid():D}",
            new UpdateEventSessionSpeakerDto
            {
                Actor = new UpdateEventSessionSpeakerActorDto { ActorId = Guid.NewGuid() }
            });
        request.Headers.TryAddWithoutValidation("If-Match", Guid.NewGuid().ToString("D"));

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            System.Net.HttpStatusCode.BadRequest,
            "Event session speaker validation failed");
        await Assert.That(mediator.LastRequest).IsNull();
    }

    private static async Task AssertRoute(
        string actionName,
        Type httpMethodAttributeType,
        string template,
        string routeName)
    {
        var action = typeof(EventSessionSpeakerController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} not found.");
        var route = action.GetCustomAttributes()
            .Single(attribute => attribute.GetType() == httpMethodAttributeType) as HttpMethodAttribute;

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(template);
        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
    }

    private static WebApplicationFactory<Program> CreateFactoryWithMediator(IMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory();

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedJsonRequest<TValue>(
        HttpMethod method,
        string url,
        TValue body)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private sealed class EventSessionSpeakerMediatorStub(Func<object, object> responseFactory) : IMediator, IDisposable
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            object response = responseFactory(request);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<object?>(responseFactory(request));
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
