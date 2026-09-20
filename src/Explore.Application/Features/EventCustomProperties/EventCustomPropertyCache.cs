namespace Explore.Application.Features.EventCustomProperties;

internal static class EventCustomPropertyCache
{
    public static string ListsByTenant(Guid tenantId) => $"event-custom-properties:lists:tenant:{tenantId:N}";

    public static string ListsByEvent(Guid tenantId, Guid eventId) => $"{ListsByTenant(tenantId)}:event:{eventId:N}";

    public static string ListKey(Guid tenantId, Guid eventId, int page, int size) =>
        $"{ListsByEvent(tenantId, eventId)}:{page}:{size}";
}
