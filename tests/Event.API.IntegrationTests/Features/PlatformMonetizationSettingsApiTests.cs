using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PlatformMonetization;
using Explore.Application.Features.PlatformMonetization.Requests.Commands;
using Explore.Application.Features.PlatformMonetization.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category("PlatformMonetization")]
public sealed class PlatformMonetizationSettingsApiTests
{
    [Test]
    public async Task Controller_UsesProtectedAdminRoutesAndProblemDetailsContracts()
    {
        Type controller = typeof(PlatformMonetizationSettingsController);
        await Assert.That(controller.IsDefined(typeof(AuthorizeAttribute), true)).IsTrue();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Admin);

        await AssertAction(nameof(PlatformMonetizationSettingsController.Get), RouteNames.GetInstancePlatformMonetizationSettings, HttpMethods.Get);
        await AssertAction(nameof(PlatformMonetizationSettingsController.Update), RouteNames.UpdateInstancePlatformMonetizationSettings, HttpMethods.Put, expectConflict: true);
    }

    [Test]
    public async Task Get_WhenMediatorReturnsSettings_AssemblesHalResource()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<PlatformMonetizationSettingsDto, PlatformMonetizationSettingsDto>>();
        var settings = CreateSettings();
        var resource = new HalResource<PlatformMonetizationSettingsDto>(settings);
        mediator.Send(Arg.Any<GetPlatformMonetizationSettingsQuery>(), Arg.Any<CancellationToken>()).Returns(settings);
        assembler.ToResource(settings, Arg.Any<HttpContext>()).Returns(resource);
        var controller = CreateController(mediator, assembler);

        var result = await controller.Get(CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<ObjectResult>();
        await Assert.That(((ObjectResult)result.Result!).Value).IsEqualTo(resource);
        await mediator.Received(1).Send(Arg.Any<GetPlatformMonetizationSettingsQuery>(), Arg.Any<CancellationToken>());
        await assembler.Received(1).ToResource(settings, Arg.Any<HttpContext>());
    }

    [Test]
    public async Task Update_WhenMediatorSucceeds_DispatchesCompleteReplacementCommand()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<UpdatePlatformMonetizationSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(Guid.CreateVersion7()));
        var controller = CreateController(mediator, Substitute.For<IResourceAssembler<PlatformMonetizationSettingsDto, PlatformMonetizationSettingsDto>>());
        var update = new UpdatePlatformMonetizationSettingsDto
        {
            ExpectedFeeVersion = 1,
            ExpectedContributionVersion = 1,
            ContributionOptions = [new PlatformContributionOptionDto { ContributionBasisPoints = 0, SortOrder = 0, IsDefault = true }]
        };

        var result = await controller.Update(update, CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<UpdatePlatformMonetizationSettingsCommand>(command => command.Settings == update),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LinkPolicy_UsesOneInstanceSettingKeyAndPermissionBoundEdit()
    {
        var policy = new PlatformMonetizationSettingsLinkPolicy();

        LinkDefinition[] links = policy.GetLinks(CreateSettings(), null).ToArray();

        LinkDefinition self = links.Single(link => link.Rel == LinkRelations.Self);
        LinkDefinition edit = links.Single(link => link.Rel == LinkRelations.Edit);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstancePlatformMonetizationSettings);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(edit.RouteName).IsEqualTo(RouteNames.UpdateInstancePlatformMonetizationSettings);
        await Assert.That(edit.Method).IsEqualTo(HttpMethods.Put);
        await Assert.That(edit.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(links.All(link => link.PermissionResourceId == GetPlatformMonetizationSettingsQuery.SettingKey)).IsTrue();
        await Assert.That(links.All(link => link.PermissionResourceKind == ResourceKinds.InstanceSetting)).IsTrue();
    }

    private static PlatformMonetizationSettingsController CreateController(
        IMediator mediator,
        IResourceAssembler<PlatformMonetizationSettingsDto, PlatformMonetizationSettingsDto> assembler) => new(mediator, assembler)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static PlatformMonetizationSettingsDto CreateSettings() => new()
    {
        FeeVersion = 1,
        ContributionVersion = 1,
        ContributionOptions = [new PlatformContributionOptionDto { ContributionBasisPoints = 0, SortOrder = 0, IsDefault = true }]
    };

    private static async Task AssertAction(string actionName, string routeName, string method, bool expectConflict = false)
    {
        MethodInfo action = typeof(PlatformMonetizationSettingsController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"Action {actionName} was not found.");
        HttpMethodAttribute route = action.GetCustomAttributes<HttpMethodAttribute>().Single();
        IEnumerable<ProducesResponseTypeAttribute> responses = action.GetCustomAttributes<ProducesResponseTypeAttribute>();

        await Assert.That(route.Name).IsEqualTo(routeName);
        await Assert.That(route.HttpMethods.Single()).IsEqualTo(method);
        await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status400BadRequest).Type).IsEqualTo(typeof(ProblemDetails));
        await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status401Unauthorized).Type).IsEqualTo(typeof(ProblemDetails));
        await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status403Forbidden).Type).IsEqualTo(typeof(ProblemDetails));
        await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status404NotFound).Type).IsEqualTo(typeof(ProblemDetails));
        bool hasConflictResponse = responses.Any(response => response.StatusCode == StatusCodes.Status409Conflict);
        await Assert.That(hasConflictResponse).IsEqualTo(expectConflict);
        if (expectConflict)
        {
            await Assert.That(responses.Single(response => response.StatusCode == StatusCodes.Status409Conflict).Type)
                .IsEqualTo(typeof(ProblemDetails));
        }
    }
}

