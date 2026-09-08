// ABOUTME: API contract tests for manual authorization policy package ZIP download endpoints.
// ABOUTME: Verifies setup/admin controllers return archive file responses through the provider-neutral query.

using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class AuthorizationPolicyPackageDownloadControllerTests
{
    [Test]
    public async Task SetupDownloadAuthorizationPolicyPackage_ReturnsZipArchiveFile()
    {
        var archive = CreateArchive();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>())
            .Returns(archive);
        var controller = new InstanceOnboardingController(
            mediator,
            Substitute.For<ISetupSecretProvider>(),
            Substitute.For<IInstanceBootstrapAuditLogger>(),
            Substitute.For<IAuthProviderConfigurationService>(),
            Substitute.For<ILogger<InstanceOnboardingController>>(),
            Substitute.For<IResourceAssembler<InstanceOnboardingStatusDto, InstanceOnboardingStatusDto>>(),
            Substitute.For<IVisitorAccessCapabilityResolver>(),
            Substitute.For<Explore.Application.Contracts.Infrastructure.ITenantContext>());

        IActionResult result = await controller.DownloadAuthorizationPolicyPackage(CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("application/zip");
        await Assert.That(file.FileDownloadName).IsEqualTo("authorization-policy-package.zip");
        await Assert.That(file.FileContents).IsEquivalentTo(archive.Content.ToArray());
        await mediator.Received(1).Send(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AdminDownloadAuthorizationPolicyPackage_WhenInstanceAdmin_ReturnsZipArchiveFile()
    {
        var archive = CreateArchive();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>())
            .Returns(archive);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        var controller = new InstanceAuthorizationSettingsController(
            mediator,
            Substitute.For<IAuthorizationProviderConfigurationService>(),
            adminContext,
            Substitute.For<ISetupSecretProvider>());

        IActionResult result = await controller.DownloadAuthorizationPolicyPackage(CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("application/zip");
        await Assert.That(file.FileDownloadName).IsEqualTo("authorization-policy-package.zip");
        await Assert.That(file.FileContents).IsEquivalentTo(archive.Content.ToArray());
        await mediator.Received(1).Send(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AdminDownloadAuthorizationPolicyPackage_WhenNotInstanceAdmin_ReturnsForbidden()
    {
        var mediator = Substitute.For<IMediator>();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(false);
        var controller = new InstanceAuthorizationSettingsController(
            mediator,
            Substitute.For<IAuthorizationProviderConfigurationService>(),
            adminContext,
            setupSecretProvider)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        IActionResult result = await controller.DownloadAuthorizationPolicyPackage(CancellationToken.None);

        var objectResult = result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await mediator.DidNotReceive().Send(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>());
    }

    private static PolicyPackageArchive CreateArchive()
    {
        var manifest = new PolicyPackageManifest(
            "test-policy-package",
            "1.0.0",
            "0123456789abcdef",
            DateTimeOffset.UtcNow,
            []);

        return new PolicyPackageArchive(
            "authorization-policy-package.zip",
            "application/zip",
            new byte[] { 1, 2, 3 },
            manifest);
    }
}
