using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AuthorizationPolicyPackageDownloadControllerTests
{
    [Test]
    public async Task SetupDownloadAuthorizationPolicyPackage_ReturnsZipArchiveFile()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var archive = CreateArchive();
        var downloadQueryHandler = Substitute.For<IQueryHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive>>();
        downloadQueryHandler.QueryAsync(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>())
            .Returns(archive);
        var controller = new InstanceOnboardingController(
            Substitute.For<IQueryHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto>>(),
            Substitute.For<IQueryHandler<GetOnboardingPreflightQuery, OnboardingPreflightDto>>(),
            Substitute.For<IQueryHandler<GetAuthorizationProviderConfigurationQuery, AuthorizationProviderConfigurationDto>>(),
            downloadQueryHandler,
            Substitute.For<ICommandHandler<SaveInstanceOnboardingProfileCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<CompleteInstanceOnboardingCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<CompleteLocalInstanceOnboardingCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<BootstrapKeycloakRealmCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<VerifyCerbosEndpointCommand, BaseCommandResponse<Guid>>>(),
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            scope.ServiceProvider.GetRequiredService<IQueryHandler<GetLocalIdentityLifecycleCapabilitiesQuery, LocalIdentityLifecycleCapabilities>>(),
            Substitute.For<ISetupSecretProvider>(),
            Substitute.For<IInstanceBootstrapAuditLogger>(),
            Substitute.For<IAuthProviderConfigurationService>(),
            Substitute.For<ILogger<InstanceOnboardingController>>(),
            Substitute.For<IResourceAssembler<InstanceOnboardingStatusDto, InstanceOnboardingStatusDto>>(),
            Substitute.For<IVisitorAccessCapabilityResolver>(),
            Substitute.For<Explore.Application.Contracts.Infrastructure.ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<IQueryHandler<GetInstanceOnboardingJourneyQuery, InstanceOnboardingJourneyDto>>(),
            scope.ServiceProvider.GetRequiredService<IResourceAssembler<InstanceOnboardingJourneyDto, InstanceOnboardingJourneyDto>>());

        IActionResult result = await controller.DownloadAuthorizationPolicyPackage(CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("application/zip");
        await Assert.That(file.FileDownloadName).IsEqualTo("authorization-policy-package.zip");
        await Assert.That(file.FileContents).IsEquivalentTo(archive.Content.ToArray());
        await downloadQueryHandler.Received(1).QueryAsync(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AdminDownloadAuthorizationPolicyPackage_WhenInstanceAdmin_ReturnsZipArchiveFile()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var archive = CreateArchive();
        var downloadQueryHandler = Substitute.For<IQueryHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive>>();
        downloadQueryHandler.QueryAsync(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>())
            .Returns(archive);
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        var controller = new InstanceAuthorizationSettingsController(
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            Substitute.For<IAuthorizationProviderConfigurationService>(),
            Substitute.For<IQueryHandler<GetAuthorizationProviderConfigurationQuery, AuthorizationProviderConfigurationDto>>(),
            Substitute.For<ICommandHandler<UpdateAuthorizationProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<UpdateAuthorizationProviderConfigurationCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>>>(),
            downloadQueryHandler,
            Substitute.For<IQueryHandler<GetAuthorizationPolicyPackageStatusQuery, AuthorizationPolicyPackageStatusDto>>(),
            adminContext,
            Substitute.For<ISetupSecretProvider>());

        IActionResult result = await controller.DownloadAuthorizationPolicyPackage(CancellationToken.None);

        var file = result as FileContentResult;
        await Assert.That(file).IsNotNull();
        await Assert.That(file!.ContentType).IsEqualTo("application/zip");
        await Assert.That(file.FileDownloadName).IsEqualTo("authorization-policy-package.zip");
        await Assert.That(file.FileContents).IsEquivalentTo(archive.Content.ToArray());
        await downloadQueryHandler.Received(1).QueryAsync(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AdminDownloadAuthorizationPolicyPackage_WhenNotInstanceAdmin_ReturnsForbidden()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var downloadQueryHandler = Substitute.For<IQueryHandler<DownloadAuthorizationPolicyPackageQuery, PolicyPackageArchive>>();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(false);
        var controller = new InstanceAuthorizationSettingsController(
            scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            Substitute.For<IAuthorizationProviderConfigurationService>(),
            Substitute.For<IQueryHandler<GetAuthorizationProviderConfigurationQuery, AuthorizationProviderConfigurationDto>>(),
            Substitute.For<ICommandHandler<UpdateAuthorizationProviderConfigurationDuringSetupCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<UpdateAuthorizationProviderConfigurationCommand, BaseCommandResponse<Guid>>>(),
            Substitute.For<ICommandHandler<SyncAuthorizationPolicyPackageCommand, BaseCommandResponse<Guid>>>(),
            downloadQueryHandler,
            Substitute.For<IQueryHandler<GetAuthorizationPolicyPackageStatusQuery, AuthorizationPolicyPackageStatusDto>>(),
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
        await downloadQueryHandler.DidNotReceive().QueryAsync(Arg.Any<DownloadAuthorizationPolicyPackageQuery>(), Arg.Any<CancellationToken>());
    }

    private static PolicyPackageArchive CreateArchive()
    {
        var manifest = new PolicyPackageManifest(
            "test-policy-package",
            "1.0.0",
            "0123456789abcdef",
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
            []);

        return new PolicyPackageArchive(
            "authorization-policy-package.zip",
            "application/zip",
            new byte[] { 1, 2, 3 },
            manifest);
    }
}
