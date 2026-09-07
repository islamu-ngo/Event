using Explore.Domain;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookConsumerProviderBindingSecurityBoundaryTests
{
    [Test]
    public async Task ApplicationUid_UsesInstanceAndConsumerAndSeparatesConsumersInOneTenant()
    {
        var tenantId = Guid.CreateVersion7();
        var instanceId = Guid.CreateVersion7();
        var firstConsumerId = Guid.CreateVersion7();
        var secondConsumerId = Guid.CreateVersion7();
        var profile = CreateProfile();

        var first = WebhookConsumerProviderBinding.CreatePending(
            tenantId,
            firstConsumerId,
            instanceId,
            "self-hosted",
            profile,
            WebhookProviderCapability.AppPortal);
        var second = WebhookConsumerProviderBinding.CreatePending(
            tenantId,
            secondConsumerId,
            instanceId,
            "self-hosted",
            profile,
            WebhookProviderCapability.AppPortal);

        await Assert.That(first.ApplicationUid)
            .IsEqualTo($"islamu-{instanceId:N}-consumer-{firstConsumerId:N}");
        await Assert.That(second.ApplicationUid)
            .IsEqualTo($"islamu-{instanceId:N}-consumer-{secondConsumerId:N}");
        await Assert.That(first.ApplicationUid).IsNotEqualTo(second.ApplicationUid);
        await Assert.That(first.ApplicationUid).IsNotEqualTo($"islamu-tenant-{tenantId:N}");
    }

    [Test]
    public async Task RepairAndVerifyOwnership_RecomputesIdentityAndRejectsConsumerSubstitution()
    {
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var initialInstanceId = Guid.CreateVersion7();
        var canonicalInstanceId = Guid.CreateVersion7();
        var profile = CreateProfile();
        var binding = WebhookConsumerProviderBinding.CreatePending(
            tenantId,
            consumerId,
            initialInstanceId,
            "self-hosted",
            profile,
            WebhookProviderCapability.AppPortal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            binding.RepairAndVerifyOwnership(
                canonicalInstanceId,
                tenantId,
                Guid.CreateVersion7(),
                "app_substituted",
                profile,
                WebhookProviderCapability.AppPortal,
                DomainTestClock.UtcNowOffset)));

        binding.RepairAndVerifyOwnership(
            canonicalInstanceId,
            tenantId,
            consumerId,
            "app_verified",
            profile,
            WebhookProviderCapability.AppPortal,
            DomainTestClock.UtcNowOffset);

        await Assert.That(binding.InstanceId).IsEqualTo(canonicalInstanceId);
        await Assert.That(binding.ApplicationUid)
            .IsEqualTo(WebhookConsumerProviderBinding.CreateApplicationUid(canonicalInstanceId, consumerId));
        await Assert.That(binding.IsVerifiedFor(tenantId, consumerId)).IsTrue();
        await Assert.That(binding.ConcurrencyVersion).IsEqualTo(2);
        await Assert.That(binding.VerificationFence).IsEqualTo(2);
    }

    private static WebhookProviderCapabilityProfile CreateProfile() =>
        WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix,
            "1.96.1",
            WebhookProviderCapability.AppPortal,
            "svix-self-hosted-1.96.1-v1",
            DomainTestClock.UtcNowOffset);
}
