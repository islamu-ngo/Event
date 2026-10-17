using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Settings;

namespace Explore.Application.Features.ConfigurationManifest.Importing;

/// <summary>Retained request custody, not a rejection certificate or an evidence-write grant.</summary>
public sealed record HumanConfigurationImportAuthority(
    HumanCommitSubject Subject,
    HumanAuthenticationLifetime Lifetime,
    ConfigurationImportTarget Target,
    HumanSupportAccessSourceFacts Support)
{
    public HumanSupportForwardingObservation Forwarding => Support.Forwarding;
    public override string ToString() => nameof(HumanConfigurationImportAuthority);
}

public interface IHumanConfigurationImportAdmission
{
    Task<IPreparedHumanConfigurationImport> PrepareAsync(
        HumanCommitSubject subject, ConfigurationImportTarget target,
        HumanConfigurationImportAuthority? original,
        CancellationToken cancellationToken);

    Task<T> ExecuteObservationAsync<T>(HumanConfigurationImportObservation<T> observation,
        CancellationToken cancellationToken);

    Task<T> ExecuteSessionAsync<T>(IPreparedHumanConfigurationImport preparedOwner,
        HumanConfigurationImportSessionOperation<T> operation, CancellationToken cancellationToken);

    /// <summary>Discovery only. Consumers must ask this issuer to validate the exact effect.</summary>
    IHumanConfigurationImportEffectProof? CurrentProof { get; }

    Task<T> ExecuteJoinedAsync<T>(SettingsMutationOperation operation,
        ImmutableArray<HumanConfigurationImportSettingEffect> effects,
        Func<IHumanConfigurationImportEffectProof, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default);

    Task RequireOwnedAsync(IHumanConfigurationImportEffectProof proof, HumanConfigurationImportSettingEffect effect,
        CancellationToken cancellationToken = default);

    Task RequireCurrentOwnedAsync(SettingScope scope, Guid? tenantId, Guid? userId, string key,
        HumanSettingsMutationKind kind, string? value = null, bool? isLocked = null,
        CancellationToken cancellationToken = default);

    Task RequireReadAsync(IHumanConfigurationImportEffectProof proof, SettingScope scope, Guid? tenantId,
        string key, CancellationToken cancellationToken = default);

    Task RequirePublicationAsync(HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken = default);
}

/// <summary>Opaque, callback-limited capability. A public implementation carries no authority.</summary>
public interface IHumanConfigurationImportEffectProof;

/// <summary>
/// Constructed only by the import services for their fixed operation and data callback.
/// A caller cannot substitute an action or use this observation as setting mutation proof.
/// </summary>
public sealed class HumanConfigurationImportObservation<T>
{
    internal HumanConfigurationImportObservation(HumanCommitSubject subject, ConfigurationImportTarget target,
        HumanConfigurationImportObservationAccess access,
        Func<IPreparedHumanConfigurationImport, CancellationToken, Task<T>> observe)
    {
        Subject = subject;
        Target = target;
        Access = access;
        Observe = observe;
    }

    public HumanCommitSubject Subject { get; }
    public ConfigurationImportTarget Target { get; }
    internal HumanConfigurationImportObservationAccess Access { get; }
    internal Func<IPreparedHumanConfigurationImport, CancellationToken, Task<T>> Observe { get; }
}

internal enum HumanConfigurationImportObservationAccess { Read, Update }

internal enum HumanConfigurationImportSessionOperationKind { Create, PreparePreview, ReadArtifact, Cancel }

/// <summary>Only the session manager supplies the fixed storage operation and its exact target.</summary>
public sealed class HumanConfigurationImportSessionOperation<T>
{
    internal HumanConfigurationImportSessionOperation(ConfigurationImportTarget target,
        HumanConfigurationImportSessionOperationKind kind, Func<CancellationToken, Task<T>> execute)
    {
        Target = target;
        Kind = kind;
        Execute = execute;
    }

    internal ConfigurationImportTarget Target { get; }
    internal HumanConfigurationImportSessionOperationKind Kind { get; }
    internal Func<CancellationToken, Task<T>> Execute { get; }
}

