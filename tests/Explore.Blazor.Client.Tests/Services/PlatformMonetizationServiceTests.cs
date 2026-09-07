using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class PlatformMonetizationServiceTests
{
    [Test]
    public async Task GetAndUpdate_DelegateGeneratedContractsWithCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var resource = new HalResourceOfPlatformMonetizationSettingsDto();
        var request = new UpdatePlatformMonetizationSettingsDto
        {
            FeeEnabled = true,
            ExpectedFeeVersion = 4,
            ExpectedContributionVersion = 7
        };
        var response = new BaseCommandResponseOfGuid { Success = true };
        var apiClient = Substitute.For<IPlatformMonetizationSettingsClient>();
        apiClient.GetInstancePlatformMonetizationSettingsAsync(null, null, cancellation.Token).Returns(resource);
        apiClient.UpdateInstancePlatformMonetizationSettingsAsync(request, null, null, cancellation.Token).Returns(response);
        var service = new PlatformMonetizationService(apiClient);

        var loaded = await service.GetAsync(cancellation.Token);
        var updated = await service.UpdateAsync(request, cancellation.Token);

        await Assert.That(loaded).IsSameReferenceAs(resource);
        await Assert.That(updated).IsSameReferenceAs(response);
        await apiClient.Received(1).GetInstancePlatformMonetizationSettingsAsync(null, null, cancellation.Token);
        await apiClient.Received(1).UpdateInstancePlatformMonetizationSettingsAsync(request, null, null, cancellation.Token);
    }
}
