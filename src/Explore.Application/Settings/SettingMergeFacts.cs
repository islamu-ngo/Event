using Explore.Domain;

namespace Explore.Application.Settings;

public sealed record SystemSettingFacts(
    string Value,
    SettingValueType ValueType,
    bool IsLocked,
    string? Description,
    string? Category,
    string? AllowedValues);

public sealed record TenantSettingFacts(string Value, bool IsLocked);

/// <summary>
/// Immutable inputs for one setting's precedence evaluation, without persistence entities.
/// A tenant context without overrides still retains SMTP delegation semantics.
/// </summary>
public sealed record SettingMergeFacts(
    SystemSettingFacts? System,
    TenantSettingFacts? Tenant,
    string? Organization,
    string? Group,
    string? User,
    bool HasTenantContext,
    string? SmtpDelegationValue);
