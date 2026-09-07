using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Actor;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class ActorModerationControllerContractTests
{
    private const string BaseUrl = "/api/actor";

    [Test]
    public async Task GlobalModerationRequestDto_ContainsOnlyReasonCode()
    {
        var properties = typeof(GlobalModerationRequestDto).GetProperties();

        await Assert.That(properties.Length).IsEqualTo(1);
        await Assert.That(properties[0].Name).IsEqualTo(nameof(GlobalModerationRequestDto.ReasonCode));
    }

    [Test]
    public async Task ModerationRoutes_DeclareRequiredContractMetadata()
    {
        var expectedRoutes = new Dictionary<string, (string Template, string Name)>
        {
            [nameof(ActorController.SuspendActor)] = (
                "{actorId:guid}/moderation/suspend",
                RouteNames.SuspendActor),
            [nameof(ActorController.ReinstateActor)] = (
                "{actorId:guid}/moderation/reinstate",
                RouteNames.ReinstateActor),
            [nameof(ActorController.SuspendAtprotoIdentity)] = (
                "atproto-identities/{identityId:guid}/moderation/suspend",
                RouteNames.SuspendAtprotoIdentity),
            [nameof(ActorController.ReinstateAtprotoIdentity)] = (
                "atproto-identities/{identityId:guid}/moderation/reinstate",
                RouteNames.ReinstateAtprotoIdentity)
        };

        foreach (var expectedRoute in expectedRoutes)
        {
            var method = typeof(ActorController).GetMethod(expectedRoute.Key)!;
            var post = method.GetCustomAttribute<HttpPostAttribute>();
            var classification = method.GetCustomAttribute<EndpointClassificationAttribute>();
            var rateLimit = method.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>();
            var responseTypes = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();

            await Assert.That(post).IsNotNull();
            await Assert.That(post!.Template).IsEqualTo(expectedRoute.Value.Template);
            await Assert.That(post.Name).IsEqualTo(expectedRoute.Value.Name);
            await Assert.That(method.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>())
                .IsNotNull();
            await Assert.That(classification?.Class).IsEqualTo(EndpointClass.Authenticated);
            await Assert.That(method.GetCustomAttributes()
                .Any(attribute => attribute.GetType().Name == "EndpointSummaryAttribute")).IsTrue();
            await Assert.That(method.GetCustomAttributes()
                .Any(attribute => attribute.GetType().Name == "EndpointDescriptionAttribute")).IsTrue();
            await Assert.That(method.GetCustomAttribute<ConsumesAttribute>()?.ContentTypes)
                .Contains("application/json");
            await Assert.That(rateLimit?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);

            await Assert.That(responseTypes.Any(response =>
                response.StatusCode == StatusCodes.Status200OK &&
                response.Type == typeof(BaseCommandResponse<Guid>))).IsTrue();
            foreach (var expectedResponse in new[]
                     {
                         (StatusCode: StatusCodes.Status400BadRequest, Type: typeof(ValidationProblemDetails)),
                        (StatusCode: StatusCodes.Status401Unauthorized, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status403Forbidden, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status429TooManyRequests, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status404NotFound, Type: typeof(ProblemDetails)),
                        (StatusCode: StatusCodes.Status409Conflict, Type: typeof(ProblemDetails))
                      })
            {
                await Assert.That(responseTypes.Any(response =>
                    response.StatusCode == expectedResponse.StatusCode &&
                    response.Type == expectedResponse.Type)).IsTrue();
            }
        }
    }

    [Test]
    [Arguments("{0}/moderation/suspend", "actor", "Suspend")]
    [Arguments("{0}/moderation/reinstate", "actor", "Reinstate")]
    [Arguments("atproto-identities/{0}/moderation/suspend", "identity", "Suspend")]
    [Arguments("atproto-identities/{0}/moderation/reinstate", "identity", "Reinstate")]
    public async Task AuthenticatedRoute_DispatchesServerSelectedAction(
        string routeFormat,
        string targetType,
        string expectedAction)
    {
        var mediator = new ModerationMediator();
        using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        var targetId = Guid.CreateVersion7();
        var route = string.Format(routeFormat, targetId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{route}")
        {
            Content = JsonContent.Create(new GlobalModerationRequestDto { ReasonCode = "policy-violation" })
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (targetType == "actor")
        {
            await Assert.That(mediator.LastActorCommand).IsNotNull();
            await Assert.That(mediator.LastActorCommand!.ActorId).IsEqualTo(targetId);
            await Assert.That(mediator.LastActorCommand.Moderation!.Action.ToString()).IsEqualTo(expectedAction);
            await Assert.That(mediator.LastActorCommand.Moderation.ReasonCode).IsEqualTo("policy-violation");
        }
        else
        {
            await Assert.That(mediator.LastIdentityCommand).IsNotNull();
            await Assert.That(mediator.LastIdentityCommand!.AtprotoIdentityId).IsEqualTo(targetId);
            await Assert.That(mediator.LastIdentityCommand.Moderation!.Action.ToString()).IsEqualTo(expectedAction);
            await Assert.That(mediator.LastIdentityCommand.Moderation.ReasonCode).IsEqualTo("policy-violation");
        }
    }

    [Test]
    public async Task ModerationValidationFailure_ReturnsValidationProblemDetails()
    {
        var mediator = new ModerationMediator
        {
            Response = BaseCommandResponse.Validation(
                ["ReasonCode must not be empty."],
                "Actor moderation failed validation.",
                Guid.CreateVersion7())
        };
        using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            $"{BaseUrl}/{Guid.CreateVersion7()}/moderation/suspend");
        request.Content = JsonContent.Create(new GlobalModerationRequestDto { ReasonCode = string.Empty });

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Global actor moderation validation failed");
    }

    [Test]
    public async Task UnresolvedApplicationUser_ReturnsAuthenticationRequiredProblemDetails()
    {
        var mediator = new ModerationMediator
        {
            Response = BaseCommandResponse.Authentication<Guid>(
                "Authenticated instance administrator context is required.")
        };
        using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            $"{BaseUrl}/{Guid.CreateVersion7()}/moderation/suspend");
        request.Content = JsonContent.Create(new GlobalModerationRequestDto { ReasonCode = "policy-violation" });

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.Unauthorized,
            "User ID not found in token");
    }

    [Test]
    public async Task InstanceAdminFailure_ReturnsForbiddenProblemDetails()
    {
        var mediator = new ModerationMediator
        {
            Response = BaseCommandResponse.Authorization<Guid>(
                "Only instance administrators can moderate global actors.")
        };
        using var factory = CreateFactory(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            $"{BaseUrl}/{Guid.CreateVersion7()}/moderation/suspend");
        request.Content = JsonContent.Create(new GlobalModerationRequestDto { ReasonCode = "policy-violation" });

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden, "Forbidden");
    }

    private static WebApplicationFactory<Program> CreateFactory(ModerationMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton<IMediator>(mediator);
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private sealed class ModerationMediator : IMediator
    {
        public BaseCommandResponse<Guid> Response { get; init; } =
            BaseCommandResponse.Success(Guid.CreateVersion7(), "Moderation updated.");

        public ModerateActorCommand? LastActorCommand { get; private set; }
        public ModerateAtprotoIdentityCommand? LastIdentityCommand { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                ModerateActorCommand command => Capture(command),
                ModerateAtprotoIdentityCommand command => Capture(command),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => request switch
            {
                ModerateActorCommand command => Task.FromResult<object?>(Capture(command)),
                ModerateAtprotoIdentityCommand command => Task.FromResult<object?>(Capture(command)),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private BaseCommandResponse<Guid> Capture(ModerateActorCommand command)
        {
            LastActorCommand = command;
            return Response;
        }

        private BaseCommandResponse<Guid> Capture(ModerateAtprotoIdentityCommand command)
        {
            LastIdentityCommand = command;
            return Response;
        }
    }
}
