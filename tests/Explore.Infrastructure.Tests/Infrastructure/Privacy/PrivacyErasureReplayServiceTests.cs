using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services.Privacy;
using NSubstitute;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure.Privacy;

public sealed class PrivacyErasureReplayServiceTests
{
    [Test]
    public async Task ReplayAsync_UsesApplicationErasureBoundaryAndCancellationToken()
    {
        IPrivacyErasureService erasure =
            Substitute.For<IPrivacyErasureService>();
        using var cancellation = new CancellationTokenSource();
        var service = new PrivacyErasureReplayService(erasure);

        await service.ReplayAsync(cancellation.Token);

        await erasure.Received(1).ReplayPendingAsync(cancellation.Token);
    }

    [Test]
    public async Task ReplayAsync_ContinuityOrIntegrityFailure_FailsClosed()
    {
        IPrivacyErasureService erasure =
            Substitute.For<IPrivacyErasureService>();
        erasure.ReplayPendingAsync(Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("sequence gap"));
        var service = new PrivacyErasureReplayService(erasure);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReplayAsync(CancellationToken.None));
    }
}
