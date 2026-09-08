using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantStorageSettings.Handlers.Queries;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Explore.Application.Models.Storage;
using NSubstitute;

namespace Event.Application.UnitTests.Features.TenantStorageSettings.Queries;

public sealed class TestTenantStorageProviderQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IAdminContext _adminContext = Substitute.For<IAdminContext>();
    private readonly ITenantStorageSettingService _storageService = Substitute.For<ITenantStorageSettingService>();

    public TestTenantStorageProviderQueryHandlerTests()
    {
        _tenantContext.TenantId.Returns(TenantId);
    }

    [Test]
    public async Task Handle_WhenTenantAdmin_ReturnsProviderStatus()
    {
        var expected = new InstanceStorageProviderStatusDto
        {
            IsAvailable = true,
            Preflight = new S3PreflightResult { IsSuccess = true }
        };
        _adminContext.IsTenantAdminAsync(TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _storageService.TestProviderAsync(TenantId, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateHandler().Handle(new TestTenantStorageProviderQuery(), CancellationToken.None);

        await Assert.That(result).IsSameReferenceAs(expected);
        await _storageService.Received(1).TestProviderAsync(TenantId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_WhenNotAdministrator_ThrowsAuthorizationException()
    {
        _adminContext.IsTenantAdminAsync(TenantId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            CreateHandler().Handle(new TestTenantStorageProviderQuery(), CancellationToken.None));
        await _storageService.DidNotReceive().TestProviderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private TestTenantStorageProviderQueryHandler CreateHandler() =>
        new(_tenantContext, _adminContext, _storageService);
}
