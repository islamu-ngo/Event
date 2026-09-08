using System.Text.Json.Serialization;
using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.DTOs.EventReporting;

public sealed record TenantReportingIntakePolicyDto
{
    [JsonIgnore]
    public Guid TenantId { get; init; }

    public bool Enabled { get; init; }
    public SettingSource Source { get; init; }
    public bool IsLockedByInstance { get; init; }
    public bool CanDisable { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record UpdateTenantReportingIntakePolicyDto
{
    public bool Enabled { get; init; }
}
