using System.Collections.Immutable;
using Explore.Persistence.Database.ProviderPrimitives;
using System.Globalization;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Services;

public sealed partial class MixedSettingsBatchStore
{
    private readonly HashSet<string> locationValues = new(StringComparer.Ordinal);

    public async Task ApplyLocationValueAsync(MixedSettingsBatchMutation prepared, string key, CancellationToken cancellationToken)
    {
        Require(prepared);
        var effect = await LocationPrivacyGovernanceMutationService.RequireStoreReadAsync(key, prepared.TenantId, cancellationToken);
        if (!LocationPrivacySettingDefinitions.All.Any(definition => definition.Key == key)
            || effect.Kind != HumanSettingsMutationKind.SetValue || !prepared.Accepted.TryGetValue(key, out string? value)
            || effect.Value != value || locationValues.Contains(key))
            throw new UnauthorizedAccessException("The actual location producer must own its original mixed scalar effect.");
        await ApplyScalarAsync(prepared, key, value, false, cancellationToken);
        locationValues.Add(key);
    }

    internal IReadOnlyList<EventLocationGovernanceState> LocationProjections(Guid? tenantId)
    {
        Require(Mutation);
        if (tenantId != Mutation.TenantId)
            throw new UnauthorizedAccessException("Location projections must belong to the original mixed scope.");
        static Guid Id(JsonNode row, string key) => Guid.Parse(row[key]!.GetValue<string>());
        static DateTime Time(JsonNode value) =>
            DateTime.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var locations = expected![nameof(Location)]!.AsArray()
            .ToDictionary(row => (Id(row!, nameof(Location.TenantId)), Id(row!, nameof(Location.Id))));
        return expected[nameof(EventLocation)]!.AsArray()
            .Where(row => !row![nameof(EventLocation.IsDeleted)]!.GetValue<bool>()
                && (!tenantId.HasValue || Id(row, nameof(EventLocation.TenantId)) == tenantId))
            .OrderBy(row => row![nameof(EventLocation.TenantId)]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(row => row![nameof(EventLocation.EventId)]!.GetValue<string>(), StringComparer.Ordinal)
            .ThenBy(row => row![nameof(EventLocation.Id)]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(row =>
            {
                var item = row!;
                JsonNode? location = item[nameof(EventLocation.LocationId)] is { } locationId
                    ? locations.GetValueOrDefault((Id(item, nameof(EventLocation.TenantId)), Guid.Parse(locationId.GetValue<string>()))) : null;
                EventLocationDisclosureFields fields = EventLocationDisclosureFields.None;
                foreach (var (property, field) in new[]
                {
                    (nameof(EventLocation.ShowVenueName), EventLocationDisclosureFields.VenueName),
                    (nameof(EventLocation.ShowCity), EventLocationDisclosureFields.City),
                    (nameof(EventLocation.ShowCountry), EventLocationDisclosureFields.Country),
                    (nameof(EventLocation.ShowRoomName), EventLocationDisclosureFields.RoomName),
                    (nameof(EventLocation.ShowStreetAddress), EventLocationDisclosureFields.StreetAddress),
                    (nameof(EventLocation.ShowPostcode), EventLocationDisclosureFields.Postcode),
                    (nameof(EventLocation.ShowCoordinates), EventLocationDisclosureFields.Coordinates)
                })
                    if (item[property]!.GetValue<bool>()) fields |= field;
                return new EventLocationGovernanceState(
                    Id(item, nameof(EventLocation.TenantId)), Id(item, nameof(EventLocation.EventId)), Id(item, nameof(EventLocation.Id)),
                    false, item[nameof(EventLocation.IsToBeAnnounced)]!.GetValue<bool>(),
                    location?[nameof(Location.LocationKindId)]?.GetValue<int>(), fields,
                    (LocationDisclosureAudienceEnum)item[nameof(EventLocation.FullDetailsAudienceId)]!.GetValue<int>(),
                    item[nameof(EventLocation.RevealFullDetailsFromUtc)] is { } reveal ? Time(reveal) : null,
                    Time(item[nameof(EventLocation.CreatedAt)]!), item[nameof(EventLocation.PolicyVersion)]!.GetValue<int>(),
                    Id(item, nameof(EventLocation.ConcurrencyStamp)));
            }).ToArray();
    }

    internal async Task<Dictionary<Guid, EventLocation>> LoadLocationRowsAsync(Guid[] ids, CancellationToken token)
    {
        Require(Mutation);
        if (!(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            return await context.Set<EventLocation>().IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .Where(row => ids.Contains(row.Id) && !row.IsDeleted).ToDictionaryAsync(row => row.Id, token);
        var rows = new JsonArray(expected![nameof(EventLocation)]!.AsArray()
            .Where(row => ids.Contains(Guid.Parse(row![nameof(EventLocation.Id)]!.GetValue<string>())))
            .Select(row => row!.DeepClone()).ToArray());
        // Materialize the exact original native image through EF's own private-setter mapping.
        // This selects no ambient relation and does not synthesize a second context or owner.
        return await MixedSettingsLocationImageQuery.LoadAsync(context, rows, token);
    }

    internal async Task WriteLocationCorrectionsAsync(LocationPrivacyGovernanceStore writer, string key,
        IReadOnlyList<LocationPrivacyGovernanceCorrection> corrections, Guid actor, DateTime occurred, CancellationToken token)
    {
        Require(Mutation);
        await LocationPrivacyGovernanceMutationService.RequireStoreCorrectionsAsync(
            key, Mutation.TenantId, corrections, actor, occurred, token);
        if (!ReferenceEquals(writer, services.GetRequiredService<ILocationPrivacyGovernanceStore>())
            || actor != Mutation.Actor || occurred != OccurredAt || !locationValues.Contains(key))
            throw new UnauthorizedAccessException("Only the actual location correction writer can emit the original mixed corrections.");
        var entries = context.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        var targets = corrections.Select(correction => correction.Before.EventLocationId).ToHashSet();
        var audits = corrections.Select(correction => correction.Audit).ToHashSet(ReferenceEqualityComparer.Instance);
        var messages = corrections.Select(correction => correction.Message).ToHashSet(ReferenceEqualityComparer.Instance);
        var receipts = corrections.ToDictionary(correction => correction.Message.Id, correction =>
            Snapshot(LocationPrivacyCorrectionReceipt.ForGovernance(correction.Message.Id,
                correction.Before.EventId, correction.Audit, occurred), typeof(LocationPrivacyCorrectionReceipt)));
        if (entries.Any(entry => entry.Entity switch
            {
                EventLocation row => !targets.Contains(row.Id),
                EventLocationDisclosureAudit row => !audits.Contains(row),
                OutboxMessage row => !messages.Contains(row),
                LocationPrivacyCorrectionReceipt row => !receipts.TryGetValue(row.Id, out var receipt)
                    || !JsonNode.DeepEquals(receipt, Snapshot(row, typeof(LocationPrivacyCorrectionReceipt))),
                _ => true
            }))
            throw new UnauthorizedAccessException("An unrelated row cannot join the private mixed location correction set.");
        await EmitAsync("settings", [key], entries.Select(entry => (entry.Entity, entry.State)).ToImmutableArray(), false, token);
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            context.DisclosureMutations.Enlist(corrections.Select(correction => correction.Before.TenantId));
    }

    internal async Task CompleteLocationAsync(LocationPrivacyGovernanceStore writer, string key, Guid actor, CancellationToken token)
    {
        Require(Mutation);
        var effect = await LocationPrivacyGovernanceMutationService.RequireStoreReadAsync(key, Mutation.TenantId, token);
        if (!ReferenceEquals(writer, services.GetRequiredService<ILocationPrivacyGovernanceStore>())
            || actor != Mutation.Actor || !locationValues.Contains(key) || effect.Value != Mutation.Accepted[key])
            throw new UnauthorizedAccessException("The original location producer has not completed its mixed setting and corrections.");
        await EmitAsync("settings", [key], [], true, token);
        completions.Add(key, new(writer, Mutation, key, Mutation.Accepted[key]));
    }
}
