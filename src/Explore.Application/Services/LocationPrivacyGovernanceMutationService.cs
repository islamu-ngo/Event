using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Services;

public sealed class LocationPrivacyGovernanceMutationService(
    ISystemSettingRepository systemSettings,
    ILocationPrivacyGovernanceStore governanceStore,
    ISettingMutationLock mutationLock,
    HybridCache cache,
    TimeProvider timeProvider,
    IHumanSettingsMutationPreparation preparation) : ILocationPrivacyGovernanceMutationService
{
    private static readonly AsyncLocal<StoreAdmission?> CurrentAdmission = new();

    public bool Handles(string key) => LocationPrivacyGovernancePolicy.Handles(key);

    public async Task<string?> ValidateTenantValueAsync(
        string key,
        string proposedStoredValue,
        CancellationToken cancellationToken = default)
    {
        if (!Handles(key))
        {
            return null;
        }

        if (!LocationPrivacyGovernancePolicy.TryParse(
                key,
                proposedStoredValue,
                out LocationPrivacyGovernanceSettingValue tenantValue,
                out string? proposedError))
        {
            return proposedError;
        }

        SystemSetting? instanceRow = await systemSettings.GetByKey(key, cancellationToken);
        string instanceStoredValue = instanceRow?.Value
            ?? LocationPrivacyGovernancePolicy.DefaultStoredValue(key);
        if (!LocationPrivacyGovernancePolicy.TryParse(
                key,
                instanceStoredValue,
                out LocationPrivacyGovernanceSettingValue instanceValue,
                out _))
        {
            return $"Setting '{key}' cannot be overridden while its instance ceiling is invalid.";
        }

        return LocationPrivacyGovernancePolicy.IsTenantWidening(instanceValue, tenantValue)
            ? $"Tenant value for '{key}' would widen the instance location-privacy ceiling."
            : null;
    }

    public Task<LocationPrivacyGovernanceMutationResult> ExecuteAsync(
        string key,
        string proposedStoredValue,
        SettingScope scope,
        Guid? tenantId,
        Guid actorUserId,
        Func<CancellationToken, Task<string?>> persist,
        CancellationToken cancellationToken = default) =>
        preparation.ExecuteLocationJoinedAsync(key, scope, tenantId, actorUserId,
            async (proof, effect, derivedValue, token) =>
            {
                if (effect.Kind == HumanSettingsMutationKind.SetValue
                    && !string.Equals(proposedStoredValue, derivedValue, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("Location values must match the original prepared action.");
                var previous = CurrentAdmission.Value;
                CurrentAdmission.Value = new(preparation, proof, effect, actorUserId, [], null);
                try
                {
                    var result = await ExecuteAdmittedAsync(key, derivedValue, scope, tenantId,
                        actorUserId, persist, token);
                    if (result.Accepted && governanceStore is IMixedLocationPrivacyGovernanceStore { MixedOccurredAt: not null } mixed)
                        await mixed.CompleteMixedAsync(key, tenantId, actorUserId, token);
                    return result;
                }
                finally { CurrentAdmission.Value = previous; }
            }, cancellationToken);

    private async Task<LocationPrivacyGovernanceMutationResult> ExecuteAdmittedAsync(
        string key, string proposedStoredValue, SettingScope scope, Guid? tenantId, Guid actorUserId,
        Func<CancellationToken, Task<string?>> persist, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(persist);
        if (!Handles(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown location-privacy setting key.");
        }

        if (scope is not SettingScope.Instance and not SettingScope.Tenant)
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(
                "Location-privacy governance can only be changed at instance or tenant scope.");
        }

        if (scope == SettingScope.Tenant && (!tenantId.HasValue || tenantId.Value == Guid.Empty))
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(
                "A tenant-scoped location-privacy change requires a non-empty tenant id.");
        }

        if (actorUserId == Guid.Empty)
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(
                "An authenticated actor is required to change location-privacy governance.");
        }

        LocationPrivacyGovernanceMutationResult result;
        bool persistenceStarted = false;
        try
        {
            result = await mutationLock.ExecuteAsync(
                key,
                async token => await ExecuteLockedAsync(
                    key,
                    proposedStoredValue,
                    scope,
                    tenantId,
                    actorUserId,
                    async innerToken =>
                    {
                        persistenceStarted = true;
                        return await persist(innerToken);
                    },
                    token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch when (!persistenceStarted)
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(
                "Location-privacy governance storage is unavailable.");
        }

        // Once persistence begins, failures must reach the transaction owner.
        // Returning a rejection there could commit the setting without its projection corrections.
        return result;
    }

    public async ValueTask InvalidateScopeAsync(
        SettingScope scope,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        await cache.RemoveByTagAsync(CacheTags.EventLocations, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.Events, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventLists, cancellationToken);
        await cache.RemoveByTagAsync(CacheTags.EventDetails, cancellationToken);
        if (scope == SettingScope.Tenant && tenantId is { } scopedTenantId && scopedTenantId != Guid.Empty)
        {
            await cache.RemoveByTagAsync(CacheTags.EventLocationsByTenant(scopedTenantId), cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(scopedTenantId), cancellationToken);
        }
    }

    private async Task<LocationPrivacyGovernanceMutationResult> ExecuteLockedAsync(
        string key,
        string proposedStoredValue,
        SettingScope scope,
        Guid? tenantId,
        Guid actorUserId,
        Func<CancellationToken, Task<string?>> persist,
        CancellationToken cancellationToken)
    {
        if (!LocationPrivacyGovernancePolicy.TryParse(
                key,
                proposedStoredValue,
                out LocationPrivacyGovernanceSettingValue proposedValue,
                out string? proposedError))
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(proposedError!);
        }

        SystemSetting? instanceRow = await systemSettings.GetByKey(key, cancellationToken);
        string previousInstanceStoredValue = instanceRow?.Value
            ?? LocationPrivacyGovernancePolicy.DefaultStoredValue(key);
        bool instanceValueIsValid = LocationPrivacyGovernancePolicy.TryParse(
            key,
            previousInstanceStoredValue,
            out LocationPrivacyGovernanceSettingValue previousInstanceValue,
            out _);

        if (scope == SettingScope.Tenant && !instanceValueIsValid)
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(
                $"Setting '{key}' cannot be overridden while its instance ceiling is invalid.");
        }

        if (scope == SettingScope.Instance && !instanceValueIsValid)
        {
            LocationPrivacyGovernancePolicy.TryParse(
                key,
                LocationPrivacyGovernancePolicy.DefaultStoredValue(key),
                out previousInstanceValue,
                out _);
        }

        LocationPrivacyGovernanceSettingValue previousEffective = previousInstanceValue;
        LocationPrivacyGovernanceSettingValue currentEffective = proposedValue;
        var tenantValuesByTenant = new Dictionary<Guid, LocationPrivacyGovernanceSettingValue>();
        if (scope == SettingScope.Tenant)
        {
            LocationPrivacyGovernanceOverride? previousTenantRow = await GetTenantRowAsync(
                tenantId!.Value,
                key,
                cancellationToken);
            if (previousTenantRow is not null)
            {
                if (!LocationPrivacyGovernancePolicy.TryParse(
                        key,
                        previousTenantRow.StoredValue,
                        out LocationPrivacyGovernanceSettingValue previousTenantValue,
                        out _))
                {
                    return LocationPrivacyGovernanceMutationResult.Rejected(
                        $"Setting '{key}' cannot be changed while its tenant value is invalid.");
                }

                previousEffective = LocationPrivacyGovernancePolicy.MostRestrictive(
                    previousInstanceValue,
                    previousTenantValue);
            }

            if (LocationPrivacyGovernancePolicy.IsTenantWidening(previousInstanceValue, proposedValue))
            {
                return LocationPrivacyGovernanceMutationResult.Rejected(
                    $"Tenant value for '{key}' would widen the instance location-privacy ceiling.");
            }

            currentEffective = LocationPrivacyGovernancePolicy.MostRestrictive(
                previousInstanceValue,
                proposedValue);
        }
        else
        {
            IReadOnlyList<LocationPrivacyGovernanceOverride> tenantRows = await governanceStore.ReadOverridesAsync(
                key, null,
                cancellationToken);
            foreach (IGrouping<Guid, LocationPrivacyGovernanceOverride> tenantGroup in tenantRows.GroupBy(row => row.TenantId))
            {
                if (tenantGroup.Count() != 1)
                {
                    return LocationPrivacyGovernanceMutationResult.Rejected(
                        "Conflicting tenant location-privacy setting rows were found.");
                }

                LocationPrivacyGovernanceOverride tenantRow = tenantGroup.Single();
                if (!LocationPrivacyGovernancePolicy.TryParse(
                        key,
                        tenantRow.StoredValue,
                        out LocationPrivacyGovernanceSettingValue tenantValue,
                        out _))
                {
                    LocationPrivacyGovernancePolicy.TryParse(
                        key,
                        LocationPrivacyGovernancePolicy.DefaultStoredValue(key),
                        out tenantValue,
                        out _);
                }

                tenantValuesByTenant.Add(tenantGroup.Key, tenantValue);
            }
        }

        string? previousStoredValue = await persist(cancellationToken);
        var active = CurrentAdmission.Value
            ?? throw new UnauthorizedAccessException("The original location mutation is required.");
        await preparation.RequireOwnedAsync(active.Proof, active.Effect, cancellationToken);
        string? persisted = scope == SettingScope.Instance
            ? (await systemSettings.GetByKey(key, cancellationToken))?.Value
            : (await GetTenantRowAsync(tenantId!.Value, key, cancellationToken))?.StoredValue;
        if (active.Effect.Kind == HumanSettingsMutationKind.Remove ? persisted is not null
            : !string.Equals(persisted, active.Effect.Value, StringComparison.Ordinal))
            throw new InvalidOperationException("Location projection effects require the original setting mutation to be persisted.");
        if (!LocationPrivacyGovernancePolicy.IsTightening(previousEffective, currentEffective))
        {
            return new(true, null, previousStoredValue, []);
        }

        IReadOnlyList<EventLocationGovernanceState> candidates = await governanceStore.ReadActiveProjectionsAsync(
            scope == SettingScope.Tenant ? tenantId : null,
            cancellationToken);
        DateTime changedAtUtc = (governanceStore as IMixedLocationPrivacyGovernanceStore)?.MixedOccurredAt
            ?? timeProvider.GetUtcNow().UtcDateTime;
        var corrections = new List<LocationPrivacyGovernanceCorrection>();
        var corrected = new List<LocationPrivacyProjectionIdentity>();

        foreach (EventLocationGovernanceState eventLocation in candidates)
        {
            LocationPrivacyGovernanceSettingValue projectionPrevious = previousEffective;
            LocationPrivacyGovernanceSettingValue projectionCurrent = currentEffective;
            if (scope == SettingScope.Instance
                && tenantValuesByTenant.TryGetValue(eventLocation.TenantId, out var tenantValue))
            {
                projectionPrevious = LocationPrivacyGovernancePolicy.MostRestrictive(
                    previousInstanceValue,
                    tenantValue);
                projectionCurrent = LocationPrivacyGovernancePolicy.MostRestrictive(
                    proposedValue,
                    tenantValue);
            }

            if (!LocationPrivacyGovernancePolicy.IsTightening(projectionPrevious, projectionCurrent)
                || !IsProjectionAffected(eventLocation, key, projectionPrevious, projectionCurrent))
            {
                continue;
            }

            EventLocationDisclosureAudit audit = eventLocation.CreateTighteningAudit(actorUserId, changedAtUtc);
            corrections.Add(new(eventLocation, audit, LocationPrivacyOutboxMessageFactory.CreateProjectionCorrection(
                Guid.CreateVersion7(), eventLocation.EventLocationId, eventLocation.TenantId,
                eventLocation.EventId, audit.NewPolicyVersion, changedAtUtc)));
            corrected.Add(new(
                eventLocation.TenantId,
                eventLocation.EventId,
                eventLocation.EventLocationId));
        }

        if (corrections.Count > 0)
        {
            var admission = CurrentAdmission.Value
                ?? throw new UnauthorizedAccessException("The original location producer is required.");
            var capturedCorrections = corrections.ToArray();
            CurrentAdmission.Value = admission with { Corrections = capturedCorrections, ChangedAtUtc = changedAtUtc };
            try
            {
                await governanceStore.ApplyCorrectionsAsync(key, scope == SettingScope.Tenant ? tenantId : null,
                    capturedCorrections, actorUserId, changedAtUtc, cancellationToken);
            }
            finally { CurrentAdmission.Value = admission; }
        }

        return new(true, null, previousStoredValue, corrected);
    }

    public static async Task<HumanSettingsMutation> RequireStoreReadAsync(
        string? key, Guid? tenantId, CancellationToken cancellationToken)
    {
        var admission = CurrentAdmission.Value;
        if (admission is null || admission.Effect.TenantId != tenantId
            || key is not null && admission.Effect.Key != key)
            throw new UnauthorizedAccessException("Location storage requires its original coordinated source closure.");
        await admission.Preparation.RequireOwnedAsync(admission.Proof, admission.Effect, cancellationToken);
        return admission.Effect;
    }

    public static async Task RequireStoreCorrectionsAsync(string key, Guid? tenantId,
        IReadOnlyList<LocationPrivacyGovernanceCorrection> corrections, Guid actor, DateTime changed,
        CancellationToken cancellationToken)
    {
        await RequireStoreReadAsync(key, tenantId, cancellationToken);
        var admission = CurrentAdmission.Value!;
        if (admission.Actor != actor || admission.ChangedAtUtc != changed
            || !ReferenceEquals(admission.Corrections, corrections))
            throw new UnauthorizedAccessException("Only the original location producer can admit projection corrections.");
    }

    private sealed record StoreAdmission(IHumanSettingsMutationPreparation Preparation,
        IPreparedHumanSettingsMutation Proof, HumanSettingsMutation Effect, Guid Actor,
        IReadOnlyList<LocationPrivacyGovernanceCorrection> Corrections, DateTime? ChangedAtUtc);

    private async Task<LocationPrivacyGovernanceOverride?> GetTenantRowAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocationPrivacyGovernanceOverride> rows = await governanceStore.ReadOverridesAsync(
            key, tenantId,
            cancellationToken);
        if (rows.Count > 1)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' has conflicting rows for location-privacy setting '{key}'.");
        }

        return rows.SingleOrDefault();
    }

    private static bool IsProjectionAffected(
        EventLocationGovernanceState eventLocation,
        string key,
        LocationPrivacyGovernanceSettingValue previous,
        LocationPrivacyGovernanceSettingValue current)
    {
        if (eventLocation.IsToBeAnnounced || eventLocation.LocationKindId is null)
        {
            return false;
        }

        bool isPrivateHome = eventLocation.LocationKindId == (int)LocationKindEnum.PrivateHome;
        return key switch
        {
            GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations =>
                isPrivateHome && current.Boolean == false,
            GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress =>
                !isPrivateHome
                && current.Boolean == false
                && (eventLocation.Fields & (EventLocationDisclosureFields.StreetAddress | EventLocationDisclosureFields.Postcode)) != 0,
            GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates =>
                !isPrivateHome && current.Boolean == false && eventLocation.Fields.HasFlag(EventLocationDisclosureFields.Coordinates),
            GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience =>
                isPrivateHome
                && current.Audience.HasValue
                && AudienceRestrictionRank(eventLocation.Audience)
                    < AudienceRestrictionRank(current.Audience.Value),
            GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset =>
                HasSelectedExactField(eventLocation)
                && EffectiveRevealFromUtc(eventLocation, current.Duration!.Value)
                    > EffectiveRevealFromUtc(eventLocation, previous.Duration!.Value),
            _ => false
        };
    }

    private static bool HasSelectedExactField(EventLocationGovernanceState eventLocation) =>
        (eventLocation.Fields & (EventLocationDisclosureFields.StreetAddress
            | EventLocationDisclosureFields.Postcode | EventLocationDisclosureFields.Coordinates)) != 0;

    private static DateTime EffectiveRevealFromUtc(EventLocationGovernanceState eventLocation, TimeSpan offset)
    {
        if (eventLocation.CreatedAt == default || eventLocation.CreatedAt.Kind != DateTimeKind.Utc)
        {
            return DateTime.MaxValue;
        }

        DateTime governed;
        try
        {
            governed = eventLocation.CreatedAt.Add(offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTime.MaxValue;
        }

        return eventLocation.RevealFullDetailsFromUtc is { } explicitReveal && explicitReveal > governed
            ? explicitReveal
            : governed;
    }

    private static int AudienceRestrictionRank(LocationDisclosureAudienceEnum audience) => audience switch
    {
        LocationDisclosureAudienceEnum.AnyCurrentRegistrant => 0,
        LocationDisclosureAudienceEnum.ConfirmedParticipant => 1,
        LocationDisclosureAudienceEnum.Never => 2,
        _ => int.MinValue
    };

    public async Task InvalidateMutationAsync(
        SettingScope scope,
        Guid? tenantId,
        IReadOnlyList<LocationPrivacyProjectionIdentity> corrected,
        CancellationToken cancellationToken)
    {
        await InvalidateScopeAsync(scope, tenantId, cancellationToken);
        foreach (Guid correctedTenantId in corrected.Select(item => item.TenantId).Distinct())
        {
            await cache.RemoveByTagAsync(CacheTags.EventLocationsByTenant(correctedTenantId), cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventListByTenant(correctedTenantId), cancellationToken);
        }

        foreach (Guid eventId in corrected.Select(item => item.EventId).Distinct())
        {
            await cache.RemoveByTagAsync(CacheTags.Event(eventId), cancellationToken);
            await cache.RemoveByTagAsync(CacheTags.EventLocationsByEvent(eventId), cancellationToken);
        }

        foreach (Guid eventLocationId in corrected.Select(item => item.EventLocationId).Distinct())
        {
            await cache.RemoveByTagAsync(CacheTags.EventLocation(eventLocationId), cancellationToken);
        }
    }
}
