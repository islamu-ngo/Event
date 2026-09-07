using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Deployment;

namespace Explore.Infrastructure.Deployment;

public sealed class TicketingDeploymentCapabilityCatalog :
    ITicketingDeploymentCapabilityCatalog
{
    private const string ResourceName =
        "Explore.Infrastructure.Deployment.ticketing-capabilities.json";
    private static readonly TicketingDeploymentCapabilitySnapshot Snapshot =
        Load();

    public TicketingDeploymentCapabilitySnapshot GetSnapshot() => Snapshot;

    private static TicketingDeploymentCapabilitySnapshot Load()
    {
        using Stream stream =
            typeof(TicketingDeploymentCapabilityCatalog).Assembly
                .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "Ticketing deployment capability artifact is missing.");
        CapabilityDocument document =
            JsonSerializer.Deserialize(
                stream,
                TicketingDeploymentCapabilityJsonContext.Default
                    .CapabilityDocument)
            ?? throw new InvalidOperationException(
                "Ticketing deployment capability artifact is empty.");
        if (document.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(document.Revision) ||
            string.IsNullOrWhiteSpace(document.ReferenceTopology) ||
            document.Capabilities.Count == 0)
        {
            throw new InvalidOperationException(
                "Ticketing deployment capability metadata is invalid.");
        }

        FrozenDictionary<string, CapabilityItem> byCode =
            document.Capabilities.ToFrozenDictionary(
                item => item.Code,
                StringComparer.Ordinal);
        string[] statuses =
        [
            TicketingDeploymentStatuses.ProductionApproved,
            TicketingDeploymentStatuses.TestOnly,
            TicketingDeploymentStatuses.Disabled,
        ];
        if (byCode.Count != document.Capabilities.Count ||
            document.Capabilities.Any(item =>
                string.IsNullOrWhiteSpace(item.Code) ||
                !statuses.Contains(item.Status, StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(item.ReasonCode) ||
                item.RequiredExternalGates.Any(string.IsNullOrWhiteSpace)) ||
            !byCode.TryGetValue(
                "protected-delayed-payout",
                out CapabilityItem? payout) ||
            !string.Equals(
                payout.Status,
                TicketingDeploymentStatuses.Disabled,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Ticketing deployment capability values are invalid.");
        }

        return new TicketingDeploymentCapabilitySnapshot(
            document.SchemaVersion,
            document.Revision,
            document.ReferenceTopology,
            document.Capabilities
                .Select(item => new TicketingDeploymentCapability(
                    item.Code,
                    item.Status,
                    item.ReasonCode,
                    item.RequiredExternalGates.ToArray()))
                .ToArray());
    }
}

public sealed record CapabilityDocument(
    int SchemaVersion,
    string Revision,
    string ReferenceTopology,
    IReadOnlyList<CapabilityItem> Capabilities);

public sealed record CapabilityItem(
    string Code,
    string Status,
    string ReasonCode,
    IReadOnlyList<string> RequiredExternalGates);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CapabilityDocument))]
internal sealed partial class TicketingDeploymentCapabilityJsonContext :
    JsonSerializerContext;
