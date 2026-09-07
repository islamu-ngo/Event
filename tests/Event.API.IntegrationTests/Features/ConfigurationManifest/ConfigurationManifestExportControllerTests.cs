namespace Event.Api.IntegrationTests.Features.ConfigurationManifest;

using System.Net;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.Application.Contracts.Infrastructure;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

public sealed class ConfigurationManifestExportControllerTests
{
    private const string CanonicalPath =
        "/api/control-plane/configuration-manifest/export";
    private const string CanonicalRoute =
        "api/control-plane/configuration-manifest/export";
    private const string OperationId = "ExportConfigurationManifest";
    private const string CanonicalFileName = "configuration-manifest-overrides.json";
    private const string ControllerTypeName =
        "Explore.API.Controllers.ConfigurationManifestExportsController";

    [Test]
    public async Task Endpoint_UsesCanonicalRouteOperationMediaTypeAndBufferedNoStoreResponse()
    {
        Type controller = RequireController();
        RouteAttribute route = controller.GetCustomAttribute<RouteAttribute>()
            ?? throw new InvalidOperationException("The whole-instance export controller must declare its route.");
        MethodInfo action = controller.GetMethod("Export", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The whole-instance export action was not found.");
        HttpMethodAttribute http = action.GetCustomAttributes<HttpMethodAttribute>().Single();

        await Assert.That(route.Template).IsEqualTo(CanonicalRoute);
        await Assert.That(controller.IsDefined(typeof(AuthorizeAttribute), inherit: true)).IsTrue();
        await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Admin);
        await Assert.That(controller.IsDefined(typeof(RequireMultiTenantAttribute), inherit: true))
            .IsFalse()
            .Because("single- and multi-tenant instances share this endpoint");
        await Assert.That(http.HttpMethods).HasSingleItem().And.Contains(HttpMethods.Get);
        await Assert.That(http.Name).IsEqualTo(OperationId);
        await Assert.That(action.IsDefined(typeof(PrivateNoStoreAttribute), inherit: true)).IsTrue();
        await Assert.That(action.ReturnType.IsGenericType).IsTrue();
        await Assert.That(action.ReturnType.GetGenericArguments().Single())
            .IsEqualTo(typeof(FileContentResult))
            .Because("the complete bounded export must be preflighted before response bytes are exposed");

        ParameterInfo[] callerInputs = action.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(CancellationToken))
            .ToArray();
        await Assert.That(callerInputs.Any(parameter => parameter.ParameterType == typeof(Guid)))
            .IsFalse()
            .Because("a caller must not select an instance identity");
        await Assert.That(callerInputs).HasSingleItem();

