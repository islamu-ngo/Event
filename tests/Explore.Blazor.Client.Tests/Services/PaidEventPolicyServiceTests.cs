using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class PaidEventPolicyServiceTests
{
    private readonly IInstancePaidEventPolicySettingsClient _instanceClient = Substitute.For<IInstancePaidEventPolicySettingsClient>();
    private readonly ITenantPaidEventPolicySettingsClient _tenantClient = Substitute.For<ITenantPaidEventPolicySettingsClient>();
    private readonly PaidEventPolicyService _service;

    public PaidEventPolicyServiceTests()
    {
        _service = new PaidEventPolicyService(_instanceClient, _tenantClient);
    }

    [Test]
    public async Task GetInstanceAsync_DelegatesExactGeneratedMethod()
    {
        using var cancellation = new CancellationTokenSource();
        var resource = new HalResourceOfPaidEventPolicyDto();
        _instanceClient.GetInstancePaidEventPolicySettingsAsync(null, null, cancellation.Token).Returns(resource);

        var result = await _service.GetInstanceAsync(cancellation.Token);

        await Assert.That(result).IsSameReferenceAs(resource);
        await _instanceClient.Received(1).GetInstancePaidEventPolicySettingsAsync(null, null, cancellation.Token);
    }

    [Test]
    public async Task UpdateInstanceAsync_DelegatesExactGeneratedMethod()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new RevisePaidEventPolicyDto();
        var response = new BaseCommandResponseOfGuid();
        _instanceClient.UpdateInstancePaidEventPolicySettingsAsync(request, null, null, cancellation.Token).Returns(response);

        var result = await _service.UpdateInstanceAsync(request, cancellation.Token);

        await Assert.That(result).IsSameReferenceAs(response);
        await _instanceClient.Received(1).UpdateInstancePaidEventPolicySettingsAsync(request, null, null, cancellation.Token);
    }

    [Test]
    public async Task GetTenantAsync_DelegatesExactGeneratedMethod()
    {
        using var cancellation = new CancellationTokenSource();
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var resource = new HalResourceOfTenantPaidEventPolicyConfigurationDto();
        _tenantClient.GetTenantPaidEventPolicySettingsAsync(tenantId, null, null, cancellation.Token).Returns(resource);

        var result = await _service.GetTenantAsync(tenantId, cancellation.Token);

        await Assert.That(result).IsSameReferenceAs(resource);
        await _tenantClient.Received(1).GetTenantPaidEventPolicySettingsAsync(tenantId, null, null, cancellation.Token);
    }

    [Test]
    public async Task UpdateTenantAsync_DelegatesExactGeneratedMethod()
    {
        using var cancellation = new CancellationTokenSource();
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var request = new RevisePaidEventPolicyDto();
        var response = new BaseCommandResponseOfGuid();
        _tenantClient.UpdateTenantPaidEventPolicySettingsAsync(tenantId, request, null, null, cancellation.Token).Returns(response);

        var result = await _service.UpdateTenantAsync(tenantId, request, cancellation.Token);

        await Assert.That(result).IsSameReferenceAs(response);
        await _tenantClient.Received(1).UpdateTenantPaidEventPolicySettingsAsync(tenantId, request, null, null, cancellation.Token);
    }

    [Test]
    public async Task BrowserPaidPolicyContracts_OmitServerOnlyIdentifiers()
    {
        string[] policyProperties = typeof(PaidEventPolicyDto).GetProperties().Select(property => property.Name).ToArray();
        string[] configurationProperties = typeof(TenantPaidEventPolicyConfigurationDto).GetProperties().Select(property => property.Name).ToArray();

        await Assert.That(policyProperties).DoesNotContain("Id");
        await Assert.That(policyProperties).DoesNotContain("TenantId");
        await Assert.That(configurationProperties).DoesNotContain("TenantId");
    }
}
