namespace Explore.Blazor.Client.Helpers;

public static class RegistrationOrderRecovery
{
    public static string Guidance(string? statusCode) => statusCode?.ToUpperInvariant() switch
    {
        "DRAFT" => "Choose your tickets to begin registration.",
        "AWAITING_IDENTITY" => "Confirm your identity to continue registration.",
        "AWAITING_PARTICIPANT_DETAILS" => "Add the required participant details to continue.",
        "AWAITING_REQUIREMENTS" => "Complete the remaining registration requirements.",
        "READY_FOR_CHECKOUT" => "Review your order and complete checkout.",
        "CONFIRMED" => "Your registration is confirmed.",
        "AWAITING_PAYMENT" => "Payment is required before this registration can be confirmed.",
        "AWAITING_APPROVAL" => "This registration is waiting for organizer approval.",
        "WAITLISTED" => "This registration is waitlisted. The organizer will contact you if space becomes available.",
        "REJECTED" => "This registration was not approved.",
        "EXPIRED" => "The reservation expired before it was completed.",
        "CANCELLED" => "This registration order was cancelled.",
        "NEEDS_RECONCILIATION" => "This registration needs organizer review before it can continue.",
        _ => "Review the order details and complete the next available step."
    };

    public static string? Countdown(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null)
        {
            return null;
        }

        var remaining = expiresAt.Value - now;
        return remaining <= TimeSpan.Zero
            ? "This reservation has expired."
            : $"Reservation expires in {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}.";
    }
}
