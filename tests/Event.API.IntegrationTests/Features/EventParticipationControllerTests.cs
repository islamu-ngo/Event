using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventParticipation.Handlers.Commands;
using Explore.Application.Features.EventParticipation.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Operations;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventParticipationControllerTests
{
    [Test]
    public async Task ConfigureCommand_UsesManageRegistrationsAuthorization()
    {
        var commandAuthorization = typeof(ConfigureEventParticipationCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()!;

        await Assert.That(commandAuthorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(commandAuthorization.Action).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);
    }

    [Test]
    public async Task ConfigureRoute_UsesExplicitAuthenticatedWriteContract()
    {
        var action = typeof(EventParticipationController).GetMethod(nameof(EventParticipationController.Configure))!;
        var route = action.GetCustomAttribute<HttpPatchAttribute>()!;

        await Assert.That(route.Template).IsEqualTo(string.Empty);
        await Assert.That(route.Name).IsEqualTo(RouteNames.ConfigureEventParticipation);
        await Assert.That(action.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<EndpointClassificationAttribute>()!.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(action.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes)
            .Contains(HateoasConstants.JsonMediaType);
        await Assert.That(LinkRelations.ConfigureParticipation).IsEqualTo("configure-participation");

        var ifMatchParameter = action.GetParameters()
            .Single(parameter => parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == "If-Match");
        await Assert.That(ifMatchParameter.ParameterType).IsEqualTo(typeof(string));
        await Assert.That(ifMatchParameter.GetCustomAttribute<RequiredAttribute>()).IsNotNull();

        var commandAuthorization = typeof(ConfigureEventParticipationCommand)
            .GetCustomAttribute<AuthorizeResourceAttribute>()!;
        await Assert.That(commandAuthorization.Resource).IsEqualTo(ResourceKinds.Event);
        await Assert.That(commandAuthorization.Action).IsEqualTo(AuthorizationActions.Events.ManageRegistrations);

        var secureRequest = new ConfigureEventParticipationCommand
        {
            EventId = Guid.NewGuid(),
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            ParticipationConfiguration = CreateParticipationConfiguration()
        };
        await Assert.That(((ISecureRequest)secureRequest).ResourceId)
            .IsEqualTo(secureRequest.EventId.ToString());

        await AssertProducesProblem(action, StatusCodes.Status400BadRequest);
        await AssertProducesProblem(action, StatusCodes.Status401Unauthorized);
        await AssertProducesProblem(action, StatusCodes.Status403Forbidden);
        await AssertProducesProblem(action, StatusCodes.Status404NotFound);
        await AssertProducesProblem(action, StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task ConfigureDto_RequiresModeAndObligationOnly()
    {
        var dtoType = typeof(ConfigureEventParticipationDto);

        await Assert.That(dtoType.GetProperty(nameof(ConfigureEventParticipationDto.ParticipationHandlingModeId))!
            .GetCustomAttribute<RequiredAttribute>()).IsNotNull();
        await Assert.That(dtoType.GetProperty(nameof(ConfigureEventParticipationDto.AdvanceRegistrationObligationId))!
            .GetCustomAttribute<RequiredAttribute>()).IsNotNull();
        await Assert.That(dtoType.GetProperty(nameof(ConfigureEventParticipationDto.IdentityAccessModeId))!
            .GetCustomAttribute<RequiredAttribute>()).IsNull();
        await Assert.That(dtoType.GetProperty(nameof(ConfigureEventParticipationDto.GuestRecoveryPolicy))!
            .GetCustomAttribute<RequiredAttribute>()).IsNull();
    }

    [Test]
    public async Task Configure_WhenCommandSucceeds_ForwardsHeaderAndBodyToCommandHandler()
    {
        var context = new EventParticipationTestContext(command => BaseCommandResponse.Success(
            command.EventId,
            "Event participation configuration updated."));
        await using var factory = CreateFactoryWithHandler(context);
        using var client = factory.CreateClient();

        Guid eventId = Guid.NewGuid();
        Guid concurrencyStamp = Guid.NewGuid();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            $"/api/events/{eventId}/participation",
            CreateParticipationConfiguration(),
            concurrencyStamp);

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var command = context.LastCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.EventId).IsEqualTo(eventId);
        await Assert.That(command.ExpectedConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(command.ParticipationConfiguration.ParticipationHandlingModeId)
            .IsEqualTo((int)Explore.Domain.Enums.ParticipationHandlingModeEnum.InformationOnly);
        await Assert.That(command.ParticipationConfiguration.AdvanceRegistrationObligationId)
            .IsEqualTo((int)Explore.Domain.Enums.AdvanceRegistrationObligationEnum.NotApplicable);
    }

    [Test]
    public async Task Configure_WhenCommandReportsConcurrencyConflict_UsesConflictHelper()
    {
        var context = new EventParticipationTestContext(_ => BaseCommandResponse.Failure<Guid>(
            "event_participation_configuration_concurrency_conflict",
            "Event participation configuration conflict."));
        await using var factory = CreateFactoryWithHandler(context);
        using var client = factory.CreateClient();

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            $"/api/events/{Guid.NewGuid()}/participation",
            CreateParticipationConfiguration(),
            Guid.NewGuid());

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.Conflict,
            "Event participation configuration conflict");
    }

    [Test]
    public Task Configure_WhenIfMatchIsMissing_ReturnsValidationProblemDetails()
        => AssertInvalidIfMatchRejectedAsync(null);

    [Test]
    public Task Configure_WhenIfMatchIsEmpty_ReturnsValidationProblemDetails()
        => AssertInvalidIfMatchRejectedAsync(string.Empty);

    [Test]
    public Task Configure_WhenIfMatchIsUnquoted_ReturnsValidationProblemDetails()
        => AssertInvalidIfMatchRejectedAsync(Guid.NewGuid().ToString("D"));

    [Test]
    public Task Configure_WhenIfMatchIsWeak_ReturnsValidationProblemDetails()
        => AssertInvalidIfMatchRejectedAsync($"W/\"{Guid.NewGuid():D}\"");

    [Test]
    public Task Configure_WhenIfMatchIsMalformed_ReturnsValidationProblemDetails()
        => AssertInvalidIfMatchRejectedAsync("\"not-a-guid\"");

    private static WebApplicationFactory<Program> CreateFactoryWithHandler(EventParticipationTestContext testContext)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var catalog = services.SingleOrDefault(d => d.ServiceType == typeof(NativeOperationCatalog))?.ImplementationInstance as NativeOperationCatalog;
                if (catalog is not null)
                {
                    var entries = catalog.Registrations
                        .Where(r => r.Implementation == typeof(ConfigureEventParticipationCommandHandler))
                        .ToArray();
                    foreach (var entry in entries)
                    {
                        catalog.Registrations.Remove(entry);
                        services.Remove(entry.PublicDescriptor);
                        services.Remove(entry.ConcreteDescriptor);
                    }
                }

                services.AddSingleton(testContext);
                services.AddNativeOperations([
                    typeof(ConfigureEventParticipationCommand),
                    typeof(TestConfigureParticipationHandler)
                ]);
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

    private static ConfigureEventParticipationDto CreateParticipationConfiguration() => new()
    {
        ParticipationHandlingModeId = (int)Explore.Domain.Enums.ParticipationHandlingModeEnum.InformationOnly,
        AdvanceRegistrationObligationId = (int)Explore.Domain.Enums.AdvanceRegistrationObligationEnum.NotApplicable
    };

    private static async Task AssertProducesProblem(MethodInfo action, int statusCode)
    {
        var problemType = statusCode == StatusCodes.Status400BadRequest
            ? typeof(ValidationProblemDetails)
            : typeof(ProblemDetails);

        await Assert.That(action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == problemType)).IsTrue();
    }

    private static async Task AssertInvalidIfMatchRejectedAsync(string? ifMatch)
    {
        var context = new EventParticipationTestContext(_ =>
            throw new InvalidOperationException("Command handler should not run when If-Match is invalid."));
        await using var factory = CreateFactoryWithHandler(context);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Patch,
            $"/api/events/{Guid.NewGuid():D}/participation",
            CreateParticipationConfiguration());
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Status).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(problem.Errors).IsNotEmpty();
        await Assert.That(context.LastCommand).IsNull();
    }

    private sealed class TestConfigureParticipationHandler(EventParticipationTestContext context)
        : ICommandHandler<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>>
    {
        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            ConfigureEventParticipationCommand command,
            CancellationToken cancellationToken = default)
        {
            context.LastCommand = command;
            return Task.FromResult(context.ResponseFactory(command));
        }
    }

    private sealed class EventParticipationTestContext(Func<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>> responseFactory)
    {
        public Func<ConfigureEventParticipationCommand, BaseCommandResponse<Guid>> ResponseFactory { get; set; } = responseFactory;
        public ConfigureEventParticipationCommand? LastCommand { get; set; }
    }
}
