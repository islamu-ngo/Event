namespace Explore.Application.Features.EventSessionCustomProperties;

internal static class SessionCustomPropertyCache
{
    public static string ListsByTenant(Guid tenantId) => $"session-custom-properties:lists:tenant:{tenantId:N}";

    public static string ListsBySession(Guid tenantId, Guid sessionId) => $"{ListsByTenant(tenantId)}:session:{sessionId:N}";

    public static string ListKey(Guid tenantId, Guid sessionId, int page, int size) =>
        $"{ListsBySession(tenantId, sessionId)}:{page}:{size}";
}
