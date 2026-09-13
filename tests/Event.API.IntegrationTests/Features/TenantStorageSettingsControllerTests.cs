using System.Reflection;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models.Common;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
[Category("TenantStorageSettings")]
public sealed class TenantStorageSettingsControllerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    public TenantStorageSettingsControllerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ExploreDbContext>(options => options.UseInMemoryDatabase(Guid.CreateVersion7().ToString()));
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddSingleton(Substitute.For<IAuthorizationProvider>());
        services.AddNativeOperations([typeof(ResolveCurrentUserIdByIdentityRequest), typeof(ResolveCurrentUserIdByIdentityRequestHandler)]);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _scope = _provider.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
    [Test]
    public async Task GetStorageSettings_ReturnsQuerySettings()
    {
        var query = Substitute.For<IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>>();
        var assembler = Substitute.For<IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>>();
        var settings = new TenantStorageSettingsDto
        {
            Provider = StorageProviders.Local,
            MaxUploadBytes = 4096,
            TenantQuotaBytes = 8192
        };
        var halResource = new HalResource<TenantStorageSettingsDto>(settings);
        query.QueryAsync(Arg.Any<GetTenantStorageSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(settings);
        assembler.ToResource(settings, Arg.Any<HttpContext>()).Returns(halResource);
        var controller = CreateController(settingsQuery: query, storageSettingsAssembler: assembler);

        var result = await controller.GetStorageSettings(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await query.Received(1).QueryAsync(Arg.Any<GetTenantStorageSettingsQuery>(), Arg.Any<CancellationToken>());
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
    public async Task PatchStorageSettings_WhenCommandSucceeds_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var command = Substitute.For<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>();
        command.ExecuteAsync(Arg.Any<PatchTenantStorageSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                Guid.NewGuid(),
                "Tenant storage settings patched successfully."));
        var controller = CreateController(userId: userId, patchSettings: command);

        var result = await controller.PatchStorageSettings(CreatePatch(), CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var response = ok!.Value as BaseCommandResponse<Guid>;
        await Assert.That(response).IsNotNull();
        await Assert.That(response!.IsSuccess).IsTrue();
        await command.Received(1).ExecuteAsync(
            Arg.Is<PatchTenantStorageSettingsCommand>(command => command != null && command.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchStorageSettings_WhenCommandReportsPolicyFailure_ReturnsBadRequest()
    {
        var command = Substitute.For<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>();
        command.ExecuteAsync(Arg.Any<PatchTenantStorageSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure<Guid>(
                "StorageTenantOverridesLocked",
                "Tenant storage settings are locked by instance policy."));
        var controller = CreateController(patchSettings: command);

        var result = await controller.PatchStorageSettings(CreatePatch(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(400);
        var problemDetails = objectResult.Value as ValidationProblemDetails;
        await Assert.That(problemDetails).IsNotNull();
        await Assert.That(problemDetails!.Extensions["code"]).IsEqualTo("StorageTenantOverridesLocked");
    }

    [Test]
    public async Task PatchStorageSettings_WhenCommandReportsAdminFailure_ReturnsForbidden()
    {
        var command = Substitute.For<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>();
        command.ExecuteAsync(Arg.Any<PatchTenantStorageSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Authorization<Guid>(
                "Only tenant administrators or instance administrators can update tenant storage settings."));
        var controller = CreateController(patchSettings: command);

        var result = await controller.PatchStorageSettings(CreatePatch(), CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task TestStorageConnection_ReturnsCommandProviderStatus()
    {
        var command = Substitute.For<ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>>();
        var expected = new InstanceStorageProviderStatusDto
        {
            IsAvailable = true,
            Preflight = new S3PreflightResult { IsSuccess = true }
        };
        command.ExecuteAsync(Arg.Any<TestTenantStorageProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateController(testProvider: command).TestStorageConnection(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok?.Value).IsSameReferenceAs(expected);
        await command.Received(1).ExecuteAsync(
            Arg.Any<TestTenantStorageProviderCommand>(),
            Arg.Any<CancellationToken>());
    }

    private TenantStorageSettingsController CreateController(
        Guid? userId = null,
        IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>? settingsQuery = null,
        ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>? patchSettings = null,
        ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>? testProvider = null,
        IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>? storageSettingsAssembler = null)
    {
        var resolvedUserId = userId ?? Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("internal_user_id", resolvedUserId.ToString("D"))],
            authenticationType: "Test"));

        return new TenantStorageSettingsController(
            settingsQuery ?? Substitute.For<IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>>(),
            patchSettings ?? Substitute.For<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>(),
            testProvider ?? Substitute.For<ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>>(),
            _scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            storageSettingsAssembler ?? Substitute.For<IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
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
