namespace Explore.Application.DTOs.EventReporting;

public sealed record ReportingRoutingStateDto
{
    public Guid TenantId { get; init; }

    public bool LocalCanonicalRequired { get; init; }

    public bool ExternalSyncEnabled { get; init; }

    public bool TenantProviderConfigurationLocked { get; init; }

    public bool TenantOspreyProviderLocked { get; init; }

    public bool TenantCoopProviderLocked { get; init; }

    public int EvidenceModeId { get; init; }

    public string EvidenceModeCode { get; init; } = string.Empty;

    public string EvidenceModeName { get; init; } = string.Empty;

    public string OspreyRoutingMode { get; init; } = string.Empty;

    public string CoopRoutingMode { get; init; } = string.Empty;

    public ReportingProviderStateDto Osprey { get; init; } = new();

    public ReportingProviderStateDto Coop { get; init; } = new();
}

public sealed record ReportingProviderStateDto
{
    public int ProviderId { get; init; }

    public string ProviderCode { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public bool InstanceEnabled { get; init; }

    public bool TenantEnabled { get; init; }

    public IReadOnlyList<ReportingProviderTargetDto> Targets { get; init; } = [];
}

public sealed record ReportingProviderTargetDto
{
    public int ProviderId { get; init; }

    public string ProviderCode { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public int ScopeId { get; init; }

    public string ScopeCode { get; init; } = string.Empty;

    public string ScopeName { get; init; } = string.Empty;

    public string TargetId { get; init; } = string.Empty;

    public bool EndpointConfigured { get; init; }

    public bool ApiKeyConfigured { get; init; }
}
