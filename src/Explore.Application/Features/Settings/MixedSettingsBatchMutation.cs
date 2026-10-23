using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Features.Settings;

/// <summary>
/// The original batch observation, not an administrator grant. Only the actual handler
/// can freeze accepted effects; only the original scoped issuer can acquire its owner.
/// </summary>
public sealed class MixedSettingsBatchMutation
{
    public static bool RequiresCombinedSource(UpdateSettingBatchCommand command)
    {
        bool email = command.Values.Keys.Any(EmailDeliverySettingKeys.Contains);
        if (email)
            return command.Values.Keys.Any(key => !EmailDeliverySettingKeys.Contains(key));
        if (command.Scope is not (SettingScope.Instance or SettingScope.Tenant))
            return false;
        bool Specialized(string key) => EventResourceSettingMutationGuard.Handles(key)
            || VisitorAccessSettingMutationGuard.Handles(key)
            || PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal)
            || LocationPrivacySettingDefinitions.All.Any(definition => definition.Key == key);
        return command.Values.Keys.Any(Specialized)
            && command.Values.Keys.Any(key => !Specialized(key));
    }

    private readonly object issuer;
    private ImmutableDictionary<string, string>? accepted;
    private int consumed;
    private bool active;

    internal MixedSettingsBatchMutation(object issuer, UpdateSettingBatchCommand original,
        IHumanMixedSettingsSource source, IMixedSettingsBatchStore store, ISerializableAttemptOwner owner)
    {
        this.issuer = issuer;
        Original = original;
        Source = source;
        Store = store;
        Owner = owner;
    }

    public UpdateSettingBatchCommand Original { get; }
    public IHumanMixedSettingsSource Source { get; }
    public Guid Actor => Source.Subject.PrincipalId;
    public Guid? TenantId => Source.TenantId;
    public ImmutableDictionary<string, string> Accepted =>
        accepted ?? throw new InvalidOperationException("The original batch has not been compiled.");
    internal IMixedSettingsBatchStore Store { get; }
    internal ISerializableAttemptOwner Owner { get; }
    internal IAtprotoFederationSettingsMutationStore? FederationStore { get; private set; }
    /// <summary>The original issuer's opaque observation; exposing it does not authorize a writer.</summary>
    public IAtprotoFederationSettingsSource? FederationSource { get; private set; }
    internal IAsyncDisposable? EnrollmentGate { get; private set; }

    internal void RetainSourceOwners(object originalIssuer,
        IAtprotoFederationSettingsMutationStore? federationStore,
        IAtprotoFederationSettingsSource? federationSource, IAsyncDisposable? enrollmentGate)
    {
        if (!ReferenceEquals(issuer, originalIssuer) || consumed != 0 || active || accepted is not null
            || FederationStore is not null || FederationSource is not null || EnrollmentGate is not null
            || (federationStore is null) != (federationSource is null))
            throw new UnauthorizedAccessException("Only the idle original mixed issuer can retain its source owners.");
        FederationStore = federationStore;
        FederationSource = federationSource;
        EnrollmentGate = enrollmentGate;
    }

    internal void Compile(UpdateSettingBatchCommand original, ImmutableDictionary<string, string> values)
    {
        if (!ReferenceEquals(original, Original) || accepted is not null || !active
            || values.Keys.Any(key => !Original.Values.ContainsKey(key)))
            throw new UnauthorizedAccessException("Mixed compilation must belong to its original active request.");
        accepted = values;
    }

    internal void Claim(object originalIssuer)
    {
        if (!ReferenceEquals(issuer, originalIssuer) || Interlocked.Exchange(ref consumed, 1) != 0)
            throw new UnauthorizedAccessException("Mixed preparation belongs to one original issuer and physical attempt.");
    }

    internal void Activate(object originalIssuer)
    {
        if (!ReferenceEquals(issuer, originalIssuer) || consumed != 1 || active)
            throw new UnauthorizedAccessException("The original acquired mixed issuer is required.");
        active = true;
    }

    internal void Close(object originalIssuer)
    {
        if (!ReferenceEquals(issuer, originalIssuer))
            throw new UnauthorizedAccessException("Only the original mixed issuer can close its attempt.");
        active = false;
    }

    public void RequireActive()
    {
        if (!active || consumed != 1)
            throw new UnauthorizedAccessException("The original mixed attempt is not active.");
        Source.RequireCustody();
    }

    public ImmutableArray<HumanSettingsMutation> SettingsEffects()
    {
        RequireActive();
        return Accepted.Where(pair => !EmailDeliverySettingKeys.Contains(pair.Key))
            .Select(pair => new HumanSettingsMutation(
                TenantId.HasValue ? HumanSettingsOperation.TenantSettings : HumanSettingsOperation.InstanceSettings,
                Original.Scope, TenantId, null, pair.Key, HumanSettingsMutationKind.SetValue, pair.Value))
            .ToImmutableArray();
    }
}