        ProducesAttribute produces = action.GetCustomAttribute<ProducesAttribute>()
            ?? throw new InvalidOperationException("The export media type must be explicit.");
        await Assert.That(produces.ContentTypes).Contains(ConfigurationManifestContractMetadata.MediaType);
        AssertProblemResponse(action, StatusCodes.Status400BadRequest);
        AssertProblemResponse(action, StatusCodes.Status401Unauthorized);
        AssertProblemResponse(action, StatusCodes.Status403Forbidden);
        AssertProblemResponse(action, StatusCodes.Status413PayloadTooLarge);
        AssertProblemResponse(action, StatusCodes.Status429TooManyRequests);
        AssertProblemResponse(action, StatusCodes.Status503ServiceUnavailable);
    }

    [Test]
    public async Task Endpoint_UsesServerFixedSingleInstanceSelectionWithoutTenantContext()
    {
        Type controller = RequireController();
        ConstructorInfo constructor = controller.GetConstructors().Single();
        Type[] dependencies = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        MethodInfo action = controller.GetMethod("Export", BindingFlags.Public | BindingFlags.Instance)!;

        await Assert.That(dependencies).HasSingleItem();
        await Assert.That(dependencies.Single()).IsEqualTo(typeof(MediatR.IMediator));
        await Assert.That(action.GetParameters().Any(parameter => parameter.ParameterType == typeof(Guid)))
            .IsFalse()
            .Because("the deployment has one server-fixed instance and callers cannot select another");
        await Assert.That(dependencies.Any(type => type.Name.Contains("TenantContext", StringComparison.Ordinal)))
            .IsFalse()
            .Because("ambient tenant context cannot scope a whole-instance read");
    }

    [Test]
    public async Task AnonymousTenantAdminAndWrongInstanceCallersFailClosed()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = false }
        };
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage anonymous = await client.GetAsync($"{CanonicalPath}?view=Overrides");
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

        using var tenantRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{CanonicalPath}?view=Portable");
        tenantRequest.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(
                Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5673"),
                Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5674")));
        using HttpResponseMessage tenant = await client.SendAsync(tenantRequest);
        await Assert.That(tenant.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        using var wrongInstanceRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{CanonicalPath}?view=Overrides");
        wrongInstanceRequest.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(
                Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5675"),
                "Wrong instance control plane",
                ("explore:admin:instance", "true"),
                ("managed_instance_id", "0199464e-e388-7f56-9281-cefabd6a5676")));
        using HttpResponseMessage wrongInstance = await client.SendAsync(wrongInstanceRequest);
        await Assert.That(wrongInstance.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task ProviderUnavailableReturnsStableServiceUnavailableProblem()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new ProviderUnavailableAuthorizationProvider()
        };
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{CanonicalPath}?view=Overrides");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(
                Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5673")));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32())
            .IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("authorization_provider_unavailable");
    }

    [Test]
    public async Task CerbosAndLocalPoliciesConsumeTheSameExplicitExportFact()
    {
        string root = FindRepositoryRoot();
        string cerbosPolicy = await File.ReadAllTextAsync(Path.Combine(
            root,
            "cerbos/policies/islamuevent_instance_setting.yaml"));
        string localPolicy = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src/Explore.Infrastructure/Services/FallbackAuthorizationService.cs"));

        const string factToken = "configurationManifestExport";
        await Assert.That(cerbosPolicy).Contains(factToken, StringComparison.Ordinal);
        await Assert.That(localPolicy).Contains(factToken, StringComparison.Ordinal);
        await Assert.That(cerbosPolicy).Contains("islamuevent_instance_setting", StringComparison.Ordinal);
        await Assert.That(cerbosPolicy).Contains("view", StringComparison.Ordinal);
    }

    [Test]
    public async Task AggregateOverflowAdvertisesStableProblemAndCannotStreamPartialManifest()
    {
        Type controller = RequireController();
        MethodInfo action = controller.GetMethod("Export", BindingFlags.Public | BindingFlags.Instance)!;
        AssertProblemResponse(action, StatusCodes.Status413PayloadTooLarge);
        await Assert.That(action.ReturnType.GetGenericArguments().Single())
            .IsEqualTo(typeof(FileContentResult));

        Type apiContract = controller.Assembly.GetType(
            "Explore.API.Controllers.ConfigurationManifestExportApiContract")
            ?? throw new InvalidOperationException("Missing canonical configuration manifest API contract.");
        await Assert.That(apiContract.GetField("MediaType")?.GetRawConstantValue())
            .IsEqualTo(ConfigurationManifestContractMetadata.MediaType);
        await Assert.That(apiContract.GetField("OverridesFileName")?.GetRawConstantValue())
            .IsEqualTo(CanonicalFileName);
        await Assert.That(apiContract.GetField("TooLargeFailureCode")?.GetRawConstantValue())
            .IsEqualTo("configuration_manifest_export_too_large");
    }

    [Test]
    public async Task TenantBoundOverflowReturnsStablePayloadTooLargeProblem()
    {
        var handler = Substitute.For<IRequestHandler<
            ExportConfigurationManifestQuery,
            ConfigurationManifestExportResult>>();
        handler.Handle(
                Arg.Any<ExportConfigurationManifestQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ConfigurationManifestExportResult>(
                new ConfigurationManifestExportTooLargeException()));
        await using var baseFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequestHandler<
                    ExportConfigurationManifestQuery,
                    ConfigurationManifestExportResult>>();
                services.AddScoped(_ => handler);
            }));
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{CanonicalPath}?view=Overrides");
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(
                Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5673")));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsByteArrayAsync());
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32())
            .IsEqualTo(StatusCodes.Status413PayloadTooLarge);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo(ConfigurationManifestExportContract.TooLargeFailureCode);
    }

    [Test]
    public async Task LegacyTenantShapedRoutesAreNotExportAliases()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        using HttpClient client = factory.CreateClient();
        string[] obsoleteRoutes =
        [
            "/api/tenant/settings/configuration-manifest/export?view=Overrides",
            "/api/admin/control-plane/tenants/0199464e-e388-7f56-9281-cefabd6a5673/configuration-manifest/export?view=Portable"
        ];

        foreach (string obsoleteRoute in obsoleteRoutes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, obsoleteRoute);
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateInstanceAdminHeaderValue(
                    Guid.Parse("0199464e-e388-7f56-9281-cefabd6a5674")));
            using HttpResponseMessage response = await client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }

    private static Type RequireController() =>
        typeof(Program).Assembly.GetType(ControllerTypeName)
        ?? throw new InvalidOperationException(
            $"Missing planned whole-instance API surface: {ControllerTypeName}.");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void AssertProblemResponse(MethodInfo action, int statusCode)
    {
        ProducesResponseTypeAttribute? response = action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .SingleOrDefault(candidate => candidate.StatusCode == statusCode);
        if (response?.Type != typeof(ProblemDetails))
        {
            throw new InvalidOperationException(
                $"{action.DeclaringType?.Name}.{action.Name} must advertise ProblemDetails for HTTP {statusCode}.");
        }
    }

    private sealed class ProviderUnavailableAuthorizationProvider : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizationDecision.Deny(
                AuthorizationProviderMetadata.Cerbos,
                AuthorizationDecisionReasonCodes.ProviderUnavailable));

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizationDecision>>(
                requests.Select(_ => AuthorizationDecision.Deny(
                    AuthorizationProviderMetadata.Cerbos,
                    AuthorizationDecisionReasonCodes.ProviderUnavailable)).ToArray());
    }
}
