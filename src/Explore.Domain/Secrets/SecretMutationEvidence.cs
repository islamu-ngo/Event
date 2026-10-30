namespace Explore.Domain.Secrets;

/// <summary>Value-free native effect evidence, independent of delivery or activation.</summary>
public enum SecretMutationEvidence
{
    NotDispatched,
    PossiblyDispatched,
    Applied,
    Pending,
    Rejected,
    Conflict
}
