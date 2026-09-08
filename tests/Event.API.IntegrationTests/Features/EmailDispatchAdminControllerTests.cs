using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Email)]
[NotInParallel]
public sealed class EmailDispatchAdminControllerTests
{
    [Test]
    public async Task ParkDispatch_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var mediator = new EmailDispatchMediatorStub(_ => Success(Guid.NewGuid()));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();

        var response = await client.PutAsync(
            $"/api/admin/email-dispatch/tenants/{Guid.NewGuid()}/outbox/{Guid.NewGuid()}/park?reason=unsafe",
            content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ProtectedRoutes_WhenAuthorizationProviderDenies_ReturnForbidden()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using var client = factory.CreateClient();
        var requests = new[]
        {
            CreateAuthenticatedRequest(HttpMethod.Get, $"/api/admin/email-dispatch/status?tenantId={tenantId:D}"),
            CreateAuthenticatedRequest(HttpMethod.Put, $"/api/admin/email-dispatch/tenants/{tenantId:D}/pause?reason=maintenance"),
            CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/admin/email-dispatch/tenants/{tenantId:D}/pause"),
            CreateAuthenticatedRequest(HttpMethod.Put, $"/api/admin/email-dispatch/tenants/{tenantId:D}/outbox/{outboxId:D}/park?reason=unsafe"),
            CreateAuthenticatedRequest(HttpMethod.Post, $"/api/admin/email-dispatch/tenants/{tenantId:D}/outbox/{outboxId:D}/replay"),
            CreateAuthenticatedRequest(HttpMethod.Post, $"/api/admin/email-dispatch/tenants/{tenantId:D}/outbox/{outboxId:D}/resolve-without-replay?reason=reviewed")
        };

        foreach (var request in requests)
        {
            using (request)
            {
                var response = await client.SendAsync(request);

                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            }
        }
    }

