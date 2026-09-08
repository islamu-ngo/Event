namespace Explore.Application.Contracts.Services;

public sealed class CustomPropertyGovernanceEvaluation
{
    public required string NormalizedNamespace { get; init; }
    public required string NormalizedKey { get; init; }
    public IReadOnlyCollection<string> Errors { get; init; } = [];

    public bool IsValid => Errors.Count == 0;
}