/// <summary>Private original human admission, independently issued from the selected mixed source.</summary>
public interface IHumanMixedSettingsSource
{
    HumanCommitSubject Subject { get; }
    Guid? TenantId { get; }
    string OriginalData { get; }
    void RequireCustody();
    Task RequireSourcesAsync(CancellationToken cancellationToken);
    void RequireTime(DateTimeOffset now);
}

/// <summary>
/// A selected mixed persistence owner. Its implementation retains observation, compilation,
/// native exclusions and private writer completion; a caller-provided implementation is not proof.
/// </summary>
public interface IMixedSettingsBatchStore
{
    DateTime OccurredAt { get; }
    Task ObserveAsync(UpdateSettingBatchCommand original, IHumanMixedSettingsSource source,
        ISerializableAttemptOwner owner, CancellationToken cancellationToken);
    Task AcquireAsync(MixedSettingsBatchMutation mutation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResolvedSetting>> ResolveOriginalAsync(MixedSettingsBatchMutation mutation,
        CancellationToken cancellationToken);
    Task<ResolvedSetting?> ResolveOriginalInstanceAsync(MixedSettingsBatchMutation mutation, string key,
        CancellationToken cancellationToken);
    Task CompileAsync(MixedSettingsBatchMutation mutation, CancellationToken cancellationToken);
    Task ApplyOrdinaryAsync(MixedSettingsBatchMutation mutation, string key, CancellationToken cancellationToken);
    Task ApplyLocationValueAsync(MixedSettingsBatchMutation mutation, string key, CancellationToken cancellationToken);
    string ExpectedSourceData(IHumanMixedSettingsSource originalSource);
    Task RequireCompletedAsync(MixedSettingsBatchMutation mutation, CancellationToken cancellationToken);
    void Close(MixedSettingsBatchMutation mutation);
}

/// <summary>Complete original human facts on the dedicated mixed physical selection.</summary>
public interface IHumanMixedSettingsSourceFactory
{
    Task<IHumanMixedSettingsSourceProtocol> CreateAsync(TypedSettingsOriginal original,
        CancellationToken cancellationToken);
    Task<IHumanMixedSettingsSourceProtocol> CreateAsync(UpdateSettingBatchCommand original,
        CancellationToken cancellationToken);
}

public interface IHumanMixedSettingsSourceProtocol
{
    HumanCommitSubject Subject { get; }
    Explore.Application.Authentication.HumanAuthenticationLifetime Lifetime { get; }
    HumanSupportForwardingObservation Forwarding { get; }
    Guid? TenantId { get; }
    string OriginalData { get; }
    Task<HumanEmailSourceFacts> ObserveAsync(CancellationToken cancellationToken);
    Task<HumanEmailSourceFacts> AcquireAsync(CancellationToken cancellationToken);
    Task<HumanEmailSourceFacts> RequireOwnedAsync(CancellationToken cancellationToken);
}
