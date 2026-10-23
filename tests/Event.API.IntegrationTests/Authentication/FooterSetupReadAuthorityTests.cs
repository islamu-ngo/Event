using System.Security.Claims;
using System.Security.Cryptography;
using Explore.API.Authentication;
using Explore.API.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class FooterSetupReadAuthorityTests
{
    [Test]
    public async Task ActualSetupAuthenticationIsReadOnlyAndRevocationInvalidatesItsAdmission()
    {
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var secrets = Substitute.For<ISetupSecretProvider>();
        bool completed = false;
        secrets.ValidateSecretAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
            completed ? SetupSecretValidationOutcome.SetupCompleted
                : call.ArgAt<string?>(0) == secret ? SetupSecretValidationOutcome.Accepted : SetupSecretValidationOutcome.Rejected);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(secrets);
        services.AddAuthentication(ApiAuthenticationSchemeNames.SetupSecret)
            .AddScheme<AuthenticationSchemeOptions, SetupSecretAuthenticationHandler>(
                ApiAuthenticationSchemeNames.SetupSecret, _ => { });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = "/api/instance/settings/footer-governance";
        http.Request.Headers["X-Setup-Secret"] = secret;
        var result = await http.AuthenticateAsync();
        await Assert.That(result.Succeeded).IsTrue();
        http.User = result.Principal!;
        var accessor = new HttpContextAccessor { HttpContext = http };
        try
        {
            var authority = new HttpFooterSetupReadAuthority(accessor, secrets);
            var admission = await authority.TryAcquireAsync();
            await Assert.That(admission).IsNotNull();
            await admission!.ValidateAsync();
            completed = true;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => admission.ValidateAsync());
            await Assert.That(await authority.TryAcquireAsync()).IsNull();
        }
        finally { accessor.HttpContext = null; }

        completed = false;
        await using var writeScope = provider.CreateAsyncScope();
        var write = new DefaultHttpContext { RequestServices = writeScope.ServiceProvider };
        write.Request.Method = HttpMethods.Patch;
        write.Request.Path = "/api/instance/settings/footer-governance";
        write.Request.Headers["X-Setup-Secret"] = secret;
        await Assert.That((await write.AuthenticateAsync()).Succeeded).IsFalse();
    }

    [Test]
    public async Task AClaimedSetupIdentityCannotReplaceTheValidatedSecret()
    {
        var secrets = Substitute.For<ISetupSecretProvider>();
        secrets.ValidateSecretAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.Rejected);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())],
                ApiAuthenticationSchemeNames.SetupSecret))
        };
        http.Request.Headers["X-Setup-Secret"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var accessor = new HttpContextAccessor { HttpContext = http };
        try
        {
            await Assert.That(await new HttpFooterSetupReadAuthority(accessor, secrets).TryAcquireAsync()).IsNull();
        }
        finally { accessor.HttpContext = null; }
    }
}
