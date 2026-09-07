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
    ITenantSettingRepository tenantSettings,
    IEventLocationRepository eventLocations,
    ISettingMutationLock mutationLock,
    HybridCache cache,
    TimeProvider timeProvider) : ILocationPrivacyGovernanceMutationService
{
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

    public async Task<LocationPrivacyGovernanceMutationResult> ExecuteAsync(
        string key,
        string proposedStoredValue,
        SettingScope scope,
        Guid? tenantId,
        Guid actorUserId,
        Func<CancellationToken, Task<string?>> persist,
        CancellationToken cancellationToken = default)
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
                    persist,
                    token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LocationPrivacyGovernanceMutationResult.Rejected(
                "Location-privacy governance storage is unavailable.");
        }

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
            TenantSetting? previousTenantRow = await GetTenantRowAsync(
                tenantId!.Value,
                key,
                cancellationToken);
            if (previousTenantRow is not null)
            {
                if (!LocationPrivacyGovernancePolicy.TryParse(
                        key,
                        previousTenantRow.Value,
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
            List<TenantSetting> tenantRows = await tenantSettings.GetByKeyAcrossTenants(
                key,
                cancellationToken);
            foreach (IGrouping<Guid, TenantSetting> tenantGroup in tenantRows.GroupBy(row => row.TenantId))
            {
                if (tenantGroup.Count() != 1)
                {
                    return LocationPrivacyGovernanceMutationResult.Rejected(
                        "Conflicting tenant location-privacy setting rows were found.");
                }

                TenantSetting tenantRow = tenantGroup.Single();
                if (!LocationPrivacyGovernancePolicy.TryParse(
                        key,
                        tenantRow.Value,
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
        if (!LocationPrivacyGovernancePolicy.IsTightening(previousEffective, currentEffective))
        {
            return new(true, null, previousStoredValue, []);
        }

        IReadOnlyList<EventLocation> candidates = await eventLocations.GetActiveForGovernanceUpdateAsync(
            scope == SettingScope.Tenant ? tenantId : null,
            cancellationToken);
        DateTime changedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var audits = new List<EventLocationDisclosureAudit>();
        var outboxMessages = new List<OutboxMessage>();
        var corrected = new List<LocationPrivacyProjectionIdentity>();

        foreach (EventLocation eventLocation in candidates)
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

            EventLocationDisclosureAudit audit = eventLocation.ApplyGovernanceTightening(
                requiresPrivacyReview: true,
                actorUserId,
                changedAtUtc);
            audits.Add(audit);
            outboxMessages.Add(LocationPrivacyOutboxMessageFactory.CreateProjectionCorrection(
                Guid.CreateVersion7(),
                eventLocation,
                changedAtUtc));
            corrected.Add(new(
                eventLocation.TenantId,
                eventLocation.EventId,
                eventLocation.Id));
        }

        if (audits.Count > 0)
        {
            await eventLocations.SaveGovernanceChangesAsync(audits, outboxMessages, cancellationToken);
        }

        return new(true, null, previousStoredValue, corrected);
    }

    private async Task<TenantSetting?> GetTenantRowAsync(
        Guid tenantId,
        string key,
        CancellationToken cancellationToken)
    {
        List<TenantSetting> rows = await tenantSettings.GetByTenantAndKeys(
            tenantId,
            [key],
            cancellationToken);
        if (rows.Count > 1)
        {
            throw new InvalidOperationException(
                $"Tenant '{tenantId}' has conflicting rows for location-privacy setting '{key}'.");
        }

        return rows.SingleOrDefault();
    }

    private static bool IsProjectionAffected(
        EventLocation eventLocation,
        string key,
        LocationPrivacyGovernanceSettingValue previous,
        LocationPrivacyGovernanceSettingValue current)
    {
        if (eventLocation.IsToBeAnnounced || eventLocation.Location is null)
        {
            return false;
        }

        bool isPrivateHome = eventLocation.Location.LocationKindId == (int)LocationKindEnum.PrivateHome;
        return key switch
        {
            GovernanceSettingKeys.LocationPrivacy.AllowHomeLocations =>
                isPrivateHome && current.Boolean == false,
            GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress =>
                !isPrivateHome
                && current.Boolean == false
                && (eventLocation.ShowStreetAddress || eventLocation.ShowPostcode),
            GovernanceSettingKeys.LocationPrivacy.AllowPublicCoordinates =>
                !isPrivateHome && current.Boolean == false && eventLocation.ShowCoordinates,
            GovernanceSettingKeys.LocationPrivacy.MinimumHomeAudience =>
                isPrivateHome
                && current.Audience.HasValue
                && AudienceRestrictionRank((LocationDisclosureAudienceEnum)eventLocation.FullDetailsAudienceId)
                    < AudienceRestrictionRank(current.Audience.Value),
            GovernanceSettingKeys.LocationPrivacy.DefaultRevealOffset =>
                HasSelectedExactField(eventLocation)
                && EffectiveRevealFromUtc(eventLocation, current.Duration!.Value)
                    > EffectiveRevealFromUtc(eventLocation, previous.Duration!.Value),
            _ => false
        };
    }

    private static bool HasSelectedExactField(EventLocation eventLocation) =>
        eventLocation.ShowStreetAddress
        || eventLocation.ShowPostcode
        || eventLocation.ShowCoordinates;

    private static DateTime EffectiveRevealFromUtc(EventLocation eventLocation, TimeSpan offset)
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