[Category("PlatformMonetization")]
[ClassDataSource<AuthenticatedApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class PlatformMonetizationSettingsAuthenticationApiTests(AuthenticatedApiTestFixture fixture)
{
    [Test]
    public async Task Get_WhenAnonymousOrOnlySetupSecret_ReturnsUnauthorized()
    {
        var anonymous = await fixture.Client.GetAsync("/api/instance/settings/platform-monetization");
        using var setupSecretOnly = new HttpRequestMessage(HttpMethod.Get, "/api/instance/settings/platform-monetization");
        setupSecretOnly.Headers.Add("X-Setup-Secret", "not-an-authentication-credential");
        var withSetupSecret = await fixture.Client.SendAsync(setupSecretOnly);

        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(withSetupSecret.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(anonymous.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(withSetupSecret.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
    }
}

[Category("PlatformMonetization")]
public sealed class PlatformMonetizationSettingsRuntimeApiTests
{
    [Test]
    public async Task HandlerDefense_WhenPipelineAllowsNonAdmin_GetAndPutReturnForbiddenBeforeMutation()
    {
        await using var factory = new PlatformMonetizationFactory(instanceAdmin: false, new PlatformMonetizationAuthorizationProvider());
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage get = Authenticated(HttpMethod.Get);
        using HttpRequestMessage put = Authenticated(HttpMethod.Put, CreateUpdate());

        using HttpResponseMessage getResponse = await client.SendAsync(get);
        using HttpResponseMessage putResponse = await client.SendAsync(put);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(getResponse, HttpStatusCode.Forbidden, "Forbidden");
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(putResponse, HttpStatusCode.Forbidden, "Forbidden");
        await Assert.That(factory.FeePolicies.MutationCount).IsEqualTo(0);
        await Assert.That(factory.Contributions.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task AdminGet_WhenUpdatePermissionIsDenied_ReturnsHalWithoutEdit()
    {
        await using var factory = new PlatformMonetizationFactory(
            instanceAdmin: true,
            new PlatformMonetizationAuthorizationProvider { AllowUpdateInBatch = false });
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = Authenticated(HttpMethod.Get);
        request.Headers.Accept.ParseAdd("application/hal+json");

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/hal+json");
        JsonElement links = document.RootElement.GetProperty("_links");
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("edit", out _)).IsFalse();
    }

    [Test]
    public async Task AdminPut_WhenValid_AdvancesBothActiveVersions()
    {
        await using var factory = new PlatformMonetizationFactory(instanceAdmin: true, new PlatformMonetizationAuthorizationProvider());
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = Authenticated(HttpMethod.Put, CreateUpdate());

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(factory.FeePolicies.Active.VersionNumber).IsEqualTo(2);
        await Assert.That(factory.Contributions.Active.VersionNumber).IsEqualTo(2);
    }

    [Test]
    public async Task AdminPut_WhenCurrencyIsInvalid_ReturnsValidationProblem()
    {
        UpdatePlatformMonetizationSettingsDto update = CreateUpdate(currencyCode: "XXX");
        await using var factory = new PlatformMonetizationFactory(instanceAdmin: true, new PlatformMonetizationAuthorizationProvider());
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = Authenticated(HttpMethod.Put, update);

        using HttpResponseMessage response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");
        await Assert.That(factory.FeePolicies.MutationCount).IsEqualTo(0);
    }

    [Test]
    public async Task AdminPut_WhenFeeVersionIsStale_ReturnsConcurrentUpdateProblem()
    {
        UpdatePlatformMonetizationSettingsDto update = CreateUpdate(expectedFeeVersion: 99);
        await using var factory = new PlatformMonetizationFactory(instanceAdmin: true, new PlatformMonetizationAuthorizationProvider());
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = Authenticated(HttpMethod.Put, update);

        using HttpResponseMessage response = await client.SendAsync(request);
        using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(document.RootElement.GetProperty("title").GetString()).IsEqualTo("Concurrency conflict");
        await Assert.That(document.RootElement.GetProperty("type").GetString()).IsEqualTo("/problems/concurrent_update");
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, UpdatePlatformMonetizationSettingsDto? update = null)
    {
        var request = new HttpRequestMessage(method, "/api/instance/settings/platform-monetization");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));
        if (update is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(update), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static UpdatePlatformMonetizationSettingsDto CreateUpdate(string currencyCode = "USD", int expectedFeeVersion = 1) => new()
    {
        FeeEnabled = true,
        FeeBasisPoints = 250,
        FixedCharges = [new PlatformFeeFixedChargeDto { CurrencyCode = currencyCode, AmountMinor = 25 }],
        ExpectedFeeVersion = expectedFeeVersion,
        ContributionEnabled = true,
        ContributionHeading = "Support the platform",
        ContributionBody = "Optional contribution",
        ContributionOptions =
        [
            new PlatformContributionOptionDto { ContributionBasisPoints = 0, SortOrder = 0, IsDefault = true },
            new PlatformContributionOptionDto { ContributionBasisPoints = 500, SortOrder = 1, IsDefault = false }
        ],
        ExpectedContributionVersion = 1
    };

    private sealed class PlatformMonetizationFactory(
        bool instanceAdmin,
        IAuthorizationProvider authorizationProvider) : AuthenticatedWebApplicationFactory
    {
        public PlatformFeePolicyRepositoryDouble FeePolicies { get; } = new();
        public PlatformContributionSettingRepositoryDouble Contributions { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            AuthorizationProviderOverride = authorizationProvider;
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                var adminContext = Substitute.For<IAdminContext>();
                adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(instanceAdmin);
                services.RemoveAll<IAdminContext>();
                services.AddSingleton(adminContext);
                services.RemoveAll<IPlatformFeePolicyRepository>();
                services.AddSingleton<IPlatformFeePolicyRepository>(FeePolicies);
                services.RemoveAll<IPlatformContributionSettingRepository>();
                services.AddSingleton<IPlatformContributionSettingRepository>(Contributions);
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IUnitOfWork, InlineUnitOfWork>();
            });
        }
    }

    private sealed class PlatformFeePolicyRepositoryDouble : IPlatformFeePolicyRepository
    {
        public PlatformFeePolicy Active { get; private set; } = PlatformFeePolicy.CreateDefault();
        public int MutationCount { get; private set; }

        public Task<PlatformFeePolicy?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult<PlatformFeePolicy?>(Active);

        public Task AddAsync(PlatformFeePolicy policy, CancellationToken cancellationToken)
        {
            MutationCount++;
            Active = policy;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PlatformFeePolicy policy, CancellationToken cancellationToken)
        {
            MutationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class PlatformContributionSettingRepositoryDouble : IPlatformContributionSettingRepository
    {
        public PlatformContributionSetting Active { get; private set; } = PlatformContributionSetting.CreateInitial(
            false,
            string.Empty,
            string.Empty,
            [PlatformContributionOption.Create(0, 0, true)]);
        public int MutationCount { get; private set; }

        public Task<PlatformContributionSetting?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult<PlatformContributionSetting?>(Active);

        public Task AddAsync(PlatformContributionSetting setting, CancellationToken cancellationToken)
        {
            MutationCount++;
            Active = setting;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PlatformContributionSetting setting, CancellationToken cancellationToken)
        {
            MutationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
    }

    private sealed class PlatformMonetizationAuthorizationProvider : IAuthorizationProvider
    {
        public bool AllowUpdateInBatch { get; init; } = true;

        public Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local));

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizationDecision>>(requests
                .Select(request => request.Action != AuthorizationActions.InstanceSettings.Update || AllowUpdateInBatch
                    ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                    : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local))
                .ToArray());
    }
}
