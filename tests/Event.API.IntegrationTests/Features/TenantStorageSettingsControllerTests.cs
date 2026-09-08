using System.Reflection;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models.Common;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
[Category("TenantStorageSettings")]
public sealed class TenantStorageSettingsControllerTests
{
    [Test]
    public async Task GetStorageSettings_ReturnsMediatorSettings()
    {
        var mediator = Substitute.For<IMediator>();
        var assembler = Substitute.For<IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>>();
        var settings = new TenantStorageSettingsDto
        {
            Provider = StorageProviders.Local,
            MaxUploadBytes = 4096,
            TenantQuotaBytes = 8192
        };
        var halResource = new HalResource<TenantStorageSettingsDto>(settings);
        mediator.Send(Arg.Any<GetTenantStorageSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(settings);
        assembler.ToResource(settings, Arg.Any<HttpContext>()).Returns(halResource);
        var controller = CreateController(mediator, storageSettingsAssembler: assembler);

        var result = await controller.GetStorageSettings(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await mediator.Received(1).Send(Arg.Any<GetTenantStorageSettingsQuery>(), Arg.Any<CancellationToken>());
        await assembler.Received(1).ToResource(settings, Arg.Any<HttpContext>());
    }

    [Test]
    public async Task PatchStorageSettings_UsesPatchRouteAndOperationName()
    {
        var action = typeof(TenantStorageSettingsController)
            .GetMethod(nameof(TenantStorageSettingsController.PatchStorageSettings))!;
        var route = action.GetCustomAttribute<HttpPatchAttribute>();

        await Assert.That(route).IsNotNull();
        await Assert.That(route!.Template).IsEqualTo(string.Empty);
        await Assert.That(route.Name).IsEqualTo(RouteNames.PatchTenantStorageSettings);
        await Assert.That(action.GetCustomAttribute<HttpPutAttribute>()).IsNull();
        await Assert.That(typeof(TenantStorageSettingsController).GetMethods()
            .Any(method => method.GetCustomAttribute<HttpPutAttribute>() is not null)).IsFalse();
    }

    [Test]
    public async Task PatchStorageSettings_WhenMediatorSucceeds_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<PatchTenantStorageSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                Guid.NewGuid(),
                "Tenant storage settings patched successfully."));
        var controller = CreateController(mediator, userId);

        var result = await controller.PatchStorageSettings(CreatePatch(), CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var response = ok!.Value as BaseCommandResponse<Guid>;
        await Assert.That(response).IsNotNull();
        await Assert.That(response!.IsSuccess).IsTrue();
        await mediator.Received(1).Send(
            Arg.Is<PatchTenantStorageSettingsCommand>(command => command != null && command.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchStorageSettings_WhenMediatorReportsPolicyFailure_ReturnsBadRequest()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<PatchTenantStorageSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "StorageTenantOverridesLocked",
                "Tenant storage settings are locked by instance policy."));
        var controller = CreateController(mediator, Guid.NewGuid());

        var result = await controller.PatchStorageSettings(CreatePatch(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(400);
        var problemDetails = objectResult.Value as ValidationProblemDetails;
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Extensions["code"]).IsEqualTo("StorageTenantOverridesLocked");
    }

    [Test]
    public async Task PatchStorageSettings_WhenMediatorReportsAdminFailure_ReturnsForbidden()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<PatchTenantStorageSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Authorization<Guid>(
                "Only tenant administrators or instance administrators can update tenant storage settings."));
        var controller = CreateController(mediator, Guid.NewGuid());

        var result = await controller.PatchStorageSettings(CreatePatch(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task TestStorageConnection_ReturnsMediatorProviderStatus()
    {
        var mediator = Substitute.For<IMediator>();
        var expected = new InstanceStorageProviderStatusDto
        {
            IsAvailable = true,
            Preflight = new S3PreflightResult { IsSuccess = true }
        };
        mediator.Send(Arg.Any<TestTenantStorageProviderQuery>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateController(mediator).TestStorageConnection(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok?.Value).IsSameReferenceAs(expected);
        await mediator.Received(1).Send(
            Arg.Any<TestTenantStorageProviderQuery>(),
            Arg.Any<CancellationToken>());
    }

    private static TenantStorageSettingsController CreateController(
        IMediator mediator,
        Guid? userId = null,
        IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>? storageSettingsAssembler = null)
    {
        var resolvedUserId = userId ?? Guid.NewGuid();
        var userContext = Substitute.For<IUserContext>();
        userContext.UserId.Returns(resolvedUserId);
        var services = new ServiceCollection()
            .AddSingleton(userContext)
            .BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("internal_user_id", resolvedUserId.ToString("D"))],
            authenticationType: "Test"));

        return new TenantStorageSettingsController(
            mediator,
            storageSettingsAssembler ?? Substitute.For<IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = principal
                }
            }
        };
    }

    private static PatchTenantStorageSettingsDto CreatePatch()
        => new()
        {
            Policy = new PatchTenantStoragePolicyDto
            {
                MaxUploadBytes = OptionalUpdate<long>.Set(4096)
            }
        };
}
