namespace Explore.Application.Contracts.Infrastructure;

using Explore.Application.Features.EventReporting.Models;
using Explore.Domain.Enums;

public interface IReportingRoutingPolicyResolver
{
    Task<ReportingRoutingPolicy> ResolveAsync(CancellationToken cancellationToken = default);
}

public sealed record ReportingRoutingPolicy(
    bool LocalCanonicalRequired,
    bool ExternalSyncEnabled,
    bool InstanceOspreyEnabled,
    bool TenantOspreyEnabled,
    bool InstanceCoopEnabled,
    bool TenantCoopEnabled,
    bool TenantProviderConfigurationLocked,
    bool TenantOspreyProviderLocked,
    bool TenantCoopProviderLocked,
    string OspreyRoutingMode,
    string CoopRoutingMode,
    EventReportProviderEvidenceMode EvidenceMode,
    IReadOnlyList<ReportingProviderTarget> OspreyTargets,
    IReadOnlyList<ReportingProviderTarget> CoopTargets);

public sealed record ReportingProviderTarget(
    EventReportExternalProvider Provider,
    EventReportProviderTargetScope Scope,
    string TargetId,
    string? EndpointUrl = null,
    string? ApiKey = null);
