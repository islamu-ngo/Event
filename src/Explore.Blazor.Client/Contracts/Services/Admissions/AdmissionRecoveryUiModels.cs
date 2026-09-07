using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public enum AdmissionRecoveryUiOutcome
{
    Invalid = 0,
    Consumed = 1,
    RateLimited = 2,
    Unavailable = 3
}

public sealed record AdmissionRecoveryUiResult(
    AdmissionRecoveryUiOutcome Outcome,
    AdmissionTicketRecoveryDeliveryDto? Delivery = null);
