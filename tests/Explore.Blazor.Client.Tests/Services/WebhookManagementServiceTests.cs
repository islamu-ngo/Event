using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using Explore.Blazor.Client.Services.Webhooks;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class WebhookManagementServiceTests
{
    private readonly IWebhooksClient _webhooksClient = Substitute.For<IWebhooksClient>();
    private readonly IWebhookEndpointsClient _endpointsClient = Substitute.For<IWebhookEndpointsClient>();
    private readonly IWebhookMessagesClient _messagesClient = Substitute.For<IWebhookMessagesClient>();
    private readonly WebhookManagementService _service;

    public WebhookManagementServiceTests()
    {
        _service = new WebhookManagementService(
            _webhooksClient,
            _endpointsClient,
            _messagesClient,
            Substitute.For<ILogger<WebhookManagementService>>());
        ConfigureEmptySnapshot();
    }

    [Test]
    public async Task GetSnapshotAsync_ForEveryOwnerKind_ForwardsExactNormalizedScope()
    {
        var organizationId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        WebhookOwnerSelection[] owners =
        [
            WebhookOwnerSelection.Tenant,
            WebhookOwnerSelection.ForOrganization(organizationId),
            WebhookOwnerSelection.ForGroup(groupId),
            WebhookOwnerSelection.User,
            WebhookOwnerSelection.Instance
        ];

        foreach (var owner in owners)
        {
            _webhooksClient.ClearReceivedCalls();
            _endpointsClient.ClearReceivedCalls();
            _messagesClient.ClearReceivedCalls();

            var snapshot = await _service.GetSnapshotAsync(owner);

            await Assert.That(snapshot.IsSuccess).IsTrue();
            await _webhooksClient.Received(1).GetWebhookConsumersAsync(
                owner.OwnerKindId,
                owner.OwnerId,
                200,
                null,
                null,
                Arg.Any<CancellationToken>());
            await _endpointsClient.Received(1).GetWebhookEndpointsAsync(
                owner.OwnerKindId,
                owner.OwnerId,
                null,
                200,
                null,
                null,
                Arg.Any<CancellationToken>());
            await _messagesClient.Received(1).GetWebhookMessagesAsync(
                owner.OwnerKindId,
                owner.OwnerId,
                100,
                null,
                null,
                Arg.Any<CancellationToken>());
            await _messagesClient.Received(1).GetWebhookDeliveryAttemptsAsync(
                owner.OwnerKindId,
                owner.OwnerId,
                null,
                null,
                100,
                null,
                null,
                Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public async Task GetDeliveryAttemptsAsync_ForOrganization_ForwardsOwnerAndFiltersTogether()
    {
        var organizationId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var endpointId = Guid.CreateVersion7();
        var owner = WebhookOwnerSelection.ForOrganization(organizationId);

        var attempts = await _service.GetDeliveryAttemptsAsync(
            owner,
            messageId,
            endpointId,
            limit: 37);

        await Assert.That(attempts.Count).IsEqualTo(0);
        await _messagesClient.Received(1).GetWebhookDeliveryAttemptsAsync(
            (int)WebhookOwnerKind.Organization,
            organizationId,
            messageId,
            endpointId,
            37,
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetSnapshotAsync_MapsSensitiveTabsOnlyFromMessageCollectionRelations()
    {
        var messages = new HalCollectionResourceOfWebhookMessageDto();
        GeneratedHalLinkTestHelper.SetLinks(
            messages,
            (WebhookClientLinkRelations.ProviderPublications, "/api/webhooks/provider-publications", "GET"),
            (WebhookClientLinkRelations.BulkReplayPreview, "/api/webhooks/bulk-replays/preview", "GET"),
            (WebhookClientLinkRelations.BulkReplays, "/api/webhooks/bulk-replays", "GET"));
        _messagesClient.GetWebhookMessagesAsync(
                Arg.Any<int?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(messages));

        var snapshot = await _service.GetSnapshotAsync(WebhookOwnerSelection.Tenant);

        await Assert.That(snapshot.CanViewProviderPublications).IsTrue();
        await Assert.That(snapshot.CanUseBulkReplay).IsTrue();
    }

    private void ConfigureEmptySnapshot()
    {
        _webhooksClient.GetWebhookEventTypesAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ICollection<WebhookEventTypeDto>>([]));
        _webhooksClient.GetWebhookConsumersAsync(
                Arg.Any<int?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfWebhookConsumerDto()));
        _endpointsClient.GetWebhookEndpointsAsync(
                Arg.Any<int?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfWebhookEndpointDto()));
        _messagesClient.GetWebhookMessagesAsync(
                Arg.Any<int?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfWebhookMessageDto()));
        _messagesClient.GetWebhookDeliveryAttemptsAsync(
                Arg.Any<int?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalCollectionResourceOfWebhookDeliveryAttemptDto()));
    }
}
