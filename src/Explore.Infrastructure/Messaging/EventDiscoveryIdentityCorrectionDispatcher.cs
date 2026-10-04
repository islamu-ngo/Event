using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Discovery.Commands;
using Explore.Domain;

namespace Explore.Infrastructure.Messaging;

public sealed class EventDiscoveryIdentityCorrectionDispatcher(
    IEventDiscoveryIdentityCorrectionNotificationService notificationService,
    ITenantContextAccessor tenantContextAccessor)
{
    public const string EventType = EventDiscoveryIdentityCorrectionRequested.EventType;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task DispatchAsync(
        OutboxMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.EventType != EventType || string.IsNullOrWhiteSpace(message.Payload))
        {
            throw new InvalidOperationException("Discovery identity correction outbox message is invalid.");
        }

        EventDiscoveryIdentityCorrectionRequested request =
            JsonSerializer.Deserialize<EventDiscoveryIdentityCorrectionRequested>(
                message.Payload,
                SerializerOptions)
            ?? throw new InvalidOperationException("Discovery identity correction payload is invalid.");
        if (message.AggregateId != request.EventId)
        {
            throw new InvalidOperationException("Discovery identity correction aggregate does not match its payload.");
        }

        Guid? previousTenantId = tenantContextAccessor.TenantId;
        tenantContextAccessor.SetTenant(request.TenantId);
        try
        {
            await notificationService.DeliverAsync(message.Id, request, cancellationToken);
        }
        finally
        {
            if (previousTenantId is { } tenantId)
            {
                tenantContextAccessor.SetTenant(tenantId);
            }
            else
            {
                tenantContextAccessor.Clear();
            }
        }
    }
}
