using System.Net;
using System.Reflection;
using System.Security.Claims;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class SetupSecretRateLimitMetadataTests
{
    [Test]
    public async Task ValidateSecretUsesAnonymousSetupSecretRateLimitPolicy()
    {
        var method = typeof(InstanceOnboardingController).GetMethod(nameof(InstanceOnboardingController.ValidateSecret));

        await Assert.That(method).IsNotNull();
        ArgumentNullException.ThrowIfNull(method);

        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();

        var post = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(post).IsNotNull();
        await Assert.That(post!.Template).IsEqualTo("validate-secret");
        await Assert.That(post.Name).IsEqualTo(RouteNames.ValidateInstanceSetupSecret);

        var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();
        await Assert.That(rateLimit).IsNotNull();
        await Assert.That(rateLimit!.PolicyName).IsEqualTo(RateLimitingExtensions.SetupSecretPolicy);

        AssertProducesProblem(method, StatusCodes.Status410Gone);
        AssertProducesProblem(method, StatusCodes.Status429TooManyRequests);
    }

    [Test]
    public async Task SetupSecretRequiredActionsUseSetupSecretRateLimitPolicy()
    {
        var methods = SetupSecretRequiredActions();

        await Assert.That(methods).IsNotEmpty();

        foreach (var method in methods)
        {
            var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

            await Assert.That(rateLimit)
                .IsNotNull()
                .Because($"{method.Name} is setup-secret gated and must use the setup-secret brute-force limiter.");
            await Assert.That(rateLimit!.PolicyName).IsEqualTo(RateLimitingExtensions.SetupSecretPolicy);
        }
    }

    [Test]
    public async Task AuthorizeRequiredSetupRequest_DoesNotShareAllowAnonymousAttemptBudget()
    {
        await using var host = await SetupRateLimitedApi.StartAsync();

        for (var i = 0; i < 2; i++)
        {
            using var anonymousRequest = CreateAuthenticatedRequest("/setup-anonymous");
            using var anonymousResponse = await host.Client.SendAsync(anonymousRequest);
            await Assert.That(anonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        using var request = CreateAuthenticatedRequest("/setup-authenticated");
        using var response = await host.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var throttledAnonymousRequest = CreateAuthenticatedRequest("/setup-anonymous");
        using var throttledAnonymousResponse = await host.Client.SendAsync(throttledAnonymousRequest);
        await Assert.That(throttledAnonymousResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-User", "onboarding-admin");
        return request;
    }

    [Test]
    public async Task SetupSecretRequiredActionsAdvertiseProblemDetailsForFilterFailures()
    {
        var methods = SetupSecretRequiredActions();

        await Assert.That(methods).IsNotEmpty();

        foreach (var method in methods)
        {
            AssertProducesProblem(method, StatusCodes.Status403Forbidden);
            AssertProducesProblem(method, StatusCodes.Status410Gone);
            AssertProducesProblem(method, StatusCodes.Status429TooManyRequests);
        }
    }

    [Test]
    public async Task CanonicalProviderPatchActionsUseWritePolicyAndAdvertiseRateLimitProblemDetails()
    {
        foreach (var method in CanonicalProviderPatchActions())
        {
            var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

            await Assert.That(rateLimit)
                .IsNotNull()
                .Because($"{method.Name} must retain its write limiter metadata for bearer-admin requests.");
            await Assert.That(rateLimit!.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
            AssertProducesProblem(method, StatusCodes.Status429TooManyRequests);
        }
    }

    [Test]
    public async Task CanonicalProviderGetActionsAdvertiseRateLimitProblemDetailsWithoutNamedRateLimitMetadata()
    {
        foreach (var method in CanonicalProviderGetActions())
        {
            var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>();

            await Assert.That(rateLimit)
                .IsNull()
                .Because($"{method.Name} must rely on the global setup-secret predicate so bearer reads stay outside the setup-secret bucket.");
            AssertProducesProblem(method, StatusCodes.Status429TooManyRequests);
        }
    }

    [Test]
    [Arguments("/api/instance/settings/auth-provider")]
    [Arguments("/api/instance/settings/authz-provider")]
    public async Task CanonicalProviderPatch_WithAuthenticatedSetupIdentity_UsesSetupSecretPolicy(string path)
    {
        var context = CreateContext(HttpMethods.Patch, path, ApiAuthenticationSchemeNames.SetupSecret);

        var policyName = RateLimitingExtensions.InferPolicyName(context, hasRetryAfter: false);

        await Assert.That(policyName).IsEqualTo(RateLimitingExtensions.SetupSecretPolicy);
    }

    [Test]
    [Arguments("GET", "/api/instance/settings/auth-provider")]
    [Arguments("GET", "/api/instance/settings/authz-provider")]
    [Arguments("PATCH", "/api/instance/settings/auth-provider")]
    [Arguments("PATCH", "/api/instance/settings/authz-provider")]
    public async Task CanonicalProviderRoute_WithSetupSecretHeaderAndFailedAuthentication_UsesSetupSecretPolicy(
        string method,
        string path)
    {
        var context = CreateContext(method, path, string.Empty);
        context.Request.Headers["X-Setup-Secret"] = "invalid-setup-secret";

        var policyName = RateLimitingExtensions.InferPolicyName(context, hasRetryAfter: false);

        await Assert.That(policyName).IsEqualTo(RateLimitingExtensions.SetupSecretPolicy);
    }

    [Test]
    [Arguments("/api/instance/settings/auth-provider")]
    [Arguments("/api/instance/settings/authz-provider")]
    public async Task CanonicalProviderPatch_WithAuthenticatedBearerAdmin_UsesWritePolicy(string path)
    {
        var context = CreateContext(HttpMethods.Patch, path, JwtBearerDefaults.AuthenticationScheme);
        context.User.AddIdentity(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin")],
            JwtBearerDefaults.AuthenticationScheme));

        var policyName = RateLimitingExtensions.InferPolicyName(context, hasRetryAfter: false);

        await Assert.That(policyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
    }

    [Test]
    [Arguments("PATCH", "/api/instance/settings/domains", RateLimitingExtensions.WritePolicy)]
    [Arguments("GET", "/api/instance/settings/modules", RateLimitingExtensions.AuthenticatedPolicy)]
    [Arguments("POST", "/api/setup/complete", RateLimitingExtensions.SetupSecretPolicy)]
    public async Task SetupIdentity_OnUnrelatedRequest_PreservesExistingPolicy(
        string method,
        string path,
        string expectedPolicy)
    {
        var context = CreateContext(method, path, ApiAuthenticationSchemeNames.SetupSecret);

        var policyName = RateLimitingExtensions.InferPolicyName(context, hasRetryAfter: false);

        await Assert.That(policyName).IsEqualTo(expectedPolicy);
    }

    private static IReadOnlyList<MethodInfo> SetupSecretRequiredActions()
    {
        return typeof(InstanceOnboardingController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<SetupSecretRequiredAttribute>() is not null)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<MethodInfo> CanonicalProviderPatchActions() =>
    [
        typeof(InstanceAuthenticationSettingsController).GetMethod(nameof(InstanceAuthenticationSettingsController.UpdateAuthProviderConfiguration))
            ?? throw new InvalidOperationException("The auth-provider PATCH action is missing."),
        typeof(InstanceAuthorizationSettingsController).GetMethod(nameof(InstanceAuthorizationSettingsController.UpdateAuthorizationProviderConfiguration))
            ?? throw new InvalidOperationException("The authz-provider PATCH action is missing.")
    ];

    private static IReadOnlyList<MethodInfo> CanonicalProviderGetActions() =>
    [
        typeof(InstanceAuthenticationSettingsController).GetMethod(nameof(InstanceAuthenticationSettingsController.GetAuthProviderConfiguration))
            ?? throw new InvalidOperationException("The auth-provider GET action is missing."),
        typeof(InstanceAuthorizationSettingsController).GetMethod(nameof(InstanceAuthorizationSettingsController.GetAuthorizationProviderConfiguration))
            ?? throw new InvalidOperationException("The authz-provider GET action is missing.")
    ];

    private static DefaultHttpContext CreateContext(
        string method,
        string path,
        string authenticationType)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: authenticationType));
        return context;
    }

    private static void AssertProducesProblem(MethodInfo method, int statusCode)
    {
        var hasProblemMetadata = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Any(attribute => attribute.StatusCode == statusCode && attribute.Type == typeof(ProblemDetails));

        if (!hasProblemMetadata)
        {
            throw new InvalidOperationException(
                $"{method.Name} must advertise ProblemDetails for HTTP {statusCode}.");
        }
    }

    private sealed class SetupRateLimitedApi(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<SetupRateLimitedApi> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:DisableInTesting"] = "false",
                ["RateLimiting:SetupSecret:PermitLimit"] = "2",
                ["RateLimiting:SetupSecret:WindowSeconds"] = "60"
            });
            builder.Services.AddAuthorization();
            builder.Services.AddProblemDetails();
            builder.Services.AddApiRateLimiting(builder.Configuration, builder.Environment);

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-User", out var userId))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", userId.ToString())],
                        authenticationType: "Test"));
                }

                await next(context);
            });
            app.UseRateLimiter();
            app.UseAuthorization();
            app.MapGet("/setup-anonymous", () => Results.Ok())
                .WithMetadata(new SetupSecretRequiredAttribute(), new AllowAnonymousAttribute())
                .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy);
            app.MapGet("/setup-authenticated", () => Results.Ok())
                .WithMetadata(new SetupSecretRequiredAttribute(), new AuthorizeAttribute())
                .RequireRateLimiting(RateLimitingExtensions.SetupSecretPolicy);

            await app.StartAsync();
            return new SetupRateLimitedApi(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
