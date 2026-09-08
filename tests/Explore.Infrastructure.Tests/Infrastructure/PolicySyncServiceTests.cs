using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public class PolicySyncServiceTests
{
    [Test]
    public async Task SyncRolePoliciesAsync_DelegatesToPackagePublisher()
    {
        var packageService = Substitute.For<IPolicyPackageService>();
        packageService.PublishAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSuccessfulPublishResult());
        var service = CreateService(packageService);

        await service.SyncRolePoliciesAsync(42, CancellationToken.None);

        await packageService.Received(1).PublishAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SyncAllPoliciesAsync_DelegatesToPackagePublisher()
    {
        var packageService = Substitute.For<IPolicyPackageService>();
        packageService.PublishAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSuccessfulPublishResult());
        var service = CreateService(packageService);

        await service.SyncAllPoliciesAsync(CancellationToken.None);

        await packageService.Received(1).PublishAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReloadAllInstancesAsync_DelegatesToPackagePublisher()
    {
        var packageService = Substitute.For<IPolicyPackageService>();
        packageService.PublishAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSuccessfulPublishResult());
        var service = CreateService(packageService);

        await service.ReloadAllInstancesAsync(CancellationToken.None);

        await packageService.Received(1).PublishAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetPolicySummaryAsync_UsesPackageManifest()
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var packageService = Substitute.For<IPolicyPackageService>();
        packageService.BuildManifestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PolicyPackageManifest(
                PackageId: "test-package",
                Version: "abc123",
                ContentHash: "abc123",
                GeneratedAt: generatedAt,
                Artifacts:
                [
                    new PolicyPackageArtifact("islamuevent_event.yaml", PolicyArtifactKind.Policy, "hash1", 10, new Dictionary<string, string>()),
                    new PolicyPackageArtifact("_schemas/islamuevent_event.json", PolicyArtifactKind.Schema, "hash2", 20, new Dictionary<string, string>())
                ])));
        var service = CreateService(packageService);

        var summary = await service.GetPolicySummaryAsync(CancellationToken.None);

        await Assert.That(summary.PolicyCount).IsEqualTo(1);
        await Assert.That(summary.ContentHash).IsEqualTo("abc123");
        await Assert.That(summary.GeneratedAt).IsEqualTo(generatedAt);
        await Assert.That(summary.RoleCount).IsEqualTo(0);
        await Assert.That(summary.TotalPermissionCount).IsEqualTo(0);
    }

    [Test]
    public async Task PublishFailure_DoesNotThrowToRoleMutationCallers()
    {
        var packageService = Substitute.For<IPolicyPackageService>();
        packageService.PublishAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PolicyPackagePublishResult(
                Succeeded: false,
                PackageId: "test-package",
                ContentHash: "abc123",
                Message: "safe failure",
                PublishedAt: DateTimeOffset.UtcNow,
                Warnings: ["safe warning"])));
        var service = CreateService(packageService);

        await service.SyncRolePoliciesAsync(42, CancellationToken.None);

        await packageService.Received(1).PublishAsync(Arg.Any<CancellationToken>());
    }

    private static PolicySyncService CreateService(IPolicyPackageService packageService)
    {
        return new PolicySyncService(
            packageService,
            Substitute.For<ILogger<PolicySyncService>>());
    }

    private static Task<PolicyPackagePublishResult> CreateSuccessfulPublishResult()
    {
        return Task.FromResult(new PolicyPackagePublishResult(
            Succeeded: true,
            PackageId: "test-package",
            ContentHash: "abc123",
            Message: "published",
            PublishedAt: DateTimeOffset.UtcNow,
            Warnings: []));
    }
}
