using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Wire = ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Blazor.Client.Services;

/// <summary>Owns interactive private announcement state and single-attempt HAL-advertised mutations.</summary>
public sealed class AnnouncementClientService : IAnnouncementClientService
{
    private readonly IAnnouncementsClient _client;
    private readonly IPublicExperienceService _public;
    private readonly IUserSettingsService _preferences;
    private readonly AuthenticationStateProvider _authentication;
    private readonly ILogger<AnnouncementClientService> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<long, (bool Authenticated, Wire.AnnouncementObservationDto Observation)> _pendingDismissals = [];
    private readonly Dictionary<(bool Authenticated, (Guid Tenant, Guid? OwnerTenant, Guid Edition) Publication), long>
        _uncertainDismissals = [];
    private long _generation;
    private long _authenticationGeneration;
    private long _displaySequence;
    private long _dismissalSettlementSequence;
    private long _sourceSelection;
    private bool _disposed;
    private bool _authenticatedDisplay;
    private bool _publishFence;
    private bool _publishing;
    private Wire.AnnouncementDismissalMarkerDto? _marker;
    private HalResourceOfAnnouncementSourceResource? _source;
    private Wire.AnnouncementSourceDto? _sourceSnapshot;
    private AnnouncementEditScope _sourceScope;
    private Guid? _managedTenant;

