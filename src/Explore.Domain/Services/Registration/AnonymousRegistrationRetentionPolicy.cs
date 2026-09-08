using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class AnonymousRegistrationRetentionPolicy
{
    public const int DefaultDays = 7;
    public const int MinimumDays = 0;
    public const int MaximumDays = 30;

    public static DateTime ResolveInitialDeadline(DateTimeOffset? lastSessionEndUtc, int retentionDays)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, MinimumDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retentionDays, MaximumDays);
        if (lastSessionEndUtc is not { } end || end.UtcDateTime == default ||
            end.UtcDateTime > DateTime.MaxValue.AddDays(-retentionDays))
        {
            throw new ArgumentException("Anonymous retention requires a finite event end.", nameof(lastSessionEndUtc));
        }

        return end.UtcDateTime.AddDays(retentionDays);
    }

    public static bool AppliesTo(RegistrationOrder order) =>
        order.AnonymousPiiRetentionUntilUtc.HasValue ||
        order.GuestAccessTokenHash is not null &&
        order.ParticipationSnapshot.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged &&
        order.ParticipationSnapshot.IdentityAccessModeId is
            (int)IdentityAccessModeEnum.GuestAllowed or (int)IdentityAccessModeEnum.CapabilityTokenAllowed;

    public static bool CanDisclose(RegistrationOrder order, DateTime? rowRetentionUntilUtc, DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Disclosure time must be UTC.", nameof(utcNow));

        return !AppliesTo(order) || order.AnonymousPiiRetentionUntilUtc is { } bound &&
            utcNow < bound && (!rowRetentionUntilUtc.HasValue || utcNow < rowRetentionUntilUtc.Value);
    }

    public static DateTime? GetDisclosureDeadline(RegistrationOrder order, DateTime? rowRetentionUntilUtc)
    {
        if (!AppliesTo(order)) return null;
        DateTime bound = order.AnonymousPiiRetentionUntilUtc
            ?? throw new InvalidOperationException("The original anonymous retention bound is unavailable.");
        return rowRetentionUntilUtc is { } row && row < bound ? row : bound;
    }
}