/// <summary>Exact serialized storage effect recomputed from the validated artifact, never a key grant.</summary>
public sealed record HumanConfigurationImportSettingEffect(SettingScope Scope, Guid? TenantId, string Key,
    HumanSettingsMutationKind Kind, string? Value = null, bool? IsLocked = null)
{
    public override string ToString() => nameof(HumanConfigurationImportSettingEffect);
}

/// <summary>
/// Created only by the trusted apply coordinator and section compiler after artifact/preview admission.
/// The immutable effect slice and persistence callback come from the same parsed artifact and resolved targets.
/// No public constructor accepts effects, an identity, or an arbitrary callback.
/// </summary>
public sealed class HumanConfigurationImportApply
{
    private readonly Func<CancellationToken, Task<ImmutableArray<SettingChangedNotification>>> apply;
    private readonly Func<CancellationToken, Task> validateSources;

    internal HumanConfigurationImportApply(Guid operationId, HumanCommitSubject subject,
        ConfigurationImportTarget target, string artifactDigest,
        ImmutableArray<HumanConfigurationImportSettingEffect> effects,
        Func<CancellationToken, Task> validateSources,
        Func<CancellationToken, Task<ImmutableArray<SettingChangedNotification>>> apply,
        ImmutableArray<HumanConfigurationImportPublication> publications,
        IHumanConfigurationImportPublicationStore? publicationStore)
    {
        OperationId = operationId;
        Subject = subject;
        Target = target;
        ArtifactDigest = artifactDigest;
        Effects = effects;
        this.validateSources = validateSources;
        this.apply = apply;
        Publications = publications;
        PublicationStore = publicationStore;
    }

    public Guid OperationId { get; }
    public HumanCommitSubject Subject { get; }
    public ConfigurationImportTarget Target { get; }
    public string ArtifactDigest { get; }
    public ImmutableArray<HumanConfigurationImportSettingEffect> Effects { get; }
    public ImmutableArray<HumanConfigurationImportPublication> Publications { get; }
    public IHumanConfigurationImportPublicationStore? PublicationStore { get; }
    public SettingsMutationOperation Operation => Target.Scope == ConfigurationImportScope.Instance
        ? SettingsMutationOperation.ConfigurationInstanceImport : SettingsMutationOperation.ConfigurationTenantImport;

    public Task<ImmutableArray<SettingChangedNotification>> ApplyAsync(CancellationToken cancellationToken) =>
        apply(cancellationToken);

    public async Task ValidateSourcesAsync(CancellationToken cancellationToken)
    {
        await validateSources(cancellationToken);
        if (PublicationStore is { } store)
            await store.RequireCurrentAsync(cancellationToken);
    }

    public override string ToString() => nameof(HumanConfigurationImportApply);
}

public interface IPreparedHumanConfigurationImport
{
    HumanConfigurationImportAuthority Authority { get; }
    Task AcquireAsync(CancellationToken cancellationToken);
    Task<ImmutableArray<SettingChangedNotification>> ExecuteApplyAsync(HumanConfigurationImportApply apply,
        CancellationToken cancellationToken);
}

/// <summary>
/// The actual import factory creates a new selected owner using the retained subject and
/// original credential. It must not resolve an ambient replacement human or a broader purpose.
/// </summary>
public interface IHumanConfigurationImportOwnerFactory
{
    ValueTask<IPolicyMutationExecutionScope> CreateFreshImportOwnerAsync(
        HumanConfigurationImportAuthority original, CancellationToken cancellationToken);

    IOriginalOwnerSettlementBoundary GetImportSettlementBoundary(
        HumanAuthenticationLifetime lifetime);

    IHumanConfigurationImportSourceReader GetImportSourceReader();
}

/// <summary>Fresh persisted rows on the selected live owner, never its tracking identity map.</summary>
public interface IHumanConfigurationImportSourceReader
{
    Task<ConfigurationImportSession?> ReadSessionAsync(
        Guid sessionId, ConfigurationImportTarget target, CancellationToken cancellationToken);
    Task<ConfigurationManagedApplySchedule?> ReadScheduleAsync(
        Guid scheduleId, ConfigurationImportTarget target, CancellationToken cancellationToken);
}