    public AnnouncementClientService(IAnnouncementsClient client, IPublicExperienceService publicExperience,
        IUserSettingsService preferences, AuthenticationStateProvider authentication,
        ILogger<AnnouncementClientService> logger)
    {
        _client = client;
        _public = publicExperience;
        _preferences = preferences;
        _authentication = authentication;
        _logger = logger;
        _public.SettingsChanged += OnPublicChanged;
        _authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    public event Action? Changed;
    public AnnouncementDisplayState? Display { get; private set; }
    public bool IsSaving { get; private set; }
    public bool RequiresReconciliation
    {
        get
        {
            lock (_gate)
                return HasUncertainDismissal(Display?.Observation);
        }
    }

    public async Task<AnnouncementDisplayState?> LoadAsync(
        bool reconcile = false, CancellationToken cancellationToken = default)
    {
        long generation, sequence, settlementAtReadStart;
        lock (_gate)
        {
            if (_disposed || IsSaving)
                return null;
            generation = _generation;
            sequence = ++_displaySequence;
            settlementAtReadStart = _dismissalSettlementSequence;
        }
        try
        {
            var settings = _public.SettingsAvailability == PublicExperienceAvailability.Unavailable
                ? null : await _public.GetCachedSettingsAsync();
            if (!Current(generation, cancellationToken) || sequence != _displaySequence
                || settings is null || settings.TenantId is not { } tenant || tenant == Guid.Empty)
                return null;
            var announcement = SnapshotPublic(settings.Announcement);
            if (announcement is null || announcement.TenantId != tenant)
                return null;
            bool authenticated = await AuthenticatedAsync();
            if (!Current(generation, cancellationToken) || sequence != _displaySequence)
                return null;
            Wire.AnnouncementDismissalMarkerDto? marker;
            var observed = announcement.Observation;
            bool visible = announcement.IsVisible;
            bool action;
            if (authenticated)
            {
                var self = await _client.GetMyAnnouncementDismissalAsync(cancellationToken: cancellationToken);
                if (!Current(generation, cancellationToken) || sequence != _displaySequence
                    || self is null || self.TenantId != tenant)
                    return null;
                var current = self.CurrentObservation is null ? null : Snapshot(self.CurrentObservation);
                marker = self.Marker is null ? null : Snapshot(self.Marker);
                if (marker is not null && marker.Observation.TenantId != tenant)
                    return null;
                if (current is not null && (current.TenantId != tenant || !SameIdentity(current, observed)))
                    return null;
                observed = current;
                visible &= current is not null;
                action = self._links?.ContainsKey("dismiss") == true;
            }
            else
            {
                var read = await _preferences.GetAnonymousAnnouncementMarkerAsync(tenant, cancellationToken);
                if (!read.Success || !Current(generation, cancellationToken) || sequence != _displaySequence)
                    return null;
                marker = read.Marker;
                action = observed is not null;
            }
            bool dismissed = marker is not null && SameIdentity(marker.Observation, observed);
            lock (_gate)
            {
                if (!Current(generation, cancellationToken) || sequence != _displaySequence)
                    return null;
                if (reconcile && observed is not null)
                {
                    // A read can settle only uncertainty already recorded when it started.
                    var identity = (authenticated, DismissalIdentity(observed));
                    if (_uncertainDismissals.TryGetValue(identity, out var settled) && settled <= settlementAtReadStart)
                        _uncertainDismissals.Remove(identity);
                }
                _authenticatedDisplay = authenticated;
                _marker = marker;
                IsSaving = HasPendingDismissal(observed);
                Display = new(tenant, observed, announcement.Material, visible, dismissed,
                    action && visible && !dismissed && !HasUncertainDismissal(observed) && !IsSaving);
            }
            Changed?.Invoke();
            return Current(generation, cancellationToken) && sequence == _displaySequence ? Display : null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Announcement state could not be loaded ({FailureType}).", exception.GetType().Name);
            return null;
        }
    }

    public async Task<bool> DismissAsync(CancellationToken cancellationToken = default)
    {
        AnnouncementDisplayState state;
        Wire.AnnouncementObservationDto observed;
        Wire.AnnouncementDismissalMarkerDto? expected;
        long generation, authenticationGeneration;
        bool authenticated;
        lock (_gate)
        {
            if (_disposed || IsSaving || Display is not { CanDismiss: true, Observation: { } observation } current)
                return false;
            if (HasPendingDismissal(observation) || HasUncertainDismissal(observation))
                return false;
            state = current;
            observed = observation;
            expected = _marker;
            generation = _generation;
            authenticationGeneration = _authenticationGeneration;
            authenticated = _authenticatedDisplay;
            ++_displaySequence;
            IsSaving = true;
        }
        bool attempted = false;
        AnnouncementDisplayState? accepted = null;
        try
        {
            if (await AuthenticatedAsync() != authenticated || !Current(generation, cancellationToken))
                return false;
            Wire.AnnouncementDismissalMarkerDto marker;
            if (authenticated)
            {
                Task<HalResourceOfAnnouncementDismissalReceiptDto> pending;
                lock (_gate)
                {
                    if (!Current(generation, cancellationToken) || !ReferenceEquals(state, Display))
                        return false;
                    attempted = true;
                    _pendingDismissals.Add(generation, (authenticated, observed));
                    pending = _client.DismissAnnouncementAsync(new()
                    {
                        Observed = Transport(observed),
                        ExpectedMarker = expected is null ? null : Transport(expected)
                    }, cancellationToken: cancellationToken);
                }
                var receipt = await pending;
                if (!Current(generation, cancellationToken) || receipt is null || receipt.Decision is not (0 or 1)
                    || receipt.Marker is null)
                    return false;
                marker = Snapshot(receipt.Marker);
                if (!SameIdentity(marker.Observation, observed)
                    || receipt.Decision == 0 && marker.Observation != observed)
                    return false;
                var fresh = await _client.GetMyAnnouncementDismissalAsync(cancellationToken: cancellationToken);
                if (!Current(generation, cancellationToken) || fresh is null || fresh.TenantId != state.TenantId
                    || fresh.CurrentObservation is null || Snapshot(fresh.CurrentObservation) != observed
                    || fresh.Marker is null || Snapshot(fresh.Marker) != marker)
                    return false;
            }
            else
            {
                marker = new() { Observation = observed };
                lock (_gate)
                {
                    if (!Current(generation, cancellationToken) || !ReferenceEquals(state, Display))
                        return false;
                    attempted = true;
                    _pendingDismissals.Add(generation, (authenticated, observed));
                }
                if (!await _preferences.SaveAnonymousAnnouncementMarkerAsync(
                        state.TenantId, marker, cancellationToken)
                    || !Current(generation, cancellationToken))
                    return false;
            }
            lock (_gate)
            {
                if (!Current(generation, cancellationToken) || !ReferenceEquals(state, Display))
                    return false;
                _marker = marker;
                accepted = state with { IsDismissed = true, CanDismiss = false };
                Display = accepted;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Announcement dismissal was not confirmed ({FailureType}).", exception.GetType().Name);
        }
        finally
        {
            lock (_gate)
            {
                _pendingDismissals.Remove(generation);
                if (!_disposed && (!authenticated || authenticationGeneration == _authenticationGeneration))
                {
                    if (attempted && (accepted is null || cancellationToken.IsCancellationRequested))
                        _uncertainDismissals[(authenticated, DismissalIdentity(observed))] = ++_dismissalSettlementSequence;
                    if (authenticated == _authenticatedDisplay
                        && (_generation == generation
                            || SameDismissalStorage(Display?.Observation, observed, authenticated)))
                    {
                        ++_displaySequence;
                        IsSaving = HasPendingDismissal(Display?.Observation);
                        if (Display is { } current)
                        {
                            bool canDismiss = (!authenticated || current.CanDismiss)
                                && current.Observation is not null && current.IsVisible && !current.IsDismissed
                                && !IsSaving && !HasUncertainDismissal(current.Observation);
                            if (current.CanDismiss != canDismiss)
                                Display = current with { CanDismiss = canDismiss };
                        }
                    }
                }
            }
            if (!_disposed)
                Changed?.Invoke();
        }
        lock (_gate)
            return accepted is not null && ReferenceEquals(accepted, Display)
                && Current(generation, cancellationToken) && !HasUncertainDismissal(observed);
    }

    public async Task<HalResourceOfAnnouncementSourceResource?> LoadSourceAsync(
        AnnouncementEditScope scope = AnnouncementEditScope.Tenant, Guid? managedTenantId = null,
        CancellationToken cancellationToken = default)
    {
        long generation, selection;
        lock (_gate)
        {
            if (_disposed || _publishing)
                return null;
            generation = _generation;
            selection = ++_sourceSelection;
            _source = null;
            _sourceSnapshot = null;
        }
        try
        {
            if (!await AuthenticatedAsync() || !Current(generation, cancellationToken))
                return null;
            var source = await ReadSourceAsync(scope, managedTenantId, cancellationToken);
            var snapshot = Snapshot(source.Source);
            if (!MatchesTarget(source, snapshot, scope, managedTenantId))
                return null;
            lock (_gate)
            {
                if (!Current(generation, cancellationToken) || selection != _sourceSelection)
                    return null;
                _source = source;
                _sourceSnapshot = snapshot;
                _sourceScope = scope;
                _managedTenant = managedTenantId;
                _publishFence = false;
            }
            Changed?.Invoke();
            return Current(generation, cancellationToken) && selection == _sourceSelection ? source : null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Announcement publication source could not be loaded ({FailureType}).", exception.GetType().Name);
            return null;
        }
    }

    public bool CanPublish(HalResourceOfAnnouncementSourceResource? source)
    {
        lock (_gate)
            return !_disposed && !_publishing && !_publishFence
                && source is not null && ReferenceEquals(source, _source)
                && source._links?.ContainsKey("publish") == true;
    }

    public async Task<AnnouncementPublicationClientResult> PublishAsync(
        HalResourceOfAnnouncementSourceResource source, AnnouncementChangeDto change,
        CancellationToken cancellationToken = default)
    {
        long generation, authenticationGeneration, selection;
        AnnouncementEditScope scope;
        Guid? managed;
        Wire.AnnouncementSourceDto original;
        AnnouncementChangeDto input;
        lock (_gate)
        {
            if (!CanPublish(source) || _sourceSnapshot is not { } retained)
                return new(false, _publishFence, "Reload the original announcement source before saving.");
            original = retained;
            generation = _generation;
            authenticationGeneration = _authenticationGeneration;
            selection = _sourceSelection;
            scope = _sourceScope;
            managed = _managedTenant;
            _publishing = true;
        }
        bool attempted = false, accepted = false;
        HalResourceOfAnnouncementSourceResource? fresh = null;
        try
        {
            input = CopyChange(change);
            if (Snapshot(source.Source) != original || !await AuthenticatedAsync()
                || !Current(generation, cancellationToken))
                return new(false, false, "The announcement source is no longer current.");
            Task<HalResourceOfAnnouncementPublicationReceiptDto> pending;
            lock (_gate)
            {
                if (!Current(generation, cancellationToken) || selection != _sourceSelection)
                    return new(false, false, "The announcement editing context changed.");
                var request = new AnnouncementUpdateDto { Change = input, ExpectedSource = Transport(original) };
                attempted = true;
                pending = scope switch
                {
                    AnnouncementEditScope.Instance => _client.UpdateInstanceAnnouncementAsync(
                        request, cancellationToken: cancellationToken),
                    AnnouncementEditScope.Tenant => _client.UpdateTenantAnnouncementAsync(
                        request, cancellationToken: cancellationToken),
                    AnnouncementEditScope.ManagedTenant => _client.UpdateManagedTenantAnnouncementAsync(
                        managed ?? throw new InvalidOperationException("A managed announcement requires its selected tenant."),
                        request, cancellationToken: cancellationToken),
                    _ => throw new InvalidOperationException("The retained announcement publication scope is invalid.")
                };
            }
            var receipt = await pending;
            if (!Current(generation, cancellationToken) || receipt is null || receipt.Outcome is not (0 or 1))
                return new(false, true, "Announcement publication was not confirmed. Reload its source.");
            var settled = Snapshot(receipt.Source);
            if (settled.Owner != original.Owner || receipt.Outcome == 1 && settled != original
                || receipt.Outcome == 0 && !MatchesPublication(settled, original, input))
                return new(false, true, "The publication receipt did not match the requested change.");

            // Refresh reads, never the mutation. Public notifications may replace the effective source.
            var publicSettings = await _public.GetSettingsAsync();
            if (_disposed || authenticationGeneration != _authenticationGeneration || cancellationToken.IsCancellationRequested
                || publicSettings is null || selection != _sourceSelection)
                return new(false, true, "Reload announcement state before another publication.");
            generation = Volatile.Read(ref _generation);
            fresh = await ReadSourceAsync(scope, managed, cancellationToken);
            var refreshed = Snapshot(fresh.Source);
            if (!Current(generation, cancellationToken) || selection != _sourceSelection
                || !MatchesTarget(fresh, refreshed, scope, managed) || refreshed != settled)
                return new(false, true, "The announcement changed after publication. Reload its source.");
            lock (_gate)
            {
                if (!Current(generation, cancellationToken) || selection != _sourceSelection)
                    return new(false, true, "The announcement editing context changed.");
                _source = fresh;
                _sourceSnapshot = refreshed;
                accepted = true;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Announcement publication was not confirmed ({FailureType}).", exception.GetType().Name);
        }
        finally
        {
            lock (_gate)
            {
                if (!_disposed && authenticationGeneration == _authenticationGeneration
                    && selection == _sourceSelection)
                {
                    _publishing = false;
                    _publishFence = attempted && (!accepted || cancellationToken.IsCancellationRequested);
                }
            }
            if (!_disposed)
                Changed?.Invoke();
        }
        bool current;
        lock (_gate)
        {
            bool ownsSource = accepted && Current(generation, CancellationToken.None)
                && authenticationGeneration == _authenticationGeneration && selection == _sourceSelection
                && ReferenceEquals(fresh, _source) && !_publishing;
            // A Changed subscriber can cancel after the initial fence was set, or replace the source.
            if (ownsSource && cancellationToken.IsCancellationRequested)
                _publishFence = true;
            current = ownsSource && !_publishFence;
        }
        return new(current, !current && attempted,
            current ? null : attempted
                ? "Publication is uncertain. Reload its source before another attempt."
                : "The announcement change is invalid or its editing context changed.", current ? fresh : null);
    }

    private Task<HalResourceOfAnnouncementSourceResource> ReadSourceAsync(
        AnnouncementEditScope scope, Guid? managedTenant, CancellationToken token) => scope switch
    {
        AnnouncementEditScope.Instance when managedTenant is null =>
            _client.GetInstanceAnnouncementSourceAsync(cancellationToken: token),
        AnnouncementEditScope.Tenant when managedTenant is null =>
            _client.GetTenantAnnouncementSourceAsync(cancellationToken: token),
        AnnouncementEditScope.ManagedTenant when managedTenant is { } tenant && tenant != Guid.Empty =>
            _client.GetManagedTenantAnnouncementSourceAsync(tenant, cancellationToken: token),
        _ => throw new ArgumentException("An exact announcement editing context is required.", nameof(scope))
    };

    private static bool MatchesTarget(HalResourceOfAnnouncementSourceResource source,
        Wire.AnnouncementSourceDto snapshot, AnnouncementEditScope scope, Guid? managed) =>
        source.Target == (int)scope && (scope switch
        {
            AnnouncementEditScope.Instance => snapshot.Owner.TenantId is null,
            AnnouncementEditScope.Tenant => snapshot.Owner.TenantId is { } tenant && tenant != Guid.Empty,
            AnnouncementEditScope.ManagedTenant => snapshot.Owner.TenantId == managed,
            _ => false
        });

    private static bool MatchesPublication(Wire.AnnouncementSourceDto current,
        Wire.AnnouncementSourceDto original, AnnouncementChangeDto change)
    {
        if (current.Group is not { } group || group.SelectionEpoch == original.Group?.SelectionEpoch)
            return false;
        var publication = group.Publication;
        return change.Kind switch
        {
            "publish" => publication is not null && change.Material is not null
                && publication.Material == Snapshot(change.Material) && group.Enabled == change.Enabled
                && current.IsLocked == original.IsLocked
                && (change.Redisplay == true || original.Group?.Publication?.Material != publication.Material
                    ? publication.Edition != original.Group?.Publication?.Edition
                    : publication.Edition == original.Group?.Publication?.Edition),
            "set-enabled" => group.Enabled == change.Enabled
                && publication == original.Group?.Publication && current.IsLocked == original.IsLocked,
            "set-lock" => current.IsLocked == change.IsLocked
                && publication == original.Group?.Publication && group.Enabled == (original.Group?.Enabled ?? false),
            "remove-override" => group.Publication is null && !group.Enabled && current.IsLocked == original.IsLocked,
            _ => false
        };
    }

    private static AnnouncementChangeDto CopyChange(AnnouncementChangeDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input switch
        {
            { Kind: "publish", Material: not null, Enabled: not null, Redisplay: not null, IsLocked: null } =>
                new() { Kind = input.Kind, Material = Transport(Snapshot(input.Material)),
                    Enabled = input.Enabled, Redisplay = input.Redisplay },
            { Kind: "set-enabled", Material: null, Enabled: not null, Redisplay: null, IsLocked: null } =>
                new() { Kind = input.Kind, Enabled = input.Enabled },
            { Kind: "set-lock", Material: null, Enabled: null, Redisplay: null, IsLocked: not null } =>
                new() { Kind = input.Kind, IsLocked = input.IsLocked },
            { Kind: "remove-override", Material: null, Enabled: null, Redisplay: null, IsLocked: null } =>
                new() { Kind = input.Kind },
            _ => throw new ArgumentException("An exact announcement change is required.", nameof(input))
        };
    }

    internal static bool SameIdentity(Wire.AnnouncementObservationDto? left, Wire.AnnouncementObservationDto? right) =>
        left is not null && right is not null && DismissalIdentity(left) == DismissalIdentity(right);

    private static (Guid Tenant, Guid? OwnerTenant, Guid Edition) DismissalIdentity(
        Wire.AnnouncementObservationDto observation) =>
        (observation.TenantId, observation.Owner.TenantId, observation.Edition);

    internal static Wire.AnnouncementPublicStateDto? SnapshotPublic(AnnouncementPublicStateDto? value)
    {
        if (value is null)
            return null;
        var observation = value.Observation is null ? null : Snapshot(value.Observation);
        var material = value.Material is null ? null : Snapshot(value.Material);
        if (value.TenantId == Guid.Empty || observation is not null && observation.TenantId != value.TenantId
            || (observation is null) != (material is null) || value.IsVisible && observation is null)
            throw new ArgumentException("The public announcement observation is invalid.", nameof(value));
        return new() { TenantId = value.TenantId, Observation = observation, Material = material, IsVisible = value.IsVisible };
    }

    private static Wire.AnnouncementMaterialDto Snapshot(AnnouncementMaterialDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.Message);
        ArgumentNullException.ThrowIfNull(value.LinkText);
        ArgumentNullException.ThrowIfNull(value.LinkUrl);
        return new() { Message = value.Message.Trim(), LinkText = value.LinkText.Trim(), LinkUrl = value.LinkUrl.Trim() };
    }

    private static Wire.AnnouncementObservationDto Snapshot(AnnouncementObservationDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.Owner);
        ArgumentNullException.ThrowIfNull(value.SourceWitness);
        var witness = value.SourceWitness;
        if (value.TenantId == Guid.Empty || value.Edition == Guid.Empty
            || value.Owner.TenantId == Guid.Empty || value.Owner.TenantId is { } tenant && tenant != value.TenantId
            || witness.InstanceSelectionEpoch == Guid.Empty || witness.TenantSelectionEpoch == Guid.Empty)
            throw new ArgumentException("The announcement observation is invalid.", nameof(value));
        return new()
        {
            TenantId = value.TenantId, Owner = new() { TenantId = value.Owner.TenantId }, Edition = value.Edition,
            SourceWitness = new()
            {
                InstanceSelectionEpoch = witness.InstanceSelectionEpoch, InstanceIsLocked = witness.InstanceIsLocked,
                TenantSelectionEpoch = witness.TenantSelectionEpoch, TenantIsLocked = witness.TenantIsLocked
            }
        };
    }

    private static Wire.AnnouncementDismissalMarkerDto Snapshot(AnnouncementDismissalMarkerDto value) =>
        new() { Observation = Snapshot(value.Observation) };

    private static Wire.AnnouncementSourceDto Snapshot(AnnouncementSourceDto value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(value.Owner);
        Wire.AnnouncementGroupDto? group = null;
        if (value.Owner.TenantId == Guid.Empty || value.Group is null && value.IsLocked)
            throw new ArgumentException("The announcement source is invalid.", nameof(value));
        if (value.Group is { } original)
        {
            if (original.SelectionEpoch == Guid.Empty || original.Enabled && original.Publication is null
                || original.Publication?.Edition == Guid.Empty)
                throw new ArgumentException("The announcement source group is invalid.", nameof(value));
            group = new()
            {
                SelectionEpoch = original.SelectionEpoch, Enabled = original.Enabled,
                Publication = original.Publication is null ? null : new()
                {
                    Edition = original.Publication.Edition, Material = Snapshot(original.Publication.Material)
                }
            };
        }
        return new() { Owner = new() { TenantId = value.Owner.TenantId }, Group = group, IsLocked = value.IsLocked };
    }

    private static AnnouncementMaterialDto Transport(Wire.AnnouncementMaterialDto value) =>
        new() { Message = value.Message, LinkText = value.LinkText, LinkUrl = value.LinkUrl };

    private static AnnouncementObservationDto Transport(Wire.AnnouncementObservationDto value) => new()
    {
        TenantId = value.TenantId, Owner = new() { TenantId = value.Owner.TenantId }, Edition = value.Edition,
        SourceWitness = new()
        {
            InstanceSelectionEpoch = value.SourceWitness.InstanceSelectionEpoch,
            InstanceIsLocked = value.SourceWitness.InstanceIsLocked,
            TenantSelectionEpoch = value.SourceWitness.TenantSelectionEpoch,
            TenantIsLocked = value.SourceWitness.TenantIsLocked
        }
    };

    private static AnnouncementDismissalMarkerDto Transport(Wire.AnnouncementDismissalMarkerDto value) =>
        new() { Observation = Transport(value.Observation) };

    private static AnnouncementSourceDto Transport(Wire.AnnouncementSourceDto value) => new()
    {
        Owner = new() { TenantId = value.Owner.TenantId }, IsLocked = value.IsLocked,
        Group = value.Group is null ? null : new()
        {
            SelectionEpoch = value.Group.SelectionEpoch, Enabled = value.Group.Enabled,
            Publication = value.Group.Publication is null ? null : new()
            {
                Edition = value.Group.Publication.Edition, Material = Transport(value.Group.Publication.Material)
            }
        }
    };

    private async Task<bool> AuthenticatedAsync() =>
        (await _authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true;

    private bool Current(long generation, CancellationToken token) =>
        !_disposed && generation == Volatile.Read(ref _generation) && !token.IsCancellationRequested;

    private bool HasPendingDismissal(Wire.AnnouncementObservationDto? observation) =>
        _pendingDismissals.Values.Any(pending => pending.Authenticated == _authenticatedDisplay
            && SameDismissalStorage(pending.Observation, observation, pending.Authenticated));

    private static bool SameDismissalStorage(Wire.AnnouncementObservationDto? left,
        Wire.AnnouncementObservationDto? right, bool authenticated) =>
        left is not null && right is not null
        && (authenticated ? SameIdentity(left, right) : left.TenantId == right.TenantId);

    private bool HasUncertainDismissal(Wire.AnnouncementObservationDto? observation) =>
        observation is not null && _uncertainDismissals.ContainsKey((_authenticatedDisplay, DismissalIdentity(observation)));

    private void OnAuthenticationChanged(Task<AuthenticationState> _) => Invalidate(authentication: true);
    private void OnPublicChanged() => Invalidate(authentication: false);

    private void Invalidate(bool authentication)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            ++_generation;
            ++_displaySequence;
            Display = null;
            _marker = null;
            _source = null;
            _sourceSnapshot = null;
            IsSaving = false;
            if (authentication)
            {
                foreach (var pending in _pendingDismissals.Where(pair => pair.Value.Authenticated).Select(pair => pair.Key).ToArray())
                    _pendingDismissals.Remove(pending);
                _publishing = false;
                ++_authenticationGeneration;
                foreach (var uncertain in _uncertainDismissals.Keys.Where(key => key.Authenticated).ToArray())
                    _uncertainDismissals.Remove(uncertain);
                _publishFence = false;
            }
        }
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
            ++_generation;
            ++_displaySequence;
            Display = null;
            _pendingDismissals.Clear();
            _uncertainDismissals.Clear();
            _marker = null;
            _source = null;
            _sourceSnapshot = null;
        }
        _public.SettingsChanged -= OnPublicChanged;
        _authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
        return ValueTask.CompletedTask;
    }
}
