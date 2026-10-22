namespace Explore.Application.Contracts.Operations;

/// <summary>Closed access to an already acquired mixed owner; never constructs or reselects a scope.</summary>
public interface IMixedSettingsExecutionScopeAccess
{
    IServiceProvider BorrowMixedSettings(Explore.Application.Features.Settings.MixedSettingsBatchMutation mutation);
}

/// <summary>
/// Acquires an immutable instance or target-tenant lease before constructing a mutation graph.
/// Existing handlers retain ownership of transactions, ordered locks and commit effects.
/// </summary>
public interface IPolicyMutationExecutionScopeFactory
{
    ValueTask<IPolicyMutationExecutionScope> CreateAsync(
        PolicyMutationScope selection, CancellationToken cancellationToken);
}

/// <summary>Scope-local construction services; this contract never dispatches a request.</summary>
public interface IPolicyMutationExecutionScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}

/// <summary>Selected by source-owned composition, never a posted credential capability.</summary>
public enum PolicyMutationPurpose
{
    Standard,
    EmailDelivery,
    MixedSettings,
    TenantCreation
}

/// <summary>Scope and purpose are sealed together before constructing any mutation dependencies.</summary>
public sealed record PolicyMutationScope(Guid? TenantId, PolicyMutationPurpose Purpose = PolicyMutationPurpose.Standard)
{
    public static PolicyMutationScope Instance { get; } = new(TenantId: null);
    public static PolicyMutationScope EmailInstance { get; } = new(null, PolicyMutationPurpose.EmailDelivery);
    public static PolicyMutationScope MixedSettingsInstance { get; } = new(null, PolicyMutationPurpose.MixedSettings);
    public static PolicyMutationScope TenantCreation { get; } = new(null, PolicyMutationPurpose.TenantCreation);

    public static PolicyMutationScope MixedSettingsTenant(Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return new(tenantId, PolicyMutationPurpose.MixedSettings);
    }

    public static PolicyMutationScope EmailTenant(Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return new(tenantId, PolicyMutationPurpose.EmailDelivery);
    }
}
