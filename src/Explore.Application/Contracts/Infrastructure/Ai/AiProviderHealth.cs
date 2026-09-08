namespace Explore.Application.Contracts.Infrastructure.Ai;

public sealed record AiProviderHealth(
    bool Enabled,
    bool Healthy,
    string Status,
    string Description,
    IReadOnlyDictionary<string, object> Data);
