using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Domain;

namespace Explore.Infrastructure.Payments.Stripe;

internal static class StripeConnectReadinessMapper
{
    public static OrganizerPaymentProviderReadiness MapAccountUpdated(
        global::Stripe.Account account,
        string? requestId,
        DateTime observedAt)
    {
        string? country = NormalizeUpper(account.Country, 2);
        string? currency = NormalizeUpper(account.DefaultCurrency, 3);
        string? cardPayments = account.Capabilities?.CardPayments;
        string? transfers = account.Capabilities?.Transfers;
        global::Stripe.AccountRequirements? requirements = account.Requirements;

        return new OrganizerPaymentProviderReadiness(
            HasJsonProperty(account.RawJsonElement, "charges_enabled") && account.ChargesEnabled,
            MapCapability(cardPayments),
            MapCapability(transfers),
            MapRequirements(requirements),
            BoundedRequirementKeys(requirements?.CurrentlyDue),
            BoundedRequirementKeys(requirements?.EventuallyDue),
            BoundedRequirementKeys(requirements?.PastDue),
            StripeConnectAccountAdapter.BoundedText(requirements?.DisabledReason, 120),
            country,
            currency is null ? [] : [currency],
            observedAt,
            StripeConnectAccountAdapter.BoundedText(requestId, 120) ?? $"stripe-account:{account.Id}");
    }

    public static OrganizerPaymentProviderReadiness MapDeauthorized(
        string accountId,
        string eventId,
        DateTime observedAt) => new(
            false,
            OrganizerPaymentProviderCapabilityState.Unknown,
            OrganizerPaymentProviderCapabilityState.Unknown,
            OrganizerPaymentProviderRequirementsState.Unknown,
            [],
            [],
            [],
            null,
            null,
            [],
            observedAt,
            StripeConnectAccountAdapter.BoundedText(eventId, 120) ?? $"stripe-account:{accountId}");

    public static ChargeCapabilityState MapDomainChargeReadiness(OrganizerPaymentProviderReadiness readiness) =>
        OrganizerPaymentReadinessMapper.MapChargeReadiness(readiness);

    public static ProviderRequirementsState MapDomainRequirements(OrganizerPaymentProviderRequirementsState state) =>
        OrganizerPaymentReadinessMapper.MapRequirements(state);

    private static OrganizerPaymentProviderCapabilityState MapCapability(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "active" => OrganizerPaymentProviderCapabilityState.Active,
        "pending" => OrganizerPaymentProviderCapabilityState.Pending,
        "inactive" => OrganizerPaymentProviderCapabilityState.Inactive,
        _ => OrganizerPaymentProviderCapabilityState.Unknown
    };

    private static OrganizerPaymentProviderRequirementsState MapRequirements(global::Stripe.AccountRequirements? requirements)
    {
        if (requirements is null)
        {
            return OrganizerPaymentProviderRequirementsState.Unknown;
        }

        if (!string.IsNullOrWhiteSpace(requirements.DisabledReason))
        {
            return OrganizerPaymentProviderRequirementsState.Disabled;
        }

        if (requirements.PastDue?.Count > 0)
        {
            return OrganizerPaymentProviderRequirementsState.PastDue;
        }

        if (requirements.CurrentlyDue?.Count > 0)
        {
            return OrganizerPaymentProviderRequirementsState.CurrentlyDue;
        }

        return requirements.EventuallyDue?.Count > 0
            ? OrganizerPaymentProviderRequirementsState.EventuallyDue
            : OrganizerPaymentProviderRequirementsState.Satisfied;
    }

    private static IReadOnlyList<string> BoundedRequirementKeys(IEnumerable<string>? values) => values?
        .Select(value => StripeConnectAccountAdapter.BoundedText(value, 160))
        .Where(value => value is not null)
        .Cast<string>()
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray() ?? [];

    private static string? NormalizeUpper(string? value, int length)
    {
        string normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized.Length == length && normalized.All(char.IsAsciiLetter) ? normalized : null;
    }

    private static bool HasJsonProperty(JsonElement? element, string propertyName) =>
        element is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(propertyName, out _);
}
