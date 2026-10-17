using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EventDiscoveryIdentityCorrectionDispatcherTests
{
    [Test]
    public async Task AdmittedCorrectionPreservesReceiptIdentityForDelivery()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var service = Substitute.For<IEventDiscoveryIdentityCorrectionNotificationService>();
        Guid deliveredId = Guid.Empty;
        EventDiscoveryIdentityCorrectionRequested? delivered = null;
        service.DeliverAsync(Arg.Any<Guid>(), Arg.Any<EventDiscoveryIdentityCorrectionRequested>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                deliveredId = call.Arg<Guid>();
                delivered = call.Arg<EventDiscoveryIdentityCorrectionRequested>();
                return Task.CompletedTask;
            });
        var dispatcher = new EventDiscoveryIdentityCorrectionDispatcher(
            service, Substitute.For<INotificationOutboxExecutionAuthority>());
        OutboxMessage message = CreateMessage(tenantId, eventId);

        await dispatcher.DispatchAsync(message);

        await Assert.That(deliveredId).IsEqualTo(message.Id);
        var receipt = delivered ?? throw new InvalidOperationException("No correction was delivered.");
        await Assert.That(receipt.TenantId).IsEqualTo(tenantId);
        await Assert.That(receipt.EventId).IsEqualTo(eventId);
    }

    [Test]
    public async Task UnknownPayloadFieldsFailBeforeDelivery()
    {
        var service = Substitute.For<IEventDiscoveryIdentityCorrectionNotificationService>();
        bool delivered = false;
        service.DeliverAsync(Arg.Any<Guid>(), Arg.Any<EventDiscoveryIdentityCorrectionRequested>(), Arg.Any<CancellationToken>())
            .Returns(_ => { delivered = true; return Task.CompletedTask; });
        var dispatcher = new EventDiscoveryIdentityCorrectionDispatcher(
            service, Substitute.For<INotificationOutboxExecutionAuthority>());
        OutboxMessage message = CreateMessage(Guid.CreateVersion7(), Guid.CreateVersion7());
        message.Payload = message.Payload![..^1] + ",\"PrivateCounterpartHint\":\"must-not-pass\"}";

        await Assert.That(() => dispatcher.DispatchAsync(message))
            .Throws<JsonException>();
        await Assert.That(delivered).IsFalse();
    }

    private static OutboxMessage CreateMessage(Guid tenantId, Guid eventId)
    {
        var request = new EventDiscoveryIdentityCorrectionRequested(
            tenantId,
            eventId,
            Guid.CreateVersion7(),
            4,
            "same-offering",
            "same_program");
        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(EventDiscoveryIdentity),
            AggregateId = eventId,
            EventType = EventDiscoveryIdentityCorrectionRequested.EventType,
            Payload = JsonSerializer.Serialize(request),
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 5
        };
    }

}
