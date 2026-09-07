using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding.Queries;

public sealed class DownloadAuthorizationPolicyPackageQueryHandlerTests
{
    private readonly IPolicyPackageService _policyPackageService = Substitute.For<IPolicyPackageService>();
    private readonly DownloadAuthorizationPolicyPackageQueryHandler _handler;

    public DownloadAuthorizationPolicyPackageQueryHandlerTests()
    {
        _handler = new DownloadAuthorizationPolicyPackageQueryHandler(_policyPackageService);
    }

    [Test]
    public async Task Handle_ReturnsArchiveFromPackageService()
    {
        var manifest = new PolicyPackageManifest(
            PackageId: "islamuevent-authorization-policies",
            Version: "abc123",
            ContentHash: "abc123",
            GeneratedAt: DateTimeOffset.UtcNow,
            Artifacts: []);
        var archive = new PolicyPackageArchive(
            FileName: "islamuevent-authorization-policies.zip",
            ContentType: "application/zip",
            Content: new byte[] { 1, 2, 3 },
            Manifest: manifest);
        _policyPackageService.ExportArchiveAsync(Arg.Any<CancellationToken>()).Returns(archive);

        var result = await _handler.Handle(new DownloadAuthorizationPolicyPackageQuery(), CancellationToken.None);

        await Assert.That(result).IsSameReferenceAs(archive);
        await _policyPackageService.Received(1).ExportArchiveAsync(Arg.Any<CancellationToken>());
    }
}