    [Test]
    public async Task EmailDispatchStatusQueryRequest_WhenLimitIsOutOfRange_IsInvalid()
    {
        var request = new EmailDispatchStatusQueryRequest
        {
            TenantId = Guid.NewGuid(),
            Limit = EmailDispatchStatusQueryRequest.MaxLimit + 1
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EmailDispatchStatusQueryRequest.Limit)))).IsTrue();
    }

    [Test]
    public async Task EmailDispatchParkQueryRequest_WhenReasonIsMissing_IsInvalid()
    {
        var request = new EmailDispatchParkQueryRequest
        {
            Reason = "   "
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EmailDispatchParkQueryRequest.Reason)))).IsTrue();
    }

    [Test]
    public async Task EmailDispatchResolveQueryRequestWhenReasonIsMissingIsInvalid()
    {
        var request = new EmailDispatchResolveQueryRequest { Reason = "   " };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EmailDispatchResolveQueryRequest.Reason)))).IsTrue();
    }

    [Test]
    public async Task EmailDispatchPauseTenantQueryRequest_WhenReasonHasControlCharacter_IsInvalid()
    {
        var request = new EmailDispatchPauseTenantQueryRequest
        {
            Reason = "maintenance\u0001window"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EmailDispatchPauseTenantQueryRequest.Reason)))).IsTrue();
    }

    [Test]
    public async Task GetStatus_WhenLimitIsOutOfRange_ReturnsValidationProblemBeforeDispatch()
    {
        using var mediator = new EmailDispatchMediatorStub(_ => throw new InvalidOperationException("Mediator should not be called."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/admin/email-dispatch/status?tenantId={Guid.NewGuid():D}&limit={EmailDispatchStatusQueryRequest.MaxLimit + 1}");

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");
        await Assert.That(mediator.LastRequest).IsNull();
    }

    [Test]
    public async Task ParkDispatch_WhenReasonIsMissing_ReturnsValidationProblemBeforeDispatch()
    {
        using var mediator = new EmailDispatchMediatorStub(_ => throw new InvalidOperationException("Mediator should not be called."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"/api/admin/email-dispatch/tenants/{Guid.NewGuid():D}/outbox/{Guid.NewGuid():D}/park");

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");
        await Assert.That(mediator.LastRequest).IsNull();
    }

    [Test]
    public async Task PauseTenant_WhenReasonIsTooLong_ReturnsValidationProblemBeforeDispatch()
    {
        var reason = new string('x', EmailDispatchPauseTenantQueryRequest.MaxReasonLength + 1);
        using var mediator = new EmailDispatchMediatorStub(_ => throw new InvalidOperationException("Mediator should not be called."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"/api/admin/email-dispatch/tenants/{Guid.NewGuid():D}/pause?reason={reason}");

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");
        await Assert.That(mediator.LastRequest).IsNull();
    }

    [Test]
    public async Task ParkDispatch_WithAuthentication_DispatchesCommandAndReturnsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        const string reason = "Provider payload needs manual review.";
        using var mediator = new EmailDispatchMediatorStub(_ => Success(outboxId));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"/api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/park?reason={Uri.EscapeDataString(reason)}");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var command = mediator.LastRequest as ParkEmailDispatchCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.TenantId).IsEqualTo(tenantId);
        await Assert.That(command.OutboxId).IsEqualTo(outboxId);
        await Assert.That(command.Reason).IsEqualTo(reason);
    }

    [Test]
    public async Task ReplayDispatch_WhenInvalidTransition_ReturnsConflictProblemDetails()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        using var mediator = new EmailDispatchMediatorStub(_ => Failure(
            "Sent email dispatch rows cannot be replayed.",
            EmailDispatchFailureCodes.InvalidTransition));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/replay");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("status").GetInt32()).IsEqualTo((int)HttpStatusCode.Conflict);
        await Assert.That(root.GetProperty("title").GetString()).IsEqualTo("Email dispatch state transition conflict");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(EmailDispatchFailureCodes.InvalidTransition);
        await Assert.That(root.TryGetProperty("traceId", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("timestamp", out _)).IsTrue();
    }

    [Test]
    public async Task ReplayDispatch_WhenEmailDispatchMisconfigured_ReturnsServiceUnavailableProblemDetails()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        using var mediator = new EmailDispatchMediatorStub(_ => Failure(
            "Email dispatch RabbitMQ parking queue is not configured.",
            EmailDispatchFailureCodes.Misconfigured));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/replay");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("status").GetInt32()).IsEqualTo((int)HttpStatusCode.ServiceUnavailable);
        await Assert.That(root.GetProperty("title").GetString()).IsEqualTo("Email dispatch is misconfigured");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(EmailDispatchFailureCodes.Misconfigured);
        await Assert.That(root.TryGetProperty("traceId", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("timestamp", out _)).IsTrue();
    }

    [Test]
    public async Task PauseTenant_WhenValidationFails_ReturnsValidationProblemDetails()
    {
        var tenantId = Guid.NewGuid();
        using var mediator = new EmailDispatchMediatorStub(_ => Failure(
            "Pause reason is required.",
            EmailDispatchFailureCodes.ValidationFailed));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"/api/admin/email-dispatch/tenants/{tenantId}/pause");

        var response = await client.SendAsync(request);

        using var document = await AssertEmailDispatchValidationProblemAsync(response);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
    }

    [Test]
    public async Task ResumeTenant_WhenValidationFails_ReturnsValidationProblemDetails()
    {
        var tenantId = Guid.NewGuid();
        using var mediator = new EmailDispatchMediatorStub(_ => Failure(
            "Tenant dispatch state cannot be changed.",
            EmailDispatchFailureCodes.ValidationFailed));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/admin/email-dispatch/tenants/{tenantId}/pause");

        var response = await client.SendAsync(request);

        using var document = await AssertEmailDispatchValidationProblemAsync(response);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
    }

    [Test]
    public async Task ReplayAndParkRoutes_UseStableRouteNamesAndWritePolicy()
    {
        MethodInfo park = typeof(EmailDispatchAdminController).GetMethod(nameof(EmailDispatchAdminController.ParkDispatch))!;
        MethodInfo replay = typeof(EmailDispatchAdminController).GetMethod(nameof(EmailDispatchAdminController.ReplayDispatch))!;
        MethodInfo resolve = typeof(EmailDispatchAdminController).GetMethod(nameof(EmailDispatchAdminController.ResolveWithoutReplay))!;

        var parkRoute = park.GetCustomAttribute<HttpPutAttribute>();
        var replayRoute = replay.GetCustomAttribute<HttpPostAttribute>();
        var resolveRoute = resolve.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(parkRoute).IsNotNull();
        await Assert.That(parkRoute!.Name).IsEqualTo(RouteNames.ParkEmailDispatch);
        await Assert.That(parkRoute.Template).IsEqualTo("tenants/{tenantId:guid}/outbox/{outboxId:guid}/park");
        await Assert.That(replayRoute).IsNotNull();
        await Assert.That(replayRoute!.Name).IsEqualTo(RouteNames.ReplayEmailDispatch);
        await Assert.That(replayRoute.Template).IsEqualTo("tenants/{tenantId:guid}/outbox/{outboxId:guid}/replay");
        await Assert.That(resolveRoute).IsNotNull();
        await Assert.That(resolveRoute!.Name).IsEqualTo(RouteNames.ResolveEmailDispatchWithoutReplay);
        await Assert.That(resolveRoute.Template).IsEqualTo("tenants/{tenantId:guid}/outbox/{outboxId:guid}/resolve-without-replay");

        await Assert.That(GetRateLimitPolicy(park)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(GetRateLimitPolicy(replay)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(GetRateLimitPolicy(resolve)).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await AssertProducesProblem(park, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(park, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(replay, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(replay, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(resolve, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(resolve, StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task GetStatusRoute_ReturnsHalCollectionResource()
    {
        MethodInfo getStatus = typeof(EmailDispatchAdminController).GetMethod(nameof(EmailDispatchAdminController.GetStatus))!;

        var route = getStatus.GetCustomAttribute<HttpGetAttribute>();
        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Name).IsEqualTo(RouteNames.GetEmailDispatchStatus);
        await Assert.That(route.Template).IsEqualTo("status");
        await Assert.That(getStatus.ReturnType).IsEqualTo(typeof(Task<ActionResult<HalCollectionResource<EmailDispatchStatusDto>>>));
        await Assert.That(GetRateLimitPolicy(getStatus)).IsEqualTo(RateLimitingExtensions.AuthenticatedPolicy);
        await AssertProducesProblem(getStatus, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(getStatus, StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task TenantControlRoutes_AdvertiseAuthenticationAndAuthorizationProblemDetails()
    {
        MethodInfo pause = typeof(EmailDispatchAdminController).GetMethod(nameof(EmailDispatchAdminController.PauseTenant))!;
        MethodInfo resume = typeof(EmailDispatchAdminController).GetMethod(nameof(EmailDispatchAdminController.ResumeTenant))!;

        await AssertProducesProblem(pause, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(pause, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(resume, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(resume, StatusCodes.Status403Forbidden);
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

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/problem+json"));
        return request;
    }

    private static string? GetRateLimitPolicy(MethodInfo method)
        => method.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName;

    private static async Task AssertProducesProblem(MethodInfo method, int statusCode)
    {
        var attributes = method.GetCustomAttributes<ProducesResponseTypeAttribute>();

        await Assert.That(attributes.Any(attribute =>
            attribute.StatusCode == statusCode &&
            attribute.Type == typeof(ProblemDetails))).IsTrue();
    }

    private static List<ValidationResult> Validate(IValidatableObject request)
        => request.Validate(new ValidationContext(request)).ToList();

    private static bool HasMember(ValidationResult result, string memberName)
        => result.MemberNames.Contains(memberName, StringComparer.Ordinal);

    private static async Task<JsonDocument> AssertEmailDispatchValidationProblemAsync(HttpResponseMessage response)
    {
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Email dispatch validation failed");

        return await ProblemDetailsAssertions.ReadAsJsonAsync(response);
    }

    private static BaseCommandResponse<Guid> Success(Guid id) =>
        BaseCommandResponse.Success(id, "Email dispatch operation completed.");

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message, [message]);

    private sealed class EmailDispatchMediatorStub(Func<object, object> responseFactory) : IMediator, IDisposable
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

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
