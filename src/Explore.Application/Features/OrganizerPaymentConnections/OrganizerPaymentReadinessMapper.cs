using Explore.Application.Contracts.Services;
using Explore.Domain;

namespace Explore.Application.Features.OrganizerPaymentConnections;

public static class OrganizerPaymentReadinessMapper
{
    public static OrganizerPaymentProviderReadinessObservation ToObservation(
        OrganizerPaymentProviderReadiness readiness) => OrganizerPaymentProviderReadinessObservation.Create(
            readiness.MerchantCountryCode,
            MapChargeReadiness(readiness),
            MapRequirements(readiness.RequirementsState),
            readiness.SupportedCurrencyCodes,
            readiness.ObservedAt,
            readiness.EvidenceRevision);

    public static ChargeCapabilityState MapChargeReadiness(OrganizerPaymentProviderReadiness readiness)
    {
        if (!readiness.ChargesEnabled
            || readiness.CardPaymentsCapabilityState == OrganizerPaymentProviderCapabilityState.Inactive
            || readiness.TransfersCapabilityState == OrganizerPaymentProviderCapabilityState.Inactive)
        {
            return ChargeCapabilityState.Inactive;
        }

        if (readiness.CardPaymentsCapabilityState == OrganizerPaymentProviderCapabilityState.Pending
            || readiness.TransfersCapabilityState == OrganizerPaymentProviderCapabilityState.Pending)
        {
            return ChargeCapabilityState.Pending;
        }

        return readiness.CardPaymentsCapabilityState == OrganizerPaymentProviderCapabilityState.Active
            && readiness.TransfersCapabilityState == OrganizerPaymentProviderCapabilityState.Active
            ? ChargeCapabilityState.Active
            : ChargeCapabilityState.Unknown;
    }

    public static ProviderRequirementsState MapRequirements(OrganizerPaymentProviderRequirementsState state) => state switch
    {
        OrganizerPaymentProviderRequirementsState.CurrentlyDue => ProviderRequirementsState.CurrentlyDue,
        OrganizerPaymentProviderRequirementsState.EventuallyDue => ProviderRequirementsState.EventuallyDue,
        OrganizerPaymentProviderRequirementsState.PastDue => ProviderRequirementsState.PastDue,
        OrganizerPaymentProviderRequirementsState.Satisfied => ProviderRequirementsState.Satisfied,
        _ => ProviderRequirementsState.Unknown
    };
}
