namespace Explore.Blazor.Client.Tests.Services;

public sealed class TenantReportingIntakePolicyServiceTests
{
    private readonly ITenantReportingIntakeSettingsClient _apiClient = Substitute.For<ITenantReportingIntakeSettingsClient>();

    [Test]
    public async Task GetAsync_UsesTenantPolicyOperationWithExplicitApiVersion()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new HalResourceOfTenantReportingIntakePolicyDto { Enabled = true };
        _apiClient.GetTenantReportingIntakePolicyAsync(
                "1.0",
                null,
                cancellation.Token)
            .Returns(expected);
        var service = new TenantReportingIntakePolicyService(_apiClient);

        var actual = await service.GetAsync(cancellation.Token);

        await Assert.That(actual).IsSameReferenceAs(expected);
        await _apiClient.Received(1).GetTenantReportingIntakePolicyAsync(
            "1.0",
            null,
            cancellation.Token);
    }

    [Test]
    public async Task UpdateAsync_UsesExactEnabledPayloadAndExplicitApiVersion()
    {
        using var cancellation = new CancellationTokenSource();
        _apiClient.UpdateTenantReportingIntakePolicyAsync(
                Arg.Any<UpdateTenantReportingIntakePolicyDto>(),
                "1.0",
                null,
                cancellation.Token)
            .Returns(new BaseCommandResponseOfGuid());
        var service = new TenantReportingIntakePolicyService(_apiClient);

        await service.UpdateAsync(false, cancellation.Token);

        await _apiClient.Received(1).UpdateTenantReportingIntakePolicyAsync(
            Arg.Is<UpdateTenantReportingIntakePolicyDto>(
                request => request != null && request.Enabled == false),
            "1.0",
            null,
            cancellation.Token);
    }
}
