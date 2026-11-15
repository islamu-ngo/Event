using System.Collections.Immutable;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Application.Contracts.Services;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Immutable inputs of the actual visitor Settings action. This envelope is not
/// authority: only its original preparation issuer and selected store can admit it.
/// </summary>
public sealed class VisitorSettingsOriginal(
    object command, ImmutableArray<HumanSettingsMutation> effects, HumanCommitSubject subject,
    Guid? tenantId, Guid? auditActor, DateTime occurredAt)
{
    public object Command { get; } = command;
    public ImmutableArray<HumanSettingsMutation> Effects { get; } = effects;
    public HumanCommitSubject Subject { get; } = subject;
    public Guid? TenantId { get; } = tenantId;
    public Guid? AuditActor { get; } = auditActor;
    public DateTime OccurredAt { get; } = occurredAt;
    public ImmutableDictionary<string, Guid> NewIds { get; } = effects
        .ToImmutableDictionary(effect => effect.Key, _ => Guid.CreateVersion7(), StringComparer.Ordinal);
}

public interface IVisitorSettingsOriginalSource
{
    HumanCommitSubject Subject { get; }
    void RequireCustody();
}

/// <summary>Exposes only the current callback's genuine visitor original.</summary>
public interface IHumanVisitorSettingsPreparation
{
    VisitorSettingsOriginal? CurrentVisitorOriginal { get; }
    IVisitorSettingsOriginalStore? CurrentVisitorStore { get; }
    void RequireVisitorSource(IVisitorSettingsOriginalSource source);
}

/// <summary>Detached storage values; changing them cannot change the store's private image.</summary>
public sealed record VisitorSettingsSnapshot(
    ImmutableArray<SystemSetting> Systems,
    ImmutableArray<TenantSetting> Tenants,
    ImmutableArray<Guid> AccountRequiredTenantIds,
    IConfiguration Deployment);

/// <summary>
/// Observes while idle, acquires on the original Serializable attempt, and privately
/// verifies either validated persistence or an explicit unchanged refusal.
/// </summary>
public interface IVisitorSettingsOriginalStore
{
    Task ObserveAsync(VisitorSettingsOriginal original, IVisitorSettingsOriginalSource source,
        CancellationToken token);
    Task AcquireAsync(VisitorSettingsOriginal original, CancellationToken token);
    Task<VisitorSettingsSnapshot> ReadSnapshotAsync(VisitorSettingsOriginal original, CancellationToken token);
    Task ApplyValidatedAsync(VisitorSettingsOriginal original, IVisitorAccessSettingsWriter validator,
        ImmutableArray<(object Entity, bool Removed)> changes, CancellationToken token);
    Task RefuseAsync(VisitorSettingsOriginal original, CancellationToken token);
    Task VerifyAsync(VisitorSettingsOriginal original, CancellationToken token);
    Task RequireCompleteAsync(VisitorSettingsOriginal original, CancellationToken token);
    void Close(VisitorSettingsOriginal original);
}
