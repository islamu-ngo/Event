using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Actor;
using Explore.Application.Exceptions;
using Explore.Application.Features.Actors.Requests.Queries;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class ExceptionHandlingIntegrationTests
{
    private readonly ApiTestFixture _fixture;

    public ExceptionHandlingIntegrationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task ExceptionPipeline_WhenHandlerThrowsValidationException_ReturnsProblemDetailsBadRequest()
    {
        var validationResult = new ValidationResult([
            new ValidationFailure("ApprovalStatusId", "ApprovalStatusId does not exist.")
        ]);

        using var client = CreateClientThatThrows(new Explore.Application.Exceptions.ValidationException(validationResult));
        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("errors", out var errors)).IsTrue();
        await Assert.That(errors.TryGetProperty("validation", out _)).IsTrue();
    }

    [Test]
    public async Task ExceptionPipeline_WhenHandlerThrowsNotFoundException_ReturnsProblemDetailsNotFound()
    {
        using var client = CreateClientThatThrows(new NotFoundException("Organization", Guid.NewGuid()));
        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, "Resource not found");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var detail = document.RootElement.GetProperty("detail").GetString() ?? string.Empty;
        await Assert.That(detail).Contains("was not found");
    }

    [Test]
    public async Task ExceptionPipeline_WhenHandlerThrowsUnhandledException_ReturnsSanitizedProblemDetails()
    {
        const string sensitiveMessage = "Sensitive internals should not be exposed";

        using var client = CreateClientThatThrows(new InvalidOperationException(sensitiveMessage));
        var response = await client.GetAsync($"/api/actor/{Guid.NewGuid()}");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError, "Internal server error");

        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        var root = document.RootElement;

        var detail = root.GetProperty("detail").GetString() ?? string.Empty;
        await Assert.That(detail).IsEqualTo("An unexpected error occurred.");
        await Assert.That(detail).DoesNotContain(sensitiveMessage);
        await Assert.That(root.TryGetProperty("stackTrace", out _)).IsFalse();
    }

    private HttpClient CreateClientThatThrows(Exception exception)
    {
        var throwingHandler = Substitute.For<IQueryHandler<GetActorDetailsRequest, ActorDto?>>();
        throwingHandler.QueryAsync(Arg.Any<GetActorDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns<ActorDto?>(_ => throw exception);

        var app = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IQueryHandler<GetActorDetailsRequest, ActorDto?>>();
                services.AddSingleton(throwingHandler);
            });
        });

        return app.CreateClient();
    }
}
