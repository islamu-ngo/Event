using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class ProblemDetailsContractTests
{
    private readonly ApiTestFixture _fixture;

    public ProblemDetailsContractTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GlobalExceptionHandler_OnNotFoundException_ReturnsProblemDetailsWithTraceIdAndTimestamp()
    {
        using var client = CreateClientThatThrows(new NotFoundException("Event", Guid.NewGuid()));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, "Resource not found");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        // Verify traceId and timestamp extensions are present
        await Assert.That(root.TryGetProperty("traceId", out var traceId)).IsTrue();
        await Assert.That(traceId.GetString()).IsNotNull();
        await Assert.That(traceId.GetString()!.Length > 0).IsTrue();

        await Assert.That(root.TryGetProperty("timestamp", out var timestamp)).IsTrue();
        await Assert.That(timestamp.GetString()).IsNotNull();

        // Verify RFC 7807 required fields
        await Assert.That(root.TryGetProperty("status", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("title", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("detail", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("type", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("instance", out _)).IsTrue();
    }

    [Test]
    public async Task GlobalExceptionHandler_OnBadRequestException_Returns400ProblemDetails()
    {
        using var client = CreateClientThatThrows(new BadRequestException("Invalid input data"));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Bad request");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var detail = document.RootElement.GetProperty("detail").GetString();
        await Assert.That(detail).IsEqualTo("Invalid input data");
    }

    [Test]
    public async Task AuthorizationMiddleware_WhenAuthenticationIsMissing_ReturnsProblemDetails401()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreatePurgeCustomPropertyRequest(Guid.NewGuid());

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized, "Authentication required");
        await AssertProblemCodeAndTypeAsync(
            response,
            "authentication_required",
            "https://tools.ietf.org/html/rfc9110#section-15.5.2");
    }

    [Test]
    public async Task AuthorizationMiddleware_WhenRoleRequirementFails_ReturnsProblemDetails403()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = CreatePurgeCustomPropertyRequest(Guid.NewGuid());
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Forbidden, "Forbidden");
        await AssertProblemCodeAndTypeAsync(
            response,
            "forbidden",
            "https://tools.ietf.org/html/rfc9110#section-15.5.4");
    }

    [Test]
    public async Task RateLimiter_WhenGlobalLimitIsExceeded_ReturnsProblemDetails429()
    {
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        const string keyId = "problem-details-rate-limit";
        const string secret = "problem-details-rate-limit-secret";
        var apiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        await using var factory = new ExternalApiPhase0WebApplicationFactory
        {
            DisableRateLimitingInTesting = false,
            GlobalRateLimitTokenLimit = 1,
            GlobalRateLimitTokensPerPeriod = 1,
            GlobalRateLimitReplenishPeriodSeconds = 60,
            PersistedApiKeys =
            [
                new ExternalApiPhase0WebApplicationFactory.PersistedApiKeySeed
                {
                    KeyId = keyId,
                    Secret = secret,
                    TenantId = tenantId,
                    OwnerId = ownerId,
                    OwnerType = ExternalApiKeyOwnerType.User,
                    Scopes = ["events:read"]
                }
            ]
        };

        using var client = factory.CreateClient();
        using var firstRequest = CreateApiKeyProbeRequest(apiKey);
        using var firstResponse = await client.SendAsync(firstRequest);
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var throttledRequest = CreateApiKeyProbeRequest(apiKey);
        using var response = await client.SendAsync(throttledRequest);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.TooManyRequests, "Too Many Requests");
        await AssertProblemCodeAndTypeAsync(
            response,
            "rate_limited",
            "https://tools.ietf.org/html/rfc6585#section-4");
        await Assert.That(response.Headers.Contains("Retry-After")).IsTrue();
        await Assert.That(response.Headers.TryGetValues("X-RateLimit-Limit", out var limits)).IsTrue();
        await Assert.That(limits).Contains("1");
        await Assert.That(response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)).IsTrue();
        await Assert.That(remaining).Contains("0");
    }

    [Test]
    public async Task ApiControllerValidation_WhenBodyIsMalformed_ReturnsSafeProblemDetails()
    {
        using var content = new StringContent("{ \"eventType\": ", Encoding.UTF8, "application/json");

        var response = await _fixture.Client.PostAsync("/api/a/t", content);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo("One or more validation errors occurred.");
        await Assert.That(root.TryGetProperty("errors", out var errors)).IsTrue();
        await Assert.That(errors.TryGetProperty("body", out var bodyErrors)).IsTrue();
        await Assert.That(bodyErrors[0].GetString()).IsEqualTo("Request body is invalid or contains unsupported fields.");

        var raw = root.GetRawText();
        await Assert.That(raw).DoesNotContain("\"eventType\"");
    }

    [Test]
    public async Task ApiControllerValidation_WhenBodyIsMissing_ReturnsBodyLevelProblemDetails()
    {
        using var content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        var response = await _fixture.Client.PostAsync("/api/a/t", content);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var errors = document.RootElement.GetProperty("errors");

        await Assert.That(errors.TryGetProperty("body", out _)).IsTrue();
    }

    [Test]
    public async Task ApiControllerValidation_WhenContentTypeIsUnsupported_ReturnsProblemDetails415()
    {
        using var content = new StringContent("{\"eventType\":\"pageview\"}", Encoding.UTF8, "text/plain");

        var response = await _fixture.Client.PostAsync("/api/a/t", content);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.UnsupportedMediaType, "Unsupported media type");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("detail").GetString())
            .IsEqualTo("The request content type is not supported for this endpoint.");
    }

    [Test]
    public async Task ApiControllerValidation_WhenBodyHasUnknownProperty_ReturnsProblemDetailsWithoutBinding()
    {
        using var content = new StringContent(
            "{\"eventType\":\"pageview\",\"unknownField\":\"not allowed\"}",
            Encoding.UTF8,
            "application/json");

        var response = await _fixture.Client.PostAsync("/api/a/t", content);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;
        var errors = root.GetProperty("errors");

        await Assert.That(errors.TryGetProperty("body", out var bodyErrors)).IsTrue();
        await Assert.That(bodyErrors[0].GetString()).IsEqualTo("Request body is invalid or contains unsupported fields.");
        await Assert.That(root.GetRawText()).DoesNotContain("not allowed");
    }

    [Test]
    public async Task ApiControllerValidation_WhenBodyOverPostsHalLinks_ReturnsProblemDetails()
    {
        using var content = new StringContent(
            "{\"eventType\":\"pageview\",\"_links\":{\"self\":{\"href\":\"/admin\"}}}",
            Encoding.UTF8,
            "application/json");

        var response = await _fixture.Client.PostAsync("/api/a/t", content);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var errors = document.RootElement.GetProperty("errors");

        await Assert.That(errors.TryGetProperty("body", out _)).IsTrue();
    }

    [Test]
    public async Task GlobalExceptionHandler_OnQuotaExceededException_Returns422ProblemDetailsWithStableExtensions()
    {
        var tenantId = Guid.NewGuid();
        using var client = CreateClientThatThrows(
            new QuotaExceededException(
                "Custom-property definition quota exceeded.",
                CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key,
                limit: 3,
                actual: 3,
                attempted: 4,
                scope: "event_custom_property_definitions",
                tenantId: tenantId));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.UnprocessableEntity, "Quota exceeded");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("/problems/quota_exceeded");
        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo("Custom-property definition quota exceeded.");
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("quota_exceeded");
        await Assert.That(root.GetProperty("quotaKey").GetString()).IsEqualTo(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key);
        await Assert.That(root.GetProperty("limit").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("actual").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("attempted").GetInt32()).IsEqualTo(4);
        await Assert.That(root.GetProperty("scope").GetString()).IsEqualTo("event_custom_property_definitions");
        await Assert.That(root.TryGetProperty("tenantId", out _)).IsFalse();
    }

    [Test]
    public async Task GlobalExceptionHandler_OnConcurrentUpdate_Returns409ProblemDetailsWithStableExtensions()
    {
        var definitionId = Guid.NewGuid();
        const string detail = "The custom-property definition changed since it was loaded. Reload and try again.";

        using var client = CreateClientThatThrows(
            new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                detail,
                "custom_property_definition",
                definitionId.ToString()));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Conflict, "Concurrency conflict");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("/problems/concurrent_update");
        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo(detail);
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(root.GetProperty("entityType").GetString()).IsEqualTo("custom_property_definition");
        await Assert.That(root.GetProperty("entityId").GetString()).IsEqualTo(definitionId.ToString());
    }

    [Test]
    public async Task CommandResponseQuotaMapper_Returns422ProblemDetailsWithStableExtensions()
    {
        var tenantId = Guid.NewGuid();
        var response = BaseCommandResponse.Quota<Guid>(
            "Custom-property option quota exceeded.",
            new QuotaExceededDetails(
                CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key,
                Limit: 2,
                Actual: null,
                Attempted: 3,
                Scope: "event_custom_property_options",
                TenantId: tenantId));

        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.Path = "/api/events/test/custom-properties";

        var mapperType = Type.GetType("Explore.API.ExceptionHandling.QuotaProblemDetailsFactory, Explore.API", throwOnError: true)!;
        var method = mapperType.GetMethods()
            .Single(candidate => candidate.Name == "ToQuotaProblemOrBadRequest" && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(Guid));

        var actionResult = (ActionResult)method.Invoke(null, [controller, response])!;
        var objectResult = actionResult as ObjectResult;

        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status422UnprocessableEntity);

        var problemDetails = objectResult.Value as ProblemDetails;
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Status).IsEqualTo(StatusCodes.Status422UnprocessableEntity);
        await Assert.That(problemDetails.Title).IsEqualTo("Quota exceeded");
        await Assert.That(problemDetails.Type).IsEqualTo("/problems/quota_exceeded");
        await Assert.That(problemDetails.Detail).IsEqualTo("Custom-property option quota exceeded.");
        await Assert.That(problemDetails.Instance).IsEqualTo("/api/events/test/custom-properties");
        await Assert.That(problemDetails.Extensions["code"]).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(problemDetails.Extensions["quotaKey"]).IsEqualTo(CustomPropertyQuotaSettingDefinitions.MaxOptionsPerDefinition.Key);
        await Assert.That(problemDetails.Extensions["limit"]).IsEqualTo(2);
        await Assert.That(problemDetails.Extensions.ContainsKey("actual")).IsFalse();
        await Assert.That(problemDetails.Extensions["attempted"]).IsEqualTo(3);
        await Assert.That(problemDetails.Extensions["scope"]).IsEqualTo("event_custom_property_options");
        await Assert.That(problemDetails.Extensions.ContainsKey("tenantId")).IsFalse();
    }

    [Test]
    public async Task QuotaExceededDetails_SerializesWithoutTenantIdButKeepsOtherQuotaFields()
    {
        var tenantId = Guid.NewGuid();
        var details = new QuotaExceededDetails(
            "event.custom_properties.max_options_per_definition",
            Limit: 2,
            Actual: 4,
            Attempted: 5,
            Scope: "event_custom_property_options",
            TenantId: tenantId);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(details, JsonSerializerOptions.Web));
        JsonElement root = document.RootElement;

        await Assert.That(details.TenantId).IsEqualTo(tenantId);
        await Assert.That(root.GetProperty("quotaKey").GetString()).IsEqualTo("event.custom_properties.max_options_per_definition");
        await Assert.That(root.GetProperty("limit").GetInt32()).IsEqualTo(2);
        await Assert.That(root.GetProperty("actual").GetInt32()).IsEqualTo(4);
        await Assert.That(root.GetProperty("attempted").GetInt32()).IsEqualTo(5);
        await Assert.That(root.GetProperty("scope").GetString()).IsEqualTo("event_custom_property_options");
        await Assert.That(root.TryGetProperty("tenantId", out _)).IsFalse();
    }

    [Test]
    public async Task GlobalExceptionHandler_OnConcurrentUpdateException_Returns409ProblemDetailsWithStableExtensions()
    {
        var entityId = Guid.NewGuid();
        const string detail = "The custom-property definition changed since it was loaded. Reload and try again.";
        using var client = CreateClientThatThrows(
            new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                detail,
                "custom_property_definition",
                entityId.ToString()));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Conflict, "Concurrency conflict");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("/problems/concurrent_update");
        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo(detail);
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(root.GetProperty("entityType").GetString()).IsEqualTo("custom_property_definition");
        await Assert.That(root.GetProperty("entityId").GetString()).IsEqualTo(entityId.ToString());
    }

    [Test]
    public async Task GlobalExceptionHandler_OnStaleSyncBase_Returns409ProblemDetailsWithStableExtensions()
    {
        var eventId = Guid.NewGuid();
        const string detail = "The template sync base is stale. Recompute the diff and try again.";

        using var client = CreateClientThatThrows(
            new ConcurrencyConflictException(
                ConcurrencyConflictException.StaleSyncBase,
                detail,
                nameof(Explore.Domain.Event),
                eventId.ToString()));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.Conflict, "Concurrency conflict");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("/problems/stale_sync_base");
        await Assert.That(root.GetProperty("detail").GetString()).IsEqualTo(detail);
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(ConcurrencyConflictException.StaleSyncBase);
        await Assert.That(root.GetProperty("entityType").GetString()).IsEqualTo(nameof(Explore.Domain.Event));
        await Assert.That(root.GetProperty("entityId").GetString()).IsEqualTo(eventId.ToString());
    }

    [Test]
    public async Task GlobalExceptionHandler_OnUnhandledException_SanitizesInternalDetails()
    {
        const string sensitiveMessage = "SQL connection to prod-db-01.internal failed";

        using var client = CreateClientThatThrows(new InvalidOperationException(sensitiveMessage));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError, "Internal server error");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        var detail = root.GetProperty("detail").GetString() ?? string.Empty;
        await Assert.That(detail).DoesNotContain(sensitiveMessage);
        await Assert.That(root.TryGetProperty("stackTrace", out _)).IsFalse();
    }

    [Test]
    public async Task ValidationExceptionHandler_ReturnsErrorsKeyedByProperty()
    {
        // Use FluentValidation.ValidationException (not Application.Exceptions.ValidationException)
        // because the handler keys errors by property only for FluentValidation exceptions.
        var failures = new List<FluentValidation.Results.ValidationFailure>
        {
            new("Name", "Name is required."),
            new("Name", "Name must be at most 100 characters."),
            new("Email", "Email is not valid.")
        };

        using var client = CreateClientThatThrows(
            new FluentValidation.ValidationException(failures));

        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("errors", out var errors)).IsTrue();

        // FluentValidation errors are grouped by lowercase property name
        await Assert.That(errors.TryGetProperty("name", out var nameErrors)).IsTrue();
        await Assert.That(nameErrors.GetArrayLength()).IsEqualTo(2);

        await Assert.That(errors.TryGetProperty("email", out var emailErrors)).IsTrue();
        await Assert.That(emailErrors.GetArrayLength()).IsEqualTo(1);
    }

    [Test]
    public async Task ProblemDetails_ContentType_IsApplicationProblemJsonOrApplicationJson()
    {
        using var client = CreateClientThatThrows(new NotFoundException("Resource", Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/actor/{Guid.NewGuid()}");
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isValid = contentType is "application/problem+json" or "application/json";
        await Assert.That(isValid).IsTrue();
    }

    [Test]
    public async Task ProblemDetails_InstanceField_ContainsRequestPath()
    {
        var eventId = Guid.NewGuid();
        using var client = CreateClientThatThrows(new NotFoundException("Event", eventId));

        var response = await client.GetAsync($"/api/actor/{eventId}");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("instance", out var instance)).IsTrue();
        var instanceValue = instance.GetString() ?? string.Empty;
        await Assert.That(instanceValue).Contains("/api/actor/");
    }

    [Test]
    public async Task ProblemDetails_WhenCorrelationHeaderProvided_IncludesCorrelationIdExtension()
    {
        const string correlationId = "phase-0b-problem-details-correlation";
        using var client = CreateClientThatThrows(new NotFoundException("Resource", Guid.NewGuid()));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/actor/{Guid.NewGuid()}");
        request.Headers.Add("X-Correlation-ID", correlationId);

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, "Resource not found");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("correlationId", out var correlationIdExtension)).IsTrue();
        await Assert.That(correlationIdExtension.GetString()).IsEqualTo(correlationId);
        await Assert.That(response.Headers.TryGetValues("X-Correlation-ID", out var responseCorrelationIds)).IsTrue();
        await Assert.That(responseCorrelationIds).Contains(correlationId);
    }

    private HttpClient CreateClientThatThrows(Exception exception)
    {
        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton<IMediator>(new ThrowingMediator(exception));
            });
        });

        return app.CreateClient();
    }

    private static HttpRequestMessage CreatePurgeCustomPropertyRequest(Guid id)
        => new(HttpMethod.Delete, $"/api/CustomPropertyDefinition/{id}/purge")
        {
            Content = JsonContent.Create(new { Reason = "problem-details-contract" })
        };

    private static HttpRequestMessage CreateApiKeyProbeRequest(string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/_internal/auth-probe/secure");
        request.Headers.Add("X-API-Key", apiKey);
        return request;
    }

    private static async Task AssertProblemCodeAndTypeAsync(
        HttpResponseMessage response,
        string expectedCode,
        string expectedType)
    {
        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo(expectedCode);
        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo(expectedType);
        await Assert.That(root.TryGetProperty("instance", out var instance)).IsTrue();
        await Assert.That(instance.GetString()).IsNotNull();
    }

    private sealed class TestController : ControllerBase;

    private sealed class ThrowingMediator(Exception exception) : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw exception;

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw exception;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw exception;

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw exception;
    }
}
