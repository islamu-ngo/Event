namespace Explore.Application.Features.ConfigurationManifest.Application;

/// <summary>
/// Value-free import evidence committed with the identity. Outbox delivery can be
/// retried without replaying the identity mutation.
/// </summary>
public sealed record OperatorIdentityImportAudit(
    Guid ActorUserId,
    string ContentDigest,
    string ExpectedRevisionHash,
    Guid CommittedRevision)
{
    public const string EventType = "OperatorIdentityImported";
    public const string AggregateType = "InstanceOperatorIdentity";
}
