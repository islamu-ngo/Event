namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.ComponentModel.DataAnnotations;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.ConfigurationImport;
using Explore.API.Controllers;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Tenant;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using MediatR;
using NSubstitute;

public sealed class ConfigurationImportSessionControllerTests
{
    [Test]
    public async Task Controllers_ExposeSeparateProtectedNoStoreRoutes()
    {
        await AssertController(
            typeof(InstanceConfigurationImportSessionsController),
            "api/control-plane/configuration-import/sessions",
            expectedTenantRoute: false);
        await AssertController(
            typeof(TenantConfigurationImportSessionsController),
            "api/tenants/{tenantId:guid}/configuration-import/sessions",
            expectedTenantRoute: true);
    }

    [Test]
    public async Task Uploads_UseDedicatedBoundedPolicies()
    {
        foreach (Type controller in Controllers())
        {
            MethodInfo create = RequireMethod(controller, "Create");
            await Assert.That(
                    create.GetCustomAttribute<EnableRateLimitingAttribute>()
                        ?.PolicyName)
                .IsEqualTo(
                    ConfigurationImportApiBoundary.UploadRateLimitPolicy);
            await Assert.That(
                    create.GetCustomAttribute<RequestTimeoutAttribute>()
                        ?.PolicyName)
                .IsEqualTo(
                    ConfigurationImportApiBoundary
                        .UploadRequestTimeoutPolicy);
            await Assert.That(
                    create.GetCustomAttribute<RequestSizeLimitAttribute>())
                .IsNotNull();
        }
    }

