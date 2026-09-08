namespace Explore.Persistence.Repositories;

internal static class RegistrationInventoryTime
{
    public static void RequireUtc(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "Expiry time must be UTC.",
                nameof(utcNow));
        }
    }
}
