using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.ExceptionHandling;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
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
        await using var factory = new AuthenticatedWebApplicationFactory();
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
        await using var factory = CreateIngressFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/admin/email-dispatch/status?tenantId={Guid.NewGuid():D}&limit={EmailDispatchStatusQueryRequest.MaxLimit + 1}");

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");
    }

    [Test]
    public async Task ParkDispatch_WhenReasonIsMissing_ReturnsValidationProblemBeforeDispatch()
    {
        await using var factory = CreateIngressFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"/api/admin/email-dispatch/tenants/{Guid.NewGuid():D}/outbox/{Guid.NewGuid():D}/park");

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");
    }

    [Test]
    public async Task PauseTenant_WhenReasonIsTooLong_ReturnsValidationProblemBeforeDispatch()
    {
        var reason = new string('x', EmailDispatchPauseTenantQueryRequest.MaxReasonLength + 1);
        await using var factory = CreateIngressFactory();
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Put,
            $"/api/admin/email-dispatch/tenants/{Guid.NewGuid():D}/pause?reason={reason}");

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");
    }

    [Test]
    public async Task ParkDispatch_WithAuthentication_DispatchesCommandAndReturnsSuccess()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        var tenantId = PlatformDefaults.DefaultTenantId;
        var outboxId = await factory.SeedDispatchAsync(EmailDispatchStatus.Pending);
        const string reason = "Provider payload needs manual review.";
        using var client = factory.Client(factory.TenantAdminId);
        var response = await client.PutAsync(
            $"/api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/park?reason={Uri.EscapeDataString(reason)}", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>()
            .GetByTenantAndId(tenantId, outboxId, default);
        await Assert.That(row!.Status).IsEqualTo(EmailDispatchStatus.Parked);
        await Assert.That(row.ParkReason).IsEqualTo(EmailDispatchParkReason.Operator);
        await Assert.That(row.TenantId).IsEqualTo(tenantId);
        await Assert.That(row.Id).IsEqualTo(outboxId);
        await Assert.That(row.LastError).IsEqualTo(reason);
        await Assert.That(row.UpdatedBy).IsEqualTo(factory.TenantAdminId);
    }

    [Test]
    public async Task ReplayDispatch_WhenInvalidTransition_ReturnsConflictProblemDetails()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        var outboxId = await factory.SeedDispatchAsync(EmailDispatchStatus.Sent);
        using var client = factory.Client();
        var response = await client.PostAsync(
            $"/api/admin/email-dispatch/tenants/{PlatformDefaults.DefaultTenantId}/outbox/{outboxId}/replay", null);

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
    public async Task EmailDispatchMisconfiguredMappingPreservesServiceUnavailableProblemDetails()
    {
        // Replay only resets durable state; misconfiguration belongs to the shared failure mapper,
        // not to a fabricated replay-handler response.
        var controller = new ProblemController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var result = (ObjectResult)controller.ToEmailDispatchProblem(Failure(
            "Email dispatch RabbitMQ parking queue is not configured.", EmailDispatchFailureCodes.Misconfigured));
        var problem = (ProblemDetails)result.Value!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");
        await Assert.That(problem.Status).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(problem.Title).IsEqualTo("Email dispatch is misconfigured");
        await Assert.That(problem.Extensions["code"]).IsEqualTo(EmailDispatchFailureCodes.Misconfigured);
        await Assert.That(problem.Extensions.ContainsKey("traceId")).IsTrue();
        await Assert.That(problem.Extensions.ContainsKey("timestamp")).IsTrue();
    }

    [Test]
    public async Task PauseTenant_WhenValidationFails_ReturnsValidationProblemDetails()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        using var client = factory.Client();
        var response = await client.PutAsync(
            $"/api/admin/email-dispatch/tenants/{Guid.Empty}/pause?reason=maintenance", null);

        using var document = await AssertEmailDispatchValidationProblemAsync(response);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
    }

    [Test]
    public async Task ResumeTenant_WhenValidationFails_ReturnsValidationProblemDetails()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        using var client = factory.Client();
        var response = await client.DeleteAsync($"/api/admin/email-dispatch/tenants/{Guid.Empty}/pause");

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

    private static AuthenticatedWebApplicationFactory CreateIngressFactory() => new()
    {
        // Reaching protected native dispatch would produce a 500, not the expected ingress 400.
        AuthorizationProviderOverride = new StubAuthorizationProvider
        {
            CheckPredicate = _ => throw new InvalidOperationException("Invalid ingress reached operation authorization.")
        }
    };

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

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode) =>
        BaseCommandResponse.Failure<Guid>(failureCode, message, [message]);

    private sealed class ProblemController : ControllerBase;

}
