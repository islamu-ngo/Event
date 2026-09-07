namespace Explore.Application.Authorization;

public sealed record AuthorizationProviderReconciliationResult(
    bool Attempted,
    bool Succeeded,
    bool EndpointVerified,
    bool PoliciesSynchronized,
    string Message);
