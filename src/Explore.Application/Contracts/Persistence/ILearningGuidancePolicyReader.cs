using Explore.Domain.Learning;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Resolves only detached facts captured by the original human learning operation.</summary>
public interface ILearningGuidancePolicyReader
{
    ResolvedLearningGuidancePolicy Resolve(LearningSourceFacts sources);
}

public sealed record ResolvedLearningGuidancePolicy(
    LearningGuidancePolicy Policy,
    int SchemaVersion,
    string DefaultsVersion,
    Guid? ConcurrencyStamp,
    string PolicySourceStamp,
    bool AllowTenantEdits);
