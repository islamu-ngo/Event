using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class LocationPrivacyGovernanceProviderOperations
{
    private const string BypassReason = TenantFilterBypassReasons.DiscoveryDisclosureMutation;

    internal static async Task<IReadOnlyList<LocationPrivacyGovernanceOverride>> ReadOverridesAsync(
        ExploreDbContext context, string key, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
            return await context.Set<TenantSetting>().IgnoreAllFilters(BypassReason).AsNoTracking()
                .Where(setting => setting.SettingKey == key && (tenantId == null || setting.TenantId == tenantId))
                .OrderBy(setting => setting.TenantId).ThenBy(setting => setting.Id)
                .Select(setting => new LocationPrivacyGovernanceOverride(setting.TenantId, setting.Value))
                .ToListAsync(cancellationToken);

        await using var command = Command(context,
            $"SELECT * FROM {PostgresLocationPrivacyGovernanceContract.OverridesFunctionSql(context)}(@key, @tenant)");
        Parameter(command, "key", DbType.String, key);
        Parameter(command, "tenant", DbType.Guid, tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LocationPrivacyGovernanceOverride>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }

    internal static async Task<IReadOnlyList<EventLocationGovernanceState>> ReadActiveProjectionsAsync(
        ExploreDbContext context, Guid? tenantId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
            return await PortableFacts(context, tenantId).ToListAsync(cancellationToken);

        await using var command = Command(context,
            $"SELECT * FROM {PostgresLocationPrivacyGovernanceContract.ProjectionsFunctionSql(context)}(@tenant)");
        Parameter(command, "tenant", DbType.Guid, tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<EventLocationGovernanceState>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetBoolean(3),
                reader.GetBoolean(4), reader.IsDBNull(5) ? null : reader.GetInt32(5),
                (EventLocationDisclosureFields)reader.GetInt32(6),
                (LocationDisclosureAudienceEnum)reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8), reader.GetDateTime(9),
                reader.GetInt32(10), reader.GetGuid(11)));
        return result;
    }

    internal static async Task<List<Guid>?> TryApplyNativeCorrectionsAsync(
        ExploreDbContext context, string key, Guid? tenantId, LocationPrivacyGovernanceCorrection[] batch,
        Guid actorUserId, DateTime changedAtUtc, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        await using var command = Command(context,
            $"SELECT * FROM {PostgresLocationPrivacyGovernanceContract.CorrectionsFunctionSql(context)}" +
            "(@key, @tenant, @actor, CAST(@changed AS timestamp with time zone), CAST(@targets AS jsonb))");
        Parameter(command, "key", DbType.String, key);
        Parameter(command, "tenant", DbType.Guid, tenantId);
        Parameter(command, "actor", DbType.Guid, actorUserId);
        // Parse the parameter and JSON audit time through the same PostgreSQL
        // timestamp conversion, including sub-microsecond rounding.
        Parameter(command, "changed", DbType.String,
            changedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        Parameter(command, "targets", DbType.String, JsonSerializer.Serialize(batch.Select(item => new
        {
            item.Before,
            item.Audit,
            Message = new
            {
                item.Message.Id,
                item.Message.AggregateType,
                item.Message.AggregateId,
                item.Message.EventType,
                Payload = JsonSerializer.Deserialize<JsonElement>(
                    item.Message.Payload ?? throw new InvalidOperationException("Governance outbox payload is required.")),
                item.Message.Status,
                item.Message.CreatedAt,
                item.Message.MaxRetries
            },
            Stamp = Guid.CreateVersion7()
        })));
        var affected = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                affected.Add(reader.GetGuid(0));
        return affected;
    }

    internal static IQueryable<EventLocationGovernanceState> PortableFacts(
        ExploreDbContext context, Guid? tenantId, Guid[]? ids = null) =>
        from item in context.Set<EventLocation>().IgnoreAllFilters(BypassReason).AsNoTracking()
        where !item.IsDeleted && (tenantId == null || item.TenantId == tenantId)
            && (ids == null || ids.Contains(item.Id))
        join location in context.Set<Location>().IgnoreTenantFilter(BypassReason).AsNoTracking()
            on new { item.TenantId, Id = item.LocationId }
            equals new { location.TenantId, Id = (Guid?)location.Id } into locations
        from location in locations.DefaultIfEmpty()
        orderby item.TenantId, item.EventId, item.Id
        select new EventLocationGovernanceState(
            item.TenantId, item.EventId, item.Id, item.IsDeleted, item.IsToBeAnnounced,
            location == null ? null : location.LocationKindId,
            (item.ShowVenueName ? EventLocationDisclosureFields.VenueName : EventLocationDisclosureFields.None)
                | (item.ShowCity ? EventLocationDisclosureFields.City : EventLocationDisclosureFields.None)
                | (item.ShowCountry ? EventLocationDisclosureFields.Country : EventLocationDisclosureFields.None)
                | (item.ShowRoomName ? EventLocationDisclosureFields.RoomName : EventLocationDisclosureFields.None)
                | (item.ShowStreetAddress ? EventLocationDisclosureFields.StreetAddress : EventLocationDisclosureFields.None)
                | (item.ShowPostcode ? EventLocationDisclosureFields.Postcode : EventLocationDisclosureFields.None)
                | (item.ShowCoordinates ? EventLocationDisclosureFields.Coordinates : EventLocationDisclosureFields.None),
            (LocationDisclosureAudienceEnum)item.FullDetailsAudienceId, item.RevealFullDetailsFromUtc,
            item.CreatedAt, item.PolicyVersion, item.ConcurrencyStamp);

    private static DbCommand Command(ExploreDbContext context, string text)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = (context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Location privacy governance requires its owning transaction."))
            .GetDbTransaction();
        command.CommandText = text;
        return command;
    }

    private static void Parameter(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
