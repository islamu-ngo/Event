using Event.Web.BffHosting.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using TUnit.Core;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class SafeAuthDiagnosticsPolicyTests
{
    [Test]
    public async Task BuildLoginRedirectUrl_DoesNotExposeRawFailureDetails()
    {
        var policy = new SafeAuthDiagnosticsPolicy();
        var diagnostic = policy.CreateDiagnostic(
            "oidc_remote_failure",
            new InvalidOperationException("raw provider failure with secretLen=24 and clientId=islamu-event-blazor"));

        var redirectUrl = policy.BuildLoginRedirectUrl("/setup", "keycloak", diagnostic);

        await Assert.That(redirectUrl).Contains("challengeError=1");
        await Assert.That(redirectUrl).Contains("errorCode=oidc_remote_failure");
        await Assert.That(redirectUrl).Contains("correlationId=");
        await Assert.That(redirectUrl).DoesNotContain("errorDetail");
        await Assert.That(redirectUrl).DoesNotContain("secretLen");
        await Assert.That(redirectUrl).DoesNotContain("clientId");
        await Assert.That(redirectUrl).DoesNotContain("raw provider failure");
    }

    [Test]
    public async Task RemoteFailure_RedirectsWithSafeCodeAndNoSecretDerivedQueryValues()
    {
        var options = CreateKeycloakOptions();
        var events = options.Events;
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ISafeAuthDiagnosticsPolicy, SafeAuthDiagnosticsPolicy>()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        var failure = new InvalidOperationException(
            "token exchange failed for clientId=islamu-event-blazor secretLen=18",
            new InvalidOperationException("inner secretPrefix=supe"));
        var failureContext = new RemoteFailureContext(
            httpContext,
            new("keycloak", "Keycloak", typeof(OpenIdConnectHandler)),
            options,
            failure);

        await events.RemoteFailure(failureContext);

        var redirectUrl = httpContext.Response.Headers.Location.ToString();
        await Assert.That(redirectUrl).Contains("/login?");
        await Assert.That(redirectUrl).Contains("challengeError=1");
        await Assert.That(redirectUrl).Contains("errorCode=oidc_remote_failure");
        await Assert.That(redirectUrl).Contains("correlationId=");
        await Assert.That(redirectUrl).DoesNotContain("errorDetail");
        await Assert.That(redirectUrl).DoesNotContain("clientId");
        await Assert.That(redirectUrl).DoesNotContain("secretLen");
        await Assert.That(redirectUrl).DoesNotContain("secretPrefix");
        await Assert.That(redirectUrl).DoesNotContain("super-secret-value");
        await Assert.That(redirectUrl).DoesNotContain("token exchange failed");
    }

    [Test]
    public async Task KeycloakOptions_RequestStandardOidcScopesWithoutOfflineAccess()
    {
        var options = CreateKeycloakOptions();

        await Assert.That(options.Scope).Contains("openid");
        await Assert.That(options.Scope).Contains("profile");
        await Assert.That(options.Scope).Contains("email");
        await Assert.That(options.Scope).DoesNotContain("offline_access");
    }

    [Test]
    public async Task KeycloakOptions_HonorExplicitHttpMetadataOverrideInProduction()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var factory = new EventBffOidcOptionsFactory(environment, new SafeAuthDiagnosticsPolicy());

        var options = factory.CreateKeycloakOptions(new EventBffOidcProviderOptions(
            "http://idp.example.test/realms/ISLAMU",
            "islamu-event-blazor",
            "super-secret-value",
            RequireHttpsMetadata: false));

        await Assert.That(options.RequireHttpsMetadata).IsFalse();
    }

    private static OpenIdConnectOptions CreateKeycloakOptions()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        var factory = new EventBffOidcOptionsFactory(environment, new SafeAuthDiagnosticsPolicy());

        return factory.CreateKeycloakOptions(new EventBffOidcProviderOptions(
            "https://idp.example.test/realms/ISLAMU",
            "islamu-event-blazor",
            "super-secret-value"));
    }
}
