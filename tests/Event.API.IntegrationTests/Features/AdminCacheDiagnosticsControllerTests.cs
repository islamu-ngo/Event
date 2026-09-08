using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Explore.API.Controllers;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class AdminCacheDiagnosticsControllerTests
{
    [Test]
    public async Task SnapshotInDevelopmentUsesCanonicalResolutionAndOwnedDiagnosticClaimNames()
    {
        Guid subject = Guid.CreateVersion7();
        Guid internalUser = Guid.CreateVersion7();
        var controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, subject.ToString("D")),
            new Claim(PlatformIdentityClaimTypes.InternalUserId, internalUser.ToString("D"))
        ], "interactive")));

        ActionResult<AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics> result =
            await controller.SnapshotCurrentUser(
                Substitute.For<IMediator>(),
                DevelopmentEnvironment(),
                EnabledConfiguration(),
                CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        var diagnostics = ok?.Value as AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics;
        await Assert.That(diagnostics).IsNotNull();
        await Assert.That(diagnostics!.ResolvedUserId).IsEqualTo(subject);
        await Assert.That(diagnostics.SubjectClaim).IsEqualTo(subject.ToString("D"));
        await Assert.That(diagnostics.InternalUserIdClaim).IsEqualTo(internalUser.ToString("D"));
    }

    [Test]
    public async Task SnapshotForPurposeBoundPrincipalDoesNotMasqueradeAsPlatformIdentity()
    {
        Guid smuggledUser = Guid.CreateVersion7();
        var controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, smuggledUser.ToString("D"))
        ], ApiAuthenticationSchemeNames.ApiKey)));

        ActionResult<AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics> result =
            await controller.SnapshotCurrentUser(
                Substitute.For<IMediator>(),
                DevelopmentEnvironment(),
                EnabledConfiguration(),
                CancellationToken.None);

        var diagnostics = (result.Result as OkObjectResult)?.Value
            as AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics;
        await Assert.That(diagnostics).IsNotNull();
        await Assert.That(diagnostics!.SubjectClaim).IsEqualTo(smuggledUser.ToString("D"));
        await Assert.That(diagnostics.ResolvedUserId).IsNull();
    }

    [Test]
    public async Task SnapshotOutsideDevelopmentOrTestingStaysNotFound()
    {
        var controller = CreateController(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString("D"))],
            "interactive")));
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Production");

        ActionResult<AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics> result =
            await controller.SnapshotCurrentUser(
                Substitute.For<IMediator>(),
                environment,
                EnabledConfiguration(),
                CancellationToken.None);

        var notFound = result.Result as ObjectResult;
        await Assert.That(notFound?.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
    }

    private static AdminCacheDiagnosticsController CreateController(ClaimsPrincipal user)
    {
        return new AdminCacheDiagnosticsController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };
    }

    private static IWebHostEnvironment DevelopmentEnvironment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return environment;
    }

    private static IConfiguration EnabledConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Diagnostics:EnableAdminCacheInvalidation"] = "true"
            })
            .Build();
}
