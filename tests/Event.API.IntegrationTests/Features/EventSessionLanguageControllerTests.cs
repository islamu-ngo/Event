using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Features.EventSessionLanguages.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventSessionLanguageControllerTests
{
    [Test]
    public async Task ManagedReadRoute_UsesAuthenticatedViewManagementContract()
    {
        var action = typeof(EventSessionLanguageController).GetMethod(
            nameof(EventSessionLanguageController.GetManagedBySession))!;
        var route = action.GetCustomAttribute<HttpGetAttribute>()!;
        var authorization = typeof(GetManagedLanguagesBySessionRequest)
            .GetCustomAttribute<AuthorizeResourceAttribute>()!;

        await Assert.That(route.Template)
            .IsEqualTo("management/by-event/{eventId:guid}/by-session/{eventSessionId:guid}");
        await Assert.That(route.Name).IsEqualTo(RouteNames.GetManagedEventSessionLanguages);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Events.ViewManagement);
    }

    [Test]
    public async Task Update_WhenIfMatchIsMissing_ReturnsValidationProblemDetails()
    {
        using var mediator = new EventSessionLanguageMediatorStub(_ => throw new InvalidOperationException("Mediator should not run when If-Match is missing."));
        await using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            "/api/eventsessionlanguage/7",
            CreateUpdateDto());

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Program validation failed");
        using var problem = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = problem.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("validation_failed");
        await Assert.That(root.GetProperty("errors").TryGetProperty("If-Match", out var ifMatchErrors)).IsTrue();
        await Assert.That(ifMatchErrors.GetArrayLength()).IsEqualTo(1);
        await Assert.That(mediator.LastRequest).IsNull();
    }

    [Test]
    public async Task Update_WhenCommandValidationFails_DoesNotProbeBeforeSecuredCommand()
    {
        using var mediator = new EventSessionLanguageMediatorStub(request => request switch
        {
            UpdateEventSessionLanguageCommand => BaseCommandResponse.Validation<int>(
                ["Language not found."],
                "Event Session Language update failed."),
            _ => throw new InvalidOperationException($"Unexpected request: {request.GetType().Name}")
        });
        await using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        var concurrencyStamp = Guid.NewGuid();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            "/api/eventsessionlanguage/7",
            CreateUpdateDto(),
            ifMatch: concurrencyStamp);

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Program validation failed");

        var command = mediator.LastRequest as UpdateEventSessionLanguageCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.EventSessionLanguageId).IsEqualTo(7);
        await Assert.That(command.EventSessionId).IsEqualTo(Guid.Empty);
        await Assert.That(command.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
    }

    [Test]
    public async Task Update_WhenFailureCodeIsNotFound_ReturnsNotFound()
    {
        using var mediator = new EventSessionLanguageMediatorStub(request => request switch
        {
            UpdateEventSessionLanguageCommand => BaseCommandResponse.NotFound<int>(
                "Event session language not found."),
            _ => throw new InvalidOperationException($"Unexpected request: {request.GetType().Name}")
        });
        await using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            "/api/eventsessionlanguage/7",
            CreateUpdateDto(),
            ifMatch: Guid.NewGuid());

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
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
        TValue body,
        Guid? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        if (ifMatch.HasValue)
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch.Value:D}\"");
        }

        return request;
    }

    private static UpdateEventSessionLanguageDto CreateUpdateDto() => new()
    {
        Language = new UpdateEventSessionLanguageLanguageDto { LanguageId = 2 }
    };

    private sealed class EventSessionLanguageMediatorStub(Func<object, object> responseFactory) : IMediator, IDisposable
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
