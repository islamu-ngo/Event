using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Domain;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Contracts.Admissions;

/// <summary>The included contact bound survives source PII deletion alongside its encrypted copy.</summary>
public sealed record AdmissionContactDeliveryPayload(DateTime? DisclosureUntilUtc, string Ciphertext)
{
    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public Guid? AccountUserId { get; init; }

    public string GetProtectionPurpose() =>
        $"contact:{DisclosureUntilUtc?.ToString("O", CultureInfo.InvariantCulture)}:{AccountUserId:N}";

    public static AdmissionContactDeliveryPayload Read(string material, int protectionVersion)
    {
        // Version 1 is the unbounded, nonanonymous delivery format.
        if (protectionVersion == 1) return new(null, material);
        if (protectionVersion != 2)
            throw new InvalidOperationException("Admission contact protection version is invalid.");
        try
        {
            var payload = JsonSerializer.Deserialize<AdmissionContactDeliveryPayload>(material, StrictJson);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Ciphertext) ||
                payload.AccountUserId == Guid.Empty ||
                payload.AccountUserId.HasValue && payload.DisclosureUntilUtc.HasValue ||
                !payload.AccountUserId.HasValue && payload.DisclosureUntilUtc is not { Kind: DateTimeKind.Utc })
                throw new JsonException();
            return payload;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Admission contact payload is invalid.", exception);
        }
    }

    public void RequireDisclosure(RegistrationOrder? order, DateTime utcNow, string? currentAccountEmail = null)
    {
        if (AccountUserId is { } userId)
        {
            if (order?.AccountUserId != userId || string.IsNullOrWhiteSpace(currentAccountEmail))
                throw new InvalidOperationException("Admission account contact authority is invalid.");
            return;
        }
        if (order is null ||
            AnonymousRegistrationRetentionPolicy.AppliesTo(order) && DisclosureUntilUtc is null ||
            !AnonymousRegistrationRetentionPolicy.CanDisclose(order, DisclosureUntilUtc, utcNow))
            throw new AdmissionContactRetentionExpiredException();
        RequireUnexpired(DisclosureUntilUtc, utcNow);
    }

    public static void RequireUnexpired(DateTime? deadline, DateTime utcNow)
    {
        if (deadline is { } bound && utcNow >= bound)
            throw new AdmissionContactRetentionExpiredException();
    }

    public override string ToString() => "AdmissionContactDeliveryPayload(<redacted>)";
}

public sealed class AdmissionContactRetentionExpiredException() : InvalidOperationException("registration_data_retention_expired");
