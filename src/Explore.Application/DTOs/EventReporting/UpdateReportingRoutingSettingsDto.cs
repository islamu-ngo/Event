using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Settings.Groups;

namespace Explore.Application.DTOs.EventReporting;

public sealed record UpdateReportingRoutingSettingsDto
{
    public ReportingRoutingPolicyUpdateDto? Policy { get; init; }
    public ReportingProviderRoutingUpdateDto? Osprey { get; init; }
    public ReportingProviderRoutingUpdateDto? Coop { get; init; }
}

public sealed record ReportingRoutingPolicyUpdateDto
{
    public bool ExternalSyncEnabled { get; init; }
    public EventReportProviderEvidenceMode EvidenceMode { get; init; } = EventReportProviderEvidenceMode.MetadataOnly;
}

public sealed record ReportingProviderRoutingUpdateDto
{
    public bool Enabled { get; init; }
    public string RoutingMode { get; init; } = ReportingRoutingMode.Both;
    public string? EndpointUrl { get; init; }
    public ReportingProviderCredentialsUpdateDto? Credentials { get; init; }
}

public sealed record ReportingProviderCredentialsUpdateDto
{
    public string? ApiKey { get; init; }
    public string? WebhookSecret { get; init; }
}
