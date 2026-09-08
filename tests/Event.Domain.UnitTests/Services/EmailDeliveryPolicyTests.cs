
using Explore.Domain.Enums;
using Explore.Domain.Services;

namespace Event.Domain.UnitTests.Services;

public sealed class EmailDeliveryPolicyTests
{
    [Test]
    [Arguments(false, true, true, true, true, EmailDeliveryState.Disabled)]
    [Arguments(false, false, false, false, false, EmailDeliveryState.Disabled)]
    [Arguments(true, false, false, true, true, EmailDeliveryState.Unconfigured)]
    [Arguments(true, true, false, true, true, EmailDeliveryState.Misconfigured)]
    [Arguments(true, false, true, true, true, EmailDeliveryState.Misconfigured)]
    [Arguments(true, true, true, false, true, EmailDeliveryState.Misconfigured)]
    [Arguments(true, true, true, true, false, EmailDeliveryState.Degraded)]
    [Arguments(true, true, true, true, true, EmailDeliveryState.Available)]
    public async Task DeliveryStatePreservesIntentAndFailure(
        bool enabled, bool hasHost, bool hasSender, bool valid, bool authorityAvailable, EmailDeliveryState expected)
    {
        await Assert.That(EmailDeliveryPolicy.Evaluate(enabled, hasHost, hasSender, valid, authorityAvailable))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task TenantTransportRejectsInstanceAndOtherTenantCredentials()
    {
        var tenantId = Guid.NewGuid();
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(tenantId, SecretScope.Instance, null)).IsFalse();
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(tenantId, SecretScope.Tenant, Guid.NewGuid())).IsFalse();
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(tenantId, SecretScope.Tenant, tenantId)).IsTrue();
    }

    [Test]
    public async Task InstanceTransportRejectsTenantCredentialsAndMalformedInstanceScope()
    {
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(null, SecretScope.Tenant, Guid.NewGuid())).IsFalse();
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(null, SecretScope.Instance, Guid.NewGuid())).IsFalse();
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(null, SecretScope.Instance, null)).IsTrue();
        await Assert.That(EmailDeliveryPolicy.CanUseCredential(Guid.Empty, SecretScope.Tenant, Guid.Empty)).IsFalse();
    }
}
