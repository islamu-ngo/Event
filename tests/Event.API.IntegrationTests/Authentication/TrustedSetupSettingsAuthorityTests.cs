using System.Security.Claims;
using Explore.API.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Infrastructure.Services;
using Event.Api.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class TrustedSetupSettingsAuthorityTests
{
    [Test]
    [Arguments("principal")]
    [Arguments("secret")]
    [Arguments("locked")]
    public async Task ActualSetupAuthenticationCannotOutliveItsOriginalCustody(string changedSource)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var application = factory.Services.CreateAsyncScope();
        var setup = application.ServiceProvider.GetRequiredService<ISetupSecretProvider>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(setup);
        services.AddAuthentication(ApiAuthenticationSchemeNames.SetupSecret)
            .AddScheme<AuthenticationSchemeOptions, SetupSecretAuthenticationHandler>(
                ApiAuthenticationSchemeNames.SetupSecret, _ => { });
        await using var authentication = services.BuildServiceProvider();
        await using var requestScope = authentication.CreateAsyncScope();
        var request = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        request.Request.Method = HttpMethods.Patch;
        request.Request.Path = "/api/instanceonboarding/profile";
        request.Request.Headers["X-Setup-Secret"] = factory.SetupSecret;
        var result = await request.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        request.User = result.Principal!;
        var accessor = new HttpContextAccessor { HttpContext = request };
        try
        {
            var authority = new TrustedSetupSettingsAuthority(accessor, [], setup,
                application.ServiceProvider.GetRequiredService<ILocalBootstrapReceiptSource>(),
                application.ServiceProvider.GetRequiredService<IAuthenticationProviderDispatcher>(),
                application.ServiceProvider.GetServices<IConfiguredAdministratorBootstrapProvider>(), TimeProvider.System);
            var lease = await authority.CaptureAsync(new TrustedSetupSettingsSource(null, null, null),
                TestContext.Current!.Execution.CancellationToken);
            await lease.RequireCurrentAsync(TestContext.Current.Execution.CancellationToken);
            if (changedSource == "principal")
                request.User = new ClaimsPrincipal(request.User.Identities.Select(identity => new ClaimsIdentity(identity)));
            else if (changedSource == "secret")
                request.Request.Headers.Remove("X-Setup-Secret");
            else
                setup.Lock();

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => lease.RequireCurrentAsync(TestContext.Current.Execution.CancellationToken));
        }
        finally { accessor.HttpContext = null; }
    }

    [Test]
    public async Task PublicCallersCannotConstructASetupEffectOrProducer()
    {
        // These types contain executable continuations and serialized settings, not request DTOs.
        // A public constructor would allow arbitrary code to substitute producer provenance.
        await Assert.That(typeof(TrustedSetupSettingsSource).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(typeof(TrustedSetupSettingEffect).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(typeof(TrustedSetupSettingsApply<bool>).GetConstructors().Length).IsEqualTo(0);
    }
}
