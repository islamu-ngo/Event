namespace Explore.Application.Settings.Groups;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Domain.Constants;

public class ReportingSettingGroup : ISettingGroup
{
    public bool TenantExternalSyncEnabled { get; private set; } = true;
    public bool EnableTenantOspreyProvider { get; private set; }
    public bool EnableTenantCoopProvider { get; private set; }
    public string OspreyRoutingMode { get; private set; } = ReportingRoutingMode.Both;
    public string CoopRoutingMode { get; private set; } = ReportingRoutingMode.Both;
    public EventReportProviderEvidenceMode EvidenceMode { get; private set; } = EventReportProviderEvidenceMode.MetadataOnly;
    public string OspreyEndpointUrl { get; private set; } = string.Empty;
    public string OspreyApiKey { get; private set; } = string.Empty;
    public string OspreyWebhookSecret { get; private set; } = string.Empty;
    public string CoopEndpointUrl { get; private set; } = string.Empty;
    public string CoopApiKey { get; private set; } = string.Empty;
    public string CoopWebhookSecret { get; private set; } = string.Empty;

    public bool IsTenantOspreyConfigured =>
        EnableTenantOspreyProvider
        && !string.IsNullOrWhiteSpace(OspreyEndpointUrl)
        && !string.IsNullOrWhiteSpace(OspreyApiKey);

    public bool IsTenantCoopConfigured =>
        EnableTenantCoopProvider
        && !string.IsNullOrWhiteSpace(CoopEndpointUrl)
        && !string.IsNullOrWhiteSpace(CoopApiKey);

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.Reporting.TenantExternalSyncEnabled,
        GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider,
        GovernanceSettingKeys.Reporting.EnableTenantCoopProvider,
        GovernanceSettingKeys.Reporting.OspreyRoutingMode,
        GovernanceSettingKeys.Reporting.CoopRoutingMode,
        GovernanceSettingKeys.Reporting.EvidenceMode,
        GovernanceSettingKeys.Reporting.OspreyEndpointUrl,
        InfrastructureSecretSettingKeys.Reporting.OspreyApiKey,
        InfrastructureSecretSettingKeys.Reporting.OspreyWebhookSecret,
        GovernanceSettingKeys.Reporting.CoopEndpointUrl,
        InfrastructureSecretSettingKeys.Reporting.CoopApiKey,
        InfrastructureSecretSettingKeys.Reporting.CoopWebhookSecret
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.TenantExternalSyncEnabled, out var tenantExternalSync))
            TenantExternalSyncEnabled = SettingValueSerializer.Deserialize(tenantExternalSync.Value, true);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.EnableTenantOspreyProvider, out var enableOsprey))
            EnableTenantOspreyProvider = SettingValueSerializer.Deserialize(enableOsprey.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.EnableTenantCoopProvider, out var enableCoop))
            EnableTenantCoopProvider = SettingValueSerializer.Deserialize(enableCoop.Value, false);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.OspreyRoutingMode, out var ospreyRoutingMode))
            OspreyRoutingMode = NormalizeRoutingMode(ospreyRoutingMode.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.CoopRoutingMode, out var coopRoutingMode))
            CoopRoutingMode = NormalizeRoutingMode(coopRoutingMode.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.EvidenceMode, out var evidenceMode))
            EvidenceMode = ParseEvidenceMode(evidenceMode.Value);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.OspreyEndpointUrl, out var ospreyEndpoint))
            OspreyEndpointUrl = SettingValueSerializer.DeserializeString(ospreyEndpoint.Value, string.Empty);
        if (settings.TryGetValue(InfrastructureSecretSettingKeys.Reporting.OspreyApiKey, out var ospreyApiKey))
            OspreyApiKey = SettingValueSerializer.DeserializeString(ospreyApiKey.Value, string.Empty);
        if (settings.TryGetValue(InfrastructureSecretSettingKeys.Reporting.OspreyWebhookSecret, out var ospreyWebhookSecret))
            OspreyWebhookSecret = SettingValueSerializer.DeserializeString(ospreyWebhookSecret.Value, string.Empty);
        if (settings.TryGetValue(GovernanceSettingKeys.Reporting.CoopEndpointUrl, out var coopEndpoint))
            CoopEndpointUrl = SettingValueSerializer.DeserializeString(coopEndpoint.Value, string.Empty);
        if (settings.TryGetValue(InfrastructureSecretSettingKeys.Reporting.CoopApiKey, out var coopApiKey))
            CoopApiKey = SettingValueSerializer.DeserializeString(coopApiKey.Value, string.Empty);
        if (settings.TryGetValue(InfrastructureSecretSettingKeys.Reporting.CoopWebhookSecret, out var coopWebhookSecret))
            CoopWebhookSecret = SettingValueSerializer.DeserializeString(coopWebhookSecret.Value, string.Empty);
    }

    private static string NormalizeRoutingMode(string? value)
    {
        string mode = SettingValueSerializer.DeserializeString(value, ReportingRoutingMode.Both).Trim();
        return mode.ToLowerInvariant() switch
        {
            ReportingRoutingMode.Instance => ReportingRoutingMode.Instance,
            ReportingRoutingMode.Tenant => ReportingRoutingMode.Tenant,
            _ => ReportingRoutingMode.Both
        };
    }

    private static EventReportProviderEvidenceMode ParseEvidenceMode(string? value)
    {
        string mode = SettingValueSerializer.DeserializeString(value, nameof(EventReportProviderEvidenceMode.MetadataOnly));
        return Enum.TryParse(mode, ignoreCase: true, out EventReportProviderEvidenceMode parsed)
            ? parsed
            : EventReportProviderEvidenceMode.MetadataOnly;
    }
}

public static class ReportingRoutingMode
{
    public const string Instance = "instance";
    public const string Tenant = "tenant";
    public const string Both = "both";
}
