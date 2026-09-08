using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class FeatureFlagClientServiceTests
{
    [Test]
    public async Task LoadFlagsAsync_WithSuccessfulResponse_HydratesFeatureState()
    {
        var api = Substitute.For<IFeaturesClient>();
        api.GetMyFeatureFlagsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, bool>
            {
                ["beta-dashboard"] = true,
                ["legacy-flow"] = false
            });
        var featureState = new FeatureStateContainer();
        var service = CreateService(api, featureState);

        await service.LoadFlagsAsync();

        await Assert.That(featureState.IsEnabled("beta-dashboard")).IsTrue();
        await Assert.That(featureState.IsEnabled("legacy-flow")).IsFalse();
        await api.Received(1).GetMyFeatureFlagsAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadFlagsAsync_WithUnauthorizedResponse_LeavesExistingFlagsUnchanged()
    {
        var api = Substitute.For<IFeaturesClient>();
        api.GetMyFeatureFlagsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IDictionary<string, bool>>>(_ => throw new ApiException(
                "Unauthorized",
                401,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));
        var featureState = new FeatureStateContainer();
        featureState.SetFlags(new Dictionary<string, bool> { ["existing"] = true });
        var service = CreateService(api, featureState);

        await service.LoadFlagsAsync();

        await Assert.That(featureState.IsEnabled("existing")).IsTrue();
        await Assert.That(featureState.All.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LoadFlagsAsync_WithTransportFailure_DoesNotThrowOrClearFlags()
    {
        var api = Substitute.For<IFeaturesClient>();
        api.GetMyFeatureFlagsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<IDictionary<string, bool>>>(_ => throw new HttpRequestException("network failed"));
        var featureState = new FeatureStateContainer();
        featureState.SetFlags(new Dictionary<string, bool> { ["existing"] = true });
        var service = CreateService(api, featureState);

        await service.LoadFlagsAsync();

        await Assert.That(featureState.IsEnabled("existing")).IsTrue();
        await Assert.That(featureState.All.Count).IsEqualTo(1);
    }

    private static FeatureFlagClientService CreateService(
        IFeaturesClient api,
        FeatureStateContainer featureState) =>
        new(api, featureState, NullLogger<FeatureFlagClientService>.Instance);
}
