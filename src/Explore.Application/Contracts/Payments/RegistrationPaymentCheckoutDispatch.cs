namespace Explore.Application.Contracts.Payments;

public sealed record RegistrationPaymentCheckoutDispatchRequest(
    string LeaseOwner,
    int BatchSize,
    TimeSpan LeaseDuration,
    Uri AllowedReturnOrigin,
    Uri SuccessUrl,
    Uri CancelUrl);

public sealed record RegistrationPaymentCheckoutDispatchResult(
    int Claimed,
    int Completed,
    int Unknown,
    int Parked,
    int Retried,
    int Stale);
