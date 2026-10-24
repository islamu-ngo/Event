namespace Explore.Domain;

/// <summary>
/// Immutable moderation ancestry established by a host-owned installation operation.
/// All identifiers are supplied, already allocated UUIDv7 values; this is not bootstrap identity.
/// The host must resolve the explicitly selected existing maintenance service principal.
/// This binding records provenance, not credentials or permission.
/// </summary>
public sealed class ModerationHostBinding
{
    private ModerationHostBinding()
    {
    }

    public Guid Id { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid SolutionId { get; private set; }
    public Guid InstanceId { get; private set; }
    public Guid MaintenanceServicePrincipalId { get; private set; }

    /// <summary>
    /// Records installation-owned coordinates and the selected principal without allocating identity.
    /// Existence and ownership checks belong to the host installation operation.
    /// </summary>
    public static ModerationHostBinding Establish(
        Guid id,
        Guid trustDomainId,
        Guid solutionId,
        Guid instanceId,
        Guid maintenanceServicePrincipalId)
    {
        RequireUuidV7(id, nameof(id));
        RequireUuidV7(trustDomainId, nameof(trustDomainId));
        RequireUuidV7(solutionId, nameof(solutionId));
        RequireUuidV7(instanceId, nameof(instanceId));
        RequireUuidV7(maintenanceServicePrincipalId, nameof(maintenanceServicePrincipalId));

        return new ModerationHostBinding
        {
            Id = id,
            TrustDomainId = trustDomainId,
            SolutionId = solutionId,
            InstanceId = instanceId,
            MaintenanceServicePrincipalId = maintenanceServicePrincipalId
        };
    }

    /// <summary>
    /// Checks a repeated installation assertion; conflicting ancestry or principal selection
    /// cannot rewrite the historical binding.
    /// </summary>
    public void RequireMatchingCoordinates(
        Guid trustDomainId,
        Guid solutionId,
        Guid instanceId,
        Guid maintenanceServicePrincipalId)
    {
        if (TrustDomainId != trustDomainId
            || SolutionId != solutionId
            || InstanceId != instanceId
            || MaintenanceServicePrincipalId != maintenanceServicePrincipalId)
        {
            throw new InvalidOperationException("Host-established moderation binding coordinates are immutable.");
        }
    }

    internal static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Identifier must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }
}
