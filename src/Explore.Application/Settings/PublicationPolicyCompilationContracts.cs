namespace Explore.Application.Settings;

using System.Collections.Immutable;

public enum PublicationPolicyMutationKind
{
    Set,
    Remove
}

public sealed record PublicationPolicySystemValueSnapshot(
    string Key,
    string? JsonValue,
    bool IsLocked);

public sealed record PublicationPolicyTenantValueSnapshot(
    Guid? TenantId,
    string Key,
    string? JsonValue);

public sealed record PublicationPolicySettingMutation(
    string Key,
    PublicationPolicyMutationKind Kind,
    string? JsonValue,
    Guid? TenantId,
    bool? IsLocked);

public sealed record PublicationPolicyTenantCompilationInput(
    Guid? TenantId,
    ImmutableArray<PublicationPolicySystemValueSnapshot> SystemValues,
    ImmutableArray<PublicationPolicyTenantValueSnapshot> TenantValues,
    ImmutableArray<PublicationPolicySettingMutation> Mutations);

public sealed record PublicationPolicyInstanceCompilationInput(
    ImmutableArray<PublicationPolicySystemValueSnapshot> SystemValues,
    ImmutableArray<PublicationPolicyTenantValueSnapshot> TenantValues,
    ImmutableArray<PublicationPolicySettingMutation> Mutations);

public sealed record PublicationPolicyCompiledTenantState(
    Guid TenantId,
    ReportingIntakePolicyState State);

public sealed record PublicationPolicyCompilationResult(
    bool Success,
    string? FailureCode,
    ReportingIntakePolicyState? BaseTenantState,
    ImmutableArray<PublicationPolicyCompiledTenantState> TenantStates);
