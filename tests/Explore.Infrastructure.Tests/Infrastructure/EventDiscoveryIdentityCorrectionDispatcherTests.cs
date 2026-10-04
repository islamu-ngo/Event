using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;
using Explore.Infrastructure.Messaging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EventDiscoveryIdentityCorrectionDispatcherTests
{
    [Test]
    public async Task DispatchBindsPayloadTenantAndRestoresThePreviousScope()
    {
        Guid previousTenantId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        var service = Substitute.For<IEventDiscoveryIdentityCorrectionNotificationService>();
        var tenant = new RecordingTenantContextAccessor(previousTenantId);
        var dispatcher = new EventDiscoveryIdentityCorrectionDispatcher(service, tenant);
        OutboxMessage message = CreateMessage(tenantId, eventId);

        await dispatcher.DispatchAsync(message);

        await service.Received(1).DeliverAsync(
            message.Id,
            Arg.Is<EventDiscoveryIdentityCorrectionRequested>(request =>
                request != null
                && request.TenantId == tenantId
                && request.EventId == eventId),
            Arg.Any<CancellationToken>());
        await Assert.That(tenant.ObservedTenants).IsEquivalentTo([tenantId, previousTenantId]);
        await Assert.That(tenant.TenantId).IsEqualTo(previousTenantId);
    }

    [Test]
    public async Task UnknownPayloadFieldsFailBeforeDelivery()
    {
        var service = Substitute.For<IEventDiscoveryIdentityCorrectionNotificationService>();
        var dispatcher = new EventDiscoveryIdentityCorrectionDispatcher(
            service,
            new RecordingTenantContextAccessor(null));
        OutboxMessage message = CreateMessage(Guid.CreateVersion7(), Guid.CreateVersion7());
        message.Payload = message.Payload![..^1] + ",\"PrivateCounterpartHint\":\"must-not-pass\"}";

        await Assert.That(() => dispatcher.DispatchAsync(message))
            .Throws<JsonException>();
        await service.DidNotReceiveWithAnyArgs()
            .DeliverAsync(default, default!, default);
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

    private sealed class RecordingTenantContextAccessor(Guid? tenantId) : ITenantContextAccessor
    {
        public List<Guid> ObservedTenants { get; } = [];
        public Guid? TenantId { get; private set; } = tenantId;
        public bool IsResolved => TenantId.HasValue;

        public void SetTenant(Guid value)
        {
            TenantId = value;
            ObservedTenants.Add(value);
        }

        public void Clear() => TenantId = null;
    }
}
