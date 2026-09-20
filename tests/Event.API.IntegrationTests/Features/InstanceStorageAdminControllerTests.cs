using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class InstanceStorageAdminControllerTests : IDisposable
{
    private readonly IdentityQueryTestScope _identity = new();
    public void Dispose() => _identity.Dispose();
    [Test]
    public async Task GetStorageSettings_WhenInstanceAdmin_ReturnsHalResource()
    {
        var instanceStorageQuery = Substitute.For<IQueryHandler<GetInstanceStorageSettingsQuery, InstanceStorageSettingsDto>>();
        var adminContext = Substitute.For<IAdminContext>();
        var assembler = Substitute.For<IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto>>();
        var settings = new InstanceStorageSettingsDto
        {
            Provider = StorageProviders.Local,
            DefaultMaxUploadBytes = 4096,
            DefaultTenantQuotaBytes = 8192
        };
        var halResource = new HalResource<InstanceStorageSettingsDto>(settings);
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        instanceStorageQuery.QueryAsync(Arg.Any<GetInstanceStorageSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(settings);
        assembler.ToResource(settings, Arg.Any<HttpContext>()).Returns(halResource);
        var controller = CreateController(adminContext, storageSettingsAssembler: assembler, instanceStorageQuery: instanceStorageQuery);

        var result = await controller.GetStorageSettings(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(halResource);
        await assembler.Received(1).ToResource(settings, Arg.Any<HttpContext>());
    }

    [Test]
    public async Task TestStorageConnection_WhenInstanceAdmin_ReturnsProviderStatus()
    {
        var testStorageHandler = Substitute.For<IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto>>();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        testStorageHandler.QueryAsync(Arg.Any<TestInstanceStorageProviderQuery>(), Arg.Any<CancellationToken>())
            .Returns(new InstanceStorageProviderStatusDto
            {
                Provider = StorageProviders.Local,
                IsAvailable = true,
                SupportsServerSideStreaming = true,
                SupportsBrowserDirectUpload = false
            });
        var controller = CreateController(adminContext, testStorageHandler: testStorageHandler);

        var result = await controller.TestStorageConnection(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var status = ok!.Value as InstanceStorageProviderStatusDto;
        await Assert.That(status).IsNotNull();
        await Assert.That(status!.Provider).IsEqualTo(StorageProviders.Local);
        await Assert.That(status.IsAvailable).IsTrue();
        await testStorageHandler.Received(1).QueryAsync(Arg.Any<TestInstanceStorageProviderQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TestStorageConnection_WhenNotInstanceAdmin_ReturnsForbidden()
    {
        var testStorageHandler = Substitute.For<IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto>>();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(false);
        var controller = CreateController(adminContext, setupSecretProvider, testStorageHandler: testStorageHandler);

        var result = await controller.TestStorageConnection(CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await testStorageHandler.DidNotReceive().QueryAsync(Arg.Any<TestInstanceStorageProviderQuery>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecalculateStorageUsage_WhenInstanceAdmin_ReturnsUsageSummary()
    {
        var recalculateStorageHandler = Substitute.For<ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto>>();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(true);
        recalculateStorageHandler.ExecuteAsync(Arg.Any<RecalculateInstanceStorageUsageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new InstanceStorageUsageDto
            {
                UsedBytes = 4096,
                ReservedBytes = 1024,
                ObjectCount = 2
            });
        var controller = CreateController(adminContext, recalculateStorageHandler: recalculateStorageHandler);

        var result = await controller.RecalculateStorageUsage(CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var usage = ok!.Value as InstanceStorageUsageDto;
        await Assert.That(usage).IsNotNull();
        await Assert.That(usage!.UsedBytes).IsEqualTo(4096);
        await Assert.That(usage.ReservedBytes).IsEqualTo(1024);
        await Assert.That(usage.ObjectCount).IsEqualTo(2);
        await recalculateStorageHandler.Received(1).ExecuteAsync(Arg.Any<RecalculateInstanceStorageUsageCommand>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecalculateStorageUsage_WhenNotInstanceAdmin_ReturnsForbidden()
    {
        var recalculateStorageHandler = Substitute.For<ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto>>();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(false);
        var controller = CreateController(adminContext, setupSecretProvider, recalculateStorageHandler: recalculateStorageHandler);

        var result = await controller.RecalculateStorageUsage(CancellationToken.None);

        var objectResult = result.Result as ObjectResult;
        await Assert.That(objectResult).IsNotNull();
        await Assert.That(objectResult!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await recalculateStorageHandler.DidNotReceive().ExecuteAsync(Arg.Any<RecalculateInstanceStorageUsageCommand>(), Arg.Any<CancellationToken>());
    }

    private InstanceStorageSettingsController CreateController(
        IAdminContext adminContext,
        ISetupSecretProvider? setupSecretProvider = null,
        IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto>? storageSettingsAssembler = null,
        IQueryHandler<GetInstanceStorageSettingsQuery, InstanceStorageSettingsDto>? instanceStorageQuery = null,
        ICommandHandler<UpdateInstanceStorageSettingsCommand, BaseCommandResponse<Guid>>? updateStorageHandler = null,
        IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto>? testStorageHandler = null,
        ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto>? recalculateStorageHandler = null)
    {
        return new InstanceStorageSettingsController(
            _identity.Query,
            storageSettingsAssembler ?? Substitute.For<IResourceAssembler<InstanceStorageSettingsDto, InstanceStorageSettingsDto>>(),
            instanceStorageQuery ?? Substitute.For<IQueryHandler<GetInstanceStorageSettingsQuery, InstanceStorageSettingsDto>>(),
            updateStorageHandler ?? Substitute.For<ICommandHandler<UpdateInstanceStorageSettingsCommand, BaseCommandResponse<Guid>>>(),
            testStorageHandler ?? Substitute.For<IQueryHandler<TestInstanceStorageProviderQuery, InstanceStorageProviderStatusDto>>(),
            recalculateStorageHandler ?? Substitute.For<ICommandHandler<RecalculateInstanceStorageUsageCommand, InstanceStorageUsageDto>>(),
            adminContext,
            setupSecretProvider ?? Substitute.For<ISetupSecretProvider>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
