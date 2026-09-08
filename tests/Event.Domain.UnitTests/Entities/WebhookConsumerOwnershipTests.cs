using Explore.Domain;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class WebhookConsumerOwnershipTests
{
    [Test]
    public async Task ConsumerKinds_AreExactlyTheFiveSupportedOwnershipScopes()
    {
        var values = Enum.GetValues<WebhookConsumerKind>();

        await Assert.That(values).IsEquivalentTo([
            WebhookConsumerKind.Tenant,
            WebhookConsumerKind.Organization,
            WebhookConsumerKind.Group,
            WebhookConsumerKind.User,
            WebhookConsumerKind.Instance
        ]);
    }

    [Test]
    [Arguments(WebhookConsumerKind.Instance, null, "018f0000-0000-7000-8000-000000000001", null, null, null)]
    [Arguments(WebhookConsumerKind.Tenant, "018f0000-0000-7000-8000-000000000002", null, null, null, null)]
    [Arguments(WebhookConsumerKind.Organization, "018f0000-0000-7000-8000-000000000002", null, "018f0000-0000-7000-8000-000000000003", null, null)]
    [Arguments(WebhookConsumerKind.Group, "018f0000-0000-7000-8000-000000000002", null, null, "018f0000-0000-7000-8000-000000000004", null)]
    [Arguments(WebhookConsumerKind.User, "018f0000-0000-7000-8000-000000000002", null, null, null, "018f0000-0000-7000-8000-000000000005")]
    public async Task Create_AcceptsOneCanonicalOwnerReference(
        WebhookConsumerKind ownerKind,
        string? tenantId,
        string? instanceId,
        string? organizationId,
        string? groupId,
        string? ownerUserId)
    {
        var ownership = WebhookOwnershipScope.Create(
            ownerKind,
            Parse(tenantId),
            Parse(instanceId),
            Parse(organizationId),
            Parse(groupId),
            Parse(ownerUserId));
        var consumer = WebhookConsumer.Create(
            ownership,
            "Accounting integration",
            WebhookProviderMode.Local,
            DomainTestClock.UtcNow);

        await Assert.That(consumer.Id.Version).IsEqualTo(7);
        await Assert.That(consumer.ConsumerKind).IsEqualTo(ownerKind);
        await Assert.That(consumer.OwnerId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task Create_RejectsMixedOrMissingOwnerReferences()
    {
        var tenantId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            WebhookConsumer.Create(WebhookOwnershipScope.Create(
                WebhookConsumerKind.Tenant,
                tenantId,
                Guid.CreateVersion7(),
                null,
                null,
                null),
                "Invalid tenant owner",
                WebhookProviderMode.Local,
                DomainTestClock.UtcNow)));

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            WebhookConsumer.Create(WebhookOwnershipScope.Create(
                WebhookConsumerKind.Organization,
                tenantId,
                null,
                null,
                null,
                null),
                "Missing organization owner",
                WebhookProviderMode.Local,
                DomainTestClock.UtcNow)));
    }

    private static Guid? Parse(string? value) =>
        value is null ? null : Guid.Parse(value);
}
