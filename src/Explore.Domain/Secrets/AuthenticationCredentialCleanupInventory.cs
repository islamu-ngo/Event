namespace Explore.Domain.Secrets;

/// <summary>
/// Exact metadata-only legacy rows for an independently authorized cleanup approval.
/// Coordinates, diagnostic text and credential values are never part of this inventory.
/// </summary>
public sealed record AuthenticationCredentialCleanupInventory
{
    public sealed record Target(Guid Id, string SettingKey);

    public AuthenticationCredentialCleanupInventory(Guid operationId, Guid deploymentRevision,
        Guid headStamp, Guid operationStamp, IEnumerable<Target> settings, IEnumerable<Target> bindings)
    {
        OperationId = operationId;
        DeploymentRevision = deploymentRevision;
        HeadStamp = headStamp;
        OperationStamp = operationStamp;
        Settings = Array.AsReadOnly(settings.ToArray());
        Bindings = Array.AsReadOnly(bindings.ToArray());
    }

    public Guid OperationId { get; }
    public Guid DeploymentRevision { get; }
    public Guid HeadStamp { get; }
    public Guid OperationStamp { get; }
    public IReadOnlyList<Target> Settings { get; }
    public IReadOnlyList<Target> Bindings { get; }
}
