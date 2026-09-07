namespace Explore.Application.Contracts.Infrastructure;

public sealed record EmailDispatchTransportHealth(
    bool Enabled,
    bool Healthy,
    string Description,
    IReadOnlyDictionary<string, object> Data);
