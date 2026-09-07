using Explore.Domain.Enums;

namespace Explore.Persistence.Repositories;

internal static class CustomPropertyExposureScope
{
    internal static ExposureLevel[] VisibleAtOrBelow(ExposureLevel ceiling) =>
        ceiling switch
        {
            ExposureLevel.Public => [ExposureLevel.Public],
            ExposureLevel.TenantAdminOnly => [ExposureLevel.Public, ExposureLevel.TenantAdminOnly],
            ExposureLevel.OrganizerOnly => [ExposureLevel.Public, ExposureLevel.TenantAdminOnly, ExposureLevel.OrganizerOnly],
            ExposureLevel.Internal => [ExposureLevel.Public, ExposureLevel.TenantAdminOnly, ExposureLevel.OrganizerOnly, ExposureLevel.Internal],
            _ => [ExposureLevel.Public]
        };
}