    [Test]
    public async Task PreviewBody_CannotSelectTargetOrFreshnessAuthority()
    {
        string[] properties = typeof(ConfigurationImportPreviewRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(properties).IsEquivalentTo(
        [
            "ApplyMode",
            "GrantedApprovalCodes",
            "Mappings",
            "SelectedSectionKeys"
        ]);
        await Assert.That(properties.Any(property =>
                property.Contains("Target", StringComparison.OrdinalIgnoreCase)
                || property.Contains("Digest", StringComparison.OrdinalIgnoreCase)
                || property.Contains("Revision", StringComparison.OrdinalIgnoreCase)
                || property.Contains("Snapshot", StringComparison.OrdinalIgnoreCase)
                || property.Contains("Token", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
        await Assert.That(
                typeof(ConfigurationImportPreviewResult)
                    .GetProperties()
                    .Any(property =>
                        property.Name.Contains(
                            "Token",
                            StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
        await Assert.That(
                typeof(ConfigurationImportSessionCreatedResult)
                    .GetProperties()
                    .Count(property =>
                        property.Name == "AccessToken"))
            .IsEqualTo(1);
    }

    [Test]
    public async Task Commands_UseExactInstanceAndTenantAuthorizationFacts()
    {
        var instance = (ISecureRequest)
            new CreateInstanceConfigurationImportSessionCommand(
                ReadOnlyMemory<byte>.Empty);
        Guid tenantId = Guid.NewGuid();
        var tenant = (ISecureRequest)
            new CreateTenantConfigurationImportSessionCommand(
                tenantId,
                ReadOnlyMemory<byte>.Empty);

        await Assert.That(instance.ResourceId)
            .IsEqualTo(
                CreateInstanceConfigurationImportSessionCommand.ResourceKey);
        await Assert.That(instance.AuthorizationFacts)
            .IsTypeOf<InstanceScopedAuthorizationFacts>();
        await Assert.That(tenant.ResourceId)
            .IsEqualTo(
                CreateTenantConfigurationImportSessionCommand.ResourceKey);
        TenantSettingAuthorizationFacts tenantFacts =
            await Assert.That(tenant.AuthorizationFacts)
                .IsTypeOf<TenantSettingAuthorizationFacts>();
        await Assert.That(tenantFacts.TenantId).IsEqualTo(tenantId);
        await Assert.That(tenantFacts.DocumentKey)
            .IsEqualTo(
                CreateTenantConfigurationImportSessionCommand.ResourceKey);

        AuthorizeResourceAttribute instancePolicy =
            typeof(CreateInstanceConfigurationImportSessionCommand)
                .GetCustomAttribute<AuthorizeResourceAttribute>()
            ?? throw new InvalidOperationException();
        AuthorizeResourceAttribute tenantPolicy =
            typeof(CreateTenantConfigurationImportSessionCommand)
                .GetCustomAttribute<AuthorizeResourceAttribute>()
            ?? throw new InvalidOperationException();
        await Assert.That(instancePolicy.Action)
            .IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(tenantPolicy.Action)
            .IsEqualTo(AuthorizationActions.TenantSettings.Update);
    }

    [Test]
    public async Task Commands_NeverPrintArtifactsOrCapabilityTokens()
    {
        const string sentinel = "capability-token-sentinel";
        var preview = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["tenant.settings"],
            Mappings = new Dictionary<string, string>
            {
                ["tenant.settings"] = sentinel
            },
            ApplyMode = ConfigurationImportApplyMode.PreviewOnly,
            GrantedApprovalCodes = []
        };
        object[] requests =
        [
            new PreviewInstanceConfigurationImportSessionCommand(
                Guid.NewGuid(),
                sentinel,
                preview),
            new CancelInstanceConfigurationImportSessionCommand(
                Guid.NewGuid(),
                sentinel),
            new PreviewTenantConfigurationImportSessionCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                sentinel,
                preview)
        ];

        foreach (object request in requests)
            await Assert.That(request.ToString()).DoesNotContain(sentinel);
        await Assert.That(preview.ToString()).DoesNotContain(sentinel);
    }

    [Test]
    public async Task ParentHal_AdvertisesOnlyPermissionBoundUploadAffordances()
    {
        LinkDefinition instance = new ControlPlaneOverviewLinkPolicy()
            .GetLinks(new ControlPlaneOverviewDto(), user: null)
            .Single(link =>
                link.Rel ==
                LinkRelations.CreateConfigurationImportSession);
        Guid tenantId = Guid.NewGuid();
        LinkDefinition tenant = new TenantDetailLinkPolicy()
            .GetLinks(
                new TenantDto
                {
                    Id = tenantId,
                    FullName = "Tenant",
                    Slug = "tenant"
                },
                user: null)
            .Single(link =>
                link.Rel ==
                LinkRelations.CreateConfigurationImportSession);

        await Assert.That(instance.RouteName)
            .IsEqualTo(RouteNames.CreateInstanceConfigurationImportSession);
        await Assert.That(instance.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(instance.PermissionAction)
            .IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(instance.PermissionFacts)
            .IsTypeOf<InstanceScopedAuthorizationFacts>();

        await Assert.That(tenant.RouteName)
            .IsEqualTo(RouteNames.CreateTenantConfigurationImportSession);
        await Assert.That(tenant.Method).IsEqualTo(HttpMethods.Post);
        await Assert.That(tenant.PermissionAction)
            .IsEqualTo(AuthorizationActions.TenantSettings.Update);
        TenantSettingAuthorizationFacts facts =
            await Assert.That(tenant.PermissionFacts)
                .IsTypeOf<TenantSettingAuthorizationFacts>();
        await Assert.That(facts.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task CreatedSession_HalPublishesOnlyPreviewRefreshAndCancelActions()
    {
        const string capability = "one-time-capability-sentinel";
        Guid sessionId = Guid.NewGuid();
        IMediator mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<CreateInstanceConfigurationImportSessionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new ConfigurationImportSessionCreatedResult(
                sessionId,
                capability,
                ConfigurationImportScope.Instance,
                TargetTenantId: null,
                ConfigurationImportSessionState.Uploaded,
                new DateTime(
                    2026,
                    8,
                    30,
                    23,
                    0,
                    0,
                    DateTimeKind.Utc),
                ArtifactByteLength: 2,
                AvailableSectionKeys: ["instance.settings"]));
        var controller =
            new InstanceConfigurationImportSessionsController(
                mediator,
                Substitute.For<IAuthorizationProvider>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                },
                Url = Substitute.For<IUrlHelper>()
            };
        controller.Request.Body = new MemoryStream([0x7b, 0x7d]);
        controller.Request.ContentLength = 2;
        controller.Url.Link(
                Arg.Any<string>(),
                Arg.Any<object>())
            .Returns(call => $"https://test.invalid/{call.ArgAt<string>(0)}");

        ActionResult<HalResource<ConfigurationImportSessionCreatedResult>>
            action = await controller.Create(CancellationToken.None);
        ObjectResult result = await Assert.That(action.Result)
            .IsTypeOf<ObjectResult>();
        HalResource<ConfigurationImportSessionCreatedResult> resource =
            await Assert.That(result.Value)
                .IsTypeOf<
                    HalResource<ConfigurationImportSessionCreatedResult>>();

        await Assert.That(result.StatusCode)
            .IsEqualTo(StatusCodes.Status201Created);
        await Assert.That(resource.Links.Keys).IsEquivalentTo(
        [
            LinkRelations.PreviewConfigurationImport,
            LinkRelations.RefreshConfigurationImportPreview,
            LinkRelations.CancelConfigurationImport
        ]);
        await Assert.That(
                resource.Links.Values.Any(link =>
                    link.Href.Contains(
                        capability,
                        StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task ProblemMapping_HidesWrongScopeAndSeparatesExpiryAndSize()
    {
        await AssertProblem(
            ConfigurationImportFailureCodes.TargetMismatch,
            StatusCodes.Status404NotFound,
            ConfigurationImportFailureCodes.ArtifactMissing);
        await AssertProblem(
            ConfigurationImportFailureCodes.TokenInvalid,
            StatusCodes.Status404NotFound,
            ConfigurationImportFailureCodes.ArtifactMissing);
        await AssertProblem(
            ConfigurationImportFailureCodes.Expired,
            StatusCodes.Status409Conflict,
            ConfigurationImportFailureCodes.Expired);
        await AssertProblem(
            ConfigurationImportFailureCodes.TooLarge,
            StatusCodes.Status413PayloadTooLarge,
            ConfigurationImportFailureCodes.TooLarge);
    }

    [Test]
    public async Task UploadRoute_MapsAnonymousDeniedAndUnavailableAuthorization()
    {
        await AssertUploadStatus(
            provider: new StubAuthorizationProvider { AllowAll = true },
            authenticated: true,
            HttpStatusCode.BadRequest);
        await AssertUploadStatus(
            provider: new StubAuthorizationProvider { AllowAll = true },
            authenticated: false,
            HttpStatusCode.Unauthorized);
        await AssertUploadStatus(
            provider: new StubAuthorizationProvider { AllowAll = false },
            authenticated: true,
            HttpStatusCode.Forbidden);
        await AssertUploadStatus(
            provider: new UnavailableAuthorizationProvider(),
            authenticated: true,
            HttpStatusCode.ServiceUnavailable);
    }

    private static async Task AssertController(
        Type controller,
        string routeTemplate,
        bool expectedTenantRoute)
    {
        await Assert.That(
                controller.GetCustomAttribute<RouteAttribute>()?.Template)
            .IsEqualTo(routeTemplate);
        await Assert.That(controller.GetCustomAttribute<AuthorizeAttribute>())
            .IsNotNull();
        MethodInfo[] actions = controller.GetMethods(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Where(method =>
                method.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToArray();
        await Assert.That(actions.Length).IsEqualTo(8);
        foreach (MethodInfo action in actions)
        {
            await Assert.That(
                    action.GetCustomAttribute<PrivateNoStoreAttribute>())
                .IsNotNull();
            ParameterInfo? accessToken = action.GetParameters()
                .SingleOrDefault(parameter =>
                    parameter.Name == "accessToken");
            if (action.GetParameters().Any(parameter =>
                    parameter.Name == "sessionId"))
            {
                await Assert.That(accessToken).IsNotNull();
                await Assert.That(
                        accessToken!.GetCustomAttribute<FromHeaderAttribute>()
                            ?.Name)
                    .IsEqualTo(
                        ConfigurationImportApiBoundary.AccessTokenHeader);
                await Assert.That(
                        accessToken.GetCustomAttribute<RequiredAttribute>())
                    .IsNotNull();
            }
            await Assert.That(
                    action.GetParameters().Any(parameter =>
                        parameter.Name == "tenantId"))
                .IsEqualTo(expectedTenantRoute);
        }
    }

    private static async Task AssertProblem(
        string failureCode,
        int expectedStatus,
        string expectedCode)
    {
        ProblemDetailsContext? captured = null;
        IProblemDetailsService writer =
            Substitute.For<IProblemDetailsService>();
        writer.TryWriteAsync(
                Arg.Do<ProblemDetailsContext>(context => captured = context))
            .Returns(new ValueTask<bool>(true));
        var handler = new ConfigurationImportExceptionHandler(writer);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/configuration-import";

        bool handled = await handler.TryHandleAsync(
            httpContext,
            new ConfigurationImportSessionException(failureCode),
            CancellationToken.None);

        await Assert.That(handled).IsTrue();
        await Assert.That(httpContext.Response.StatusCode)
            .IsEqualTo(expectedStatus);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.ProblemDetails.Extensions["code"])
            .IsEqualTo(expectedCode);
        string serialized = System.Text.Json.JsonSerializer.Serialize(
            captured.ProblemDetails);
        await Assert.That(serialized)
            .DoesNotContain(ConfigurationImportFailureCodes.TargetMismatch);
        await Assert.That(serialized)
            .DoesNotContain(ConfigurationImportFailureCodes.TokenInvalid);
    }

    private static async Task AssertUploadStatus(
        Explore.Application.Contracts.Infrastructure.IAuthorizationProvider provider,
        bool authenticated,
        HttpStatusCode expectedStatus)
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = provider
        };
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/control-plane/configuration-import/sessions");
        if (authenticated)
        {
            request.Headers.Add(
                TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        }
        var artifact = new ByteArrayContent([0x7b, 0x7d]);
        artifact.Headers.ContentType =
            new MediaTypeHeaderValue(
                ConfigurationManifestContractMetadata.MediaType);
        request.Content = artifact;

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    private static MethodInfo RequireMethod(Type controller, string name) =>
        controller.GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"{controller.Name}.{name} is missing.");

    private static Type[] Controllers() =>
    [
        typeof(InstanceConfigurationImportSessionsController),
        typeof(TenantConfigurationImportSessionsController)
    ];

    private sealed class UnavailableAuthorizationProvider
        : Explore.Application.Contracts.Infrastructure.IAuthorizationProvider
    {
        public Task<
            Explore.Application.Contracts.Infrastructure.AuthorizationDecision>
            AuthorizeAsync(
            Explore.Application.Contracts.Infrastructure.AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Explore.Application.Contracts.Infrastructure.AuthorizationDecision
                    .Deny(
                        Explore.Application.Contracts.Infrastructure
                            .AuthorizationProviderMetadata.Cerbos,
                        Explore.Application.Contracts.Infrastructure
                            .AuthorizationDecisionReasonCodes.ProviderUnavailable));

        public Task<
            IReadOnlyList<
                Explore.Application.Contracts.Infrastructure.AuthorizationDecision>>
            AuthorizeBatchAsync(
            IReadOnlyList<
                Explore.Application.Contracts.Infrastructure.AuthorizationRequest>
                requests,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<
                Explore.Application.Contracts.Infrastructure.AuthorizationDecision>>(
                requests.Select(_ =>
                        Explore.Application.Contracts.Infrastructure
                            .AuthorizationDecision.Deny(
                                Explore.Application.Contracts.Infrastructure
                                    .AuthorizationProviderMetadata.Cerbos,
                                Explore.Application.Contracts.Infrastructure
                                    .AuthorizationDecisionReasonCodes
                                    .ProviderUnavailable))
                    .ToArray());
    }
}
