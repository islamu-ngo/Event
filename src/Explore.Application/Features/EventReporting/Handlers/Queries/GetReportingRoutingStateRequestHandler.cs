using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Models;
using Explore.Application.Features.EventReporting.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.EventReporting.Handlers.Queries;

public sealed class GetReportingRoutingStateRequestHandler(
    IReportingRoutingPolicyResolver routingPolicyResolver,
    ITenantContext tenantContext)
    : IRequestHandler<GetReportingRoutingStateRequest, ReportingRoutingStateDto>
{
    public async Task<ReportingRoutingStateDto> Handle(
        GetReportingRoutingStateRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await routingPolicyResolver.ResolveAsync(cancellationToken);
        var tenantId = tenantContext.TenantId == Guid.Empty ? request.TenantId : tenantContext.TenantId;

        return new ReportingRoutingStateDto
        {
            TenantId = tenantId,
            LocalCanonicalRequired = policy.LocalCanonicalRequired,
            ExternalSyncEnabled = policy.ExternalSyncEnabled,
            TenantProviderConfigurationLocked = policy.TenantProviderConfigurationLocked,
            TenantOspreyProviderLocked = policy.TenantOspreyProviderLocked,
            TenantCoopProviderLocked = policy.TenantCoopProviderLocked,
            EvidenceModeId = (int)policy.EvidenceMode,
            EvidenceModeCode = policy.EvidenceMode.ToString(),
            EvidenceModeName = ToDisplayName(policy.EvidenceMode.ToString()),
            OspreyRoutingMode = policy.OspreyRoutingMode,
            CoopRoutingMode = policy.CoopRoutingMode,
            Osprey = MapProviderState(
                EventReportExternalProvider.Osprey,
                policy.InstanceOspreyEnabled,
                policy.TenantOspreyEnabled,
                policy.OspreyTargets),
            Coop = MapProviderState(
                EventReportExternalProvider.Coop,
                policy.InstanceCoopEnabled,
                policy.TenantCoopEnabled,
                policy.CoopTargets)
        };
    }

    private static ReportingProviderStateDto MapProviderState(
        EventReportExternalProvider provider,
        bool instanceEnabled,
        bool tenantEnabled,
        IReadOnlyList<ReportingProviderTarget> targets)
        => new()
        {
            ProviderId = (int)provider,
            ProviderCode = provider.ToString(),
            ProviderName = ToDisplayName(provider.ToString()),
            InstanceEnabled = instanceEnabled,
            TenantEnabled = tenantEnabled,
            Targets = targets.Select(MapTarget).ToList()
        };

    private static ReportingProviderTargetDto MapTarget(ReportingProviderTarget target)
        => new()
        {
            ProviderId = (int)target.Provider,
            ProviderCode = target.Provider.ToString(),
            ProviderName = ToDisplayName(target.Provider.ToString()),
            ScopeId = (int)target.Scope,
            ScopeCode = target.Scope.ToString(),
            ScopeName = ToDisplayName(target.Scope.ToString()),
            TargetId = target.TargetId,
            EndpointConfigured = !string.IsNullOrWhiteSpace(target.EndpointUrl),
            ApiKeyConfigured = !string.IsNullOrWhiteSpace(target.ApiKey)
        };

    private static string ToDisplayName(string value) =>
        string.Concat(value.SelectMany((character, index) =>
            index > 0 && char.IsUpper(character) ? [' ', character] : new[] { character }));
}
