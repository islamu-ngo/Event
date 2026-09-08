using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class CustomPropertyProjectionAdminControllerTests
{
    [Test]
    public async Task GetEventProjections_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var factory = CreateFactoryWithMediator(new ExposureFilteringMediator());
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/admin/custom-property-projections/events/{Guid.NewGuid()}?exposureCeiling=Public");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetEventProjections_WithPublicCeiling_ExcludesInternalRowsAndMetadata()
    {
        var eventId = Guid.NewGuid();
        using var factory = CreateFactoryWithMediator(new ExposureFilteringMediator(eventId: eventId));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            $"/api/admin/custom-property-projections/events/{eventId}?exposureCeiling=Public");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("internal_notes");
        await Assert.That(body).DoesNotContain("classified-internal");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("success").GetBoolean()).IsTrue();

        var rows = root.GetProperty("id").EnumerateArray().ToArray();
        await Assert.That(rows.Length).IsEqualTo(1);
        await Assert.That(rows[0].GetProperty("key").GetString()).IsEqualTo("public_note");
        await Assert.That(rows[0].GetProperty("normalizedValue").GetString()).IsEqualTo("safe-public");
    }

    [Test]
    public async Task GetSessionProjections_WithPublicCeiling_ExcludesInternalRowsAndMetadata()
    {
        var eventSessionId = Guid.NewGuid();
        using var factory = CreateFactoryWithMediator(new ExposureFilteringMediator(eventSessionId: eventSessionId));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            $"/api/admin/custom-property-projections/sessions/{eventSessionId}?exposureCeiling=Public");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain("internal_session_notes");
        await Assert.That(body).DoesNotContain("session-classified-internal");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("success").GetBoolean()).IsTrue();

        var rows = root.GetProperty("id").EnumerateArray().ToArray();
        await Assert.That(rows.Length).IsEqualTo(1);
        await Assert.That(rows[0].GetProperty("key").GetString()).IsEqualTo("public_session_note");
        await Assert.That(rows[0].GetProperty("normalizedValue").GetString()).IsEqualTo("session-safe-public");
    }

    [Test]
    public async Task GetProjectionStatus_WhenMediatorReportsFailure_ReturnsValidationProblemDetails()
    {
        const string failureMessage = "Tenant projection status cannot be loaded.";
        var tenantId = Guid.NewGuid();
        using var factory = CreateFactoryWithMediator(new ExposureFilteringMediator(
            projectionStatusFailure: ProjectionStatusFailure(failureMessage)));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest($"/api/admin/custom-property-projections/status?tenantId={tenantId}");

        var response = await client.SendAsync(request);

        using var problem = await AssertProjectionValidationProblemAsync(response, failureMessage, "Custom-property projection validation failed", "customPropertyProjection");
        await Assert.That(problem.RootElement.GetProperty("detail").GetString()).IsEqualTo(failureMessage);
    }

    [Test]
    public async Task GetSessionProjectionStatus_WhenMediatorReportsFailure_ReturnsValidationProblemDetails()
    {
        const string failureMessage = "Session projection status cannot be loaded.";
        var tenantId = Guid.NewGuid();
        using var factory = CreateFactoryWithMediator(new ExposureFilteringMediator(
            sessionProjectionStatusFailure: ProjectionStatusFailure(failureMessage)));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest($"/api/admin/custom-property-projections/sessions/status?tenantId={tenantId}");

        var response = await client.SendAsync(request);

        using var problem = await AssertProjectionValidationProblemAsync(response, failureMessage, "Event session custom-property projection validation failed", "eventSessionCustomPropertyProjection");
        await Assert.That(problem.RootElement.GetProperty("detail").GetString()).IsEqualTo(failureMessage);
    }

    private static WebApplicationFactory<Program> CreateFactoryWithMediator(IMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private static BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>> ProjectionStatusFailure(string message)
        => BaseCommandResponse.Failure<IReadOnlyList<ProjectionStatusDto>>(
            "custom_property_projection_validation_failed",
            message,
            [message],
            []);

    private static async Task<JsonDocument> AssertProjectionValidationProblemAsync(
        HttpResponseMessage response,
        string expectedError,
        string expectedTitle,
        string expectedErrorKey)
    {
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            expectedTitle);

        var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);

        var root = document.RootElement;
        await Assert.That(root.GetProperty("code").GetString()).IsEqualTo("custom_property_projection_validation_failed");
        var errors = root.GetProperty("errors").GetProperty(expectedErrorKey).EnumerateArray().ToArray();
        await Assert.That(errors.Length).IsEqualTo(1);
        await Assert.That(errors[0].GetString()).IsEqualTo(expectedError);

        return document;
    }

    private sealed class ExposureFilteringMediator(
        Guid? eventId = null,
        Guid? eventSessionId = null,
        BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>? projectionStatusFailure = null,
        BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>>? sessionProjectionStatusFailure = null) : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                GetEventCustomPropertyProjectionStatusQuery => projectionStatusFailure ?? ProjectionStatusSuccess(),
                GetEventCustomPropertyProjectionsForEventQuery query => CreateEventResponse(query),
                GetEventSessionCustomPropertyProjectionStatusQuery => sessionProjectionStatusFailure ?? ProjectionStatusSuccess(),
                GetEventSessionCustomPropertyProjectionsForSessionQuery query => CreateSessionResponse(query),
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
                GetEventCustomPropertyProjectionStatusQuery => Task.FromResult<object?>(projectionStatusFailure ?? ProjectionStatusSuccess()),
                GetEventCustomPropertyProjectionsForEventQuery query => Task.FromResult<object?>(CreateEventResponse(query)),
                GetEventSessionCustomPropertyProjectionStatusQuery => Task.FromResult<object?>(sessionProjectionStatusFailure ?? ProjectionStatusSuccess()),
                GetEventSessionCustomPropertyProjectionsForSessionQuery query => Task.FromResult<object?>(CreateSessionResponse(query)),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>> CreateEventResponse(
            GetEventCustomPropertyProjectionsForEventQuery query)
        {
            var rows = new[]
            {
                new EventCustomPropertyProjectionDto
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId ?? query.EventId,
                    TenantId = Guid.NewGuid(),
                    EventCustomPropertyDefinitionId = Guid.NewGuid(),
                    EventCustomPropertyValueId = Guid.NewGuid(),
                    Namespace = "tenant.community",
                    Key = "public_note",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public,
                    IsSearchable = true,
                    IsFilterable = true,
                    IsExportable = true,
                    IsModerationRelevant = true,
                    NormalizedValue = "safe-public",
                    TextValue = "Safe public"
                },
                new EventCustomPropertyProjectionDto
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId ?? query.EventId,
                    TenantId = Guid.NewGuid(),
                    EventCustomPropertyDefinitionId = Guid.NewGuid(),
                    EventCustomPropertyValueId = Guid.NewGuid(),
                    Namespace = "tenant.community",
                    Key = "internal_notes",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Internal,
                    IsSearchable = true,
                    IsFilterable = true,
                    IsExportable = true,
                    IsModerationRelevant = true,
                    NormalizedValue = "classified-internal",
                    TextValue = "Classified internal"
                }
            };

            return BaseCommandResponse.Success<IReadOnlyList<EventCustomPropertyProjectionDto>>(
                ApplyCeiling(rows, query.ExposureCeiling),
                "Projection rows loaded.");
        }

        private BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>> CreateSessionResponse(
            GetEventSessionCustomPropertyProjectionsForSessionQuery query)
        {
            var rows = new[]
            {
                new EventSessionCustomPropertyProjectionDto
                {
                    Id = Guid.NewGuid(),
                    EventSessionId = eventSessionId ?? query.EventSessionId,
                    TenantId = Guid.NewGuid(),
                    EventSessionCustomPropertyDefinitionId = Guid.NewGuid(),
                    EventSessionCustomPropertyValueId = Guid.NewGuid(),
                    Namespace = "tenant.community",
                    Key = "public_session_note",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Public,
                    IsSearchable = true,
                    IsFilterable = true,
                    IsExportable = true,
                    IsModerationRelevant = true,
                    NormalizedValue = "session-safe-public",
                    TextValue = "Session safe public"
                },
                new EventSessionCustomPropertyProjectionDto
                {
                    Id = Guid.NewGuid(),
                    EventSessionId = eventSessionId ?? query.EventSessionId,
                    TenantId = Guid.NewGuid(),
                    EventSessionCustomPropertyDefinitionId = Guid.NewGuid(),
                    EventSessionCustomPropertyValueId = Guid.NewGuid(),
                    Namespace = "tenant.community",
                    Key = "internal_session_notes",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.Internal,
                    IsSearchable = true,
                    IsFilterable = true,
                    IsExportable = true,
                    IsModerationRelevant = true,
                    NormalizedValue = "session-classified-internal",
                    TextValue = "Session classified internal"
                }
            };

            return BaseCommandResponse.Success<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>(
                ApplyCeiling(rows, query.ExposureCeiling),
                "Projection rows loaded.");
        }

        private static BaseCommandResponse<IReadOnlyList<ProjectionStatusDto>> ProjectionStatusSuccess()
            => BaseCommandResponse.Success<IReadOnlyList<ProjectionStatusDto>>(
                [],
                "Projection status loaded.");

        private static IReadOnlyList<TProjection> ApplyCeiling<TProjection>(
            IEnumerable<TProjection> rows,
            ExposureLevel? exposureCeiling)
            where TProjection : class
        {
            if (exposureCeiling is null)
            {
                return rows.ToArray();
            }

            var visibleLevels = VisibleAtOrBelow(exposureCeiling.Value);

            return rows.Where(row => visibleLevels.Contains(GetExposureLevel(row))).ToArray();
        }

        private static ExposureLevel[] VisibleAtOrBelow(ExposureLevel ceiling) =>
            ceiling switch
            {
                ExposureLevel.Public => [ExposureLevel.Public],
                ExposureLevel.TenantAdminOnly => [ExposureLevel.Public, ExposureLevel.TenantAdminOnly],
                ExposureLevel.OrganizerOnly => [ExposureLevel.Public, ExposureLevel.TenantAdminOnly, ExposureLevel.OrganizerOnly],
                ExposureLevel.Internal => [ExposureLevel.Public, ExposureLevel.TenantAdminOnly, ExposureLevel.OrganizerOnly, ExposureLevel.Internal],
                _ => [ExposureLevel.Public]
            };

        private static ExposureLevel GetExposureLevel<TProjection>(TProjection row)
            where TProjection : class
            => row switch
            {
                EventCustomPropertyProjectionDto eventProjection => eventProjection.ExposureLevel,
                EventSessionCustomPropertyProjectionDto sessionProjection => sessionProjection.ExposureLevel,
                _ => throw new InvalidOperationException($"Unsupported projection DTO type {row.GetType().Name}.")
            };
    }
}
