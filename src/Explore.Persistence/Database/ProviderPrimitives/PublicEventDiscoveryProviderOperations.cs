using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Federation;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static partial class PublicEventDiscoveryProviderOperations
{
    internal static async Task<(List<Event> Items, int TotalCount)> PageLocalAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, EventQuerySpecification specification,
        int pageNumber, int pageSize, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 1000);
        var request = new
        {
            Clauses = Compile(specification),
            Occurrence = Occurrence(specification),
            Sort = Sort(specification),
            Descending = !specification.HasSort || specification.SortDescending,
            After = (object?)null,
            Take = pageSize,
            Offset = checked((pageNumber - 1) * pageSize)
        };
        var result = (await ReadAsync(context, binding, "page_local", request, token)).Single();
        return (result.GetProperty("Items").EnumerateArray().Select(row => Local(context, row)).ToList(),
            result.GetProperty("TotalCount").GetInt32());
    }

    internal static async Task<IReadOnlyList<Event>> SeekLocalAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, EventQuerySpecification specification,
        EventDiscoverySourceCursor? after, int take, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, 1000);
        var request = new
        {
            Clauses = Compile(specification),
            Occurrence = Occurrence(specification),
            Sort = Sort(specification),
            Descending = specification.SortDescending,
            After = Cursor(after),
            Take = take
        };
        var rows = await ReadAsync(context, binding, "seek_local", request, token);
        return rows.Select(row => Local(context, row)).ToArray();
    }

    internal static async Task<IReadOnlyList<Event>> MembersLocalAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, EventQuerySpecification specification,
        IReadOnlyDictionary<Guid, Guid> matchingSessions, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(matchingSessions.Count, 100);
        var request = new
        {
            Clauses = Compile(specification),
            Occurrence = Occurrence(specification),
            Sort = Sort(specification),
            Descending = specification.SortDescending,
            After = (object?)null,
            Members = matchingSessions.Select(pair => new { EventId = pair.Key, SessionId = pair.Value }).ToArray()
        };
        var rows = await ReadAsync(context, binding, "members_local", request, token);
        return rows.Select(row => Local(context, row)).ToArray();
    }

    internal static async Task<IReadOnlyList<AtprotoEventProjection>> SeekRemoteAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, AtprotoEventProjectionQuery query,
        EventDiscoverySourceCursor? after, CancellationToken token) =>
        (await ReadAsync(context, binding, "seek_remote", RemoteRequest(query, after, []), token))
            .Select(row => Materialize<AtprotoEventProjection>(context, row)).ToArray();

    internal static async Task<IReadOnlyList<AtprotoEventProjection>> MembersRemoteAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, AtprotoEventProjectionQuery query,
        IReadOnlyCollection<Guid> records, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(records.Count, 100);
        return (await ReadAsync(context, binding, "members_remote", RemoteRequest(query, null, records), token))
            .Select(row => Materialize<AtprotoEventProjection>(context, row)).ToArray();
    }

    internal static async Task<IReadOnlyList<AtprotoEventProjection>> VisibleRemoteRecordsAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, IReadOnlyCollection<Guid> records, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(records.Count, 100);
        return (await ReadAsync(context, binding, "visible_remote_records",
            new { Members = records.ToArray(), Sort = "Date", Descending = false, After = (object?)null }, token))
            .Select(row => Materialize<AtprotoEventProjection>(context, row)).ToArray();
    }

    private static EventOccurrenceDiscoveryFilter Occurrence(EventQuerySpecification specification)
    {
        var occurrence = specification.Occurrence
            ?? throw new ArgumentException("Discovery requires a same-session occurrence.");
        if (!Enum.IsDefined(occurrence.View) || occurrence.LocationIds?.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(specification));
        return occurrence;
    }

    private static object RemoteRequest(AtprotoEventProjectionQuery query, EventDiscoverySourceCursor? after,
        IReadOnlyCollection<Guid> members)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, 1000);
        if (!Enum.IsDefined(query.Sort) || !Enum.IsDefined(query.TemporalFilter)
            || query.SearchTerm?.Length > 200 || query.Modes?.Count > 50)
            throw new ArgumentOutOfRangeException(nameof(query));
        return new
        {
            query.Take,
            Sort = query.Sort.ToString(),
            Descending = query.SortDescending,
            After = Cursor(after),
            SearchPattern = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : "%" + query.SearchTerm.Trim().ToLowerInvariant() + "%",
            query.DateFrom,
            query.DateTo,
            Modes = query.Modes?.ToArray() ?? [],
            Temporal = (int)query.TemporalFilter,
            query.Now,
            Members = members.ToArray()
        };
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadAsync(ExploreDbContext context,
        PublicEventDiscoveryReadBinding binding, string operation, object request, CancellationToken token)
    {
        binding.RequireSerializableOwner(context);
        DbTransaction transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = PostgresPublicEventDiscoveryContract.AdmissionSql(context, binding.Enrollment)
            + $"\nSELECT value::text FROM {PostgresPublicEventDiscoveryContract.FunctionSql(context, operation)}(@tenant,@request) AS value;";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = context.TenantFilterTenantId!.Value });
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request) });
        var rows = new List<JsonElement>();
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            // The admission DO statement has no rowset; providers can expose it as an empty result.
            do
            {
                while (await reader.ReadAsync(token))
                {
                    using var document = JsonDocument.Parse(reader.GetString(0));
                    rows.Add(document.RootElement.Clone());
                }
            } while (await reader.NextResultAsync(token));
        }
        binding.RequireSerializableOwner(context);
        if (!ReferenceEquals(transaction, context.Database.CurrentTransaction!.GetDbTransaction()))
            throw new UnauthorizedAccessException("Discovery source reads cannot substitute their transaction.");
        return rows;
    }

    private static Event Local(ExploreDbContext context, JsonElement row)
    {
        var entity = Materialize<Event>(context, row);
        entity.EventType = Optional<EventType>("EventType");
        entity.EventProvenanceType = Optional<EventProvenanceType>("EventProvenanceType");
        entity.AudienceGender = Optional<AudienceGender>("AudienceGender");
        entity.AudienceAge = Optional<AudienceAge>("AudienceAge");
        entity.EventStatus = Optional<EventStatus>("EventStatus")!;
        entity.VisibilityType = Optional<VisibilityType>("VisibilityType")!;
        entity.EventFormat = Optional<EventFormat>("EventFormat")!;
        entity.Madhab = Optional<Madhab>("Madhab");
        entity.RegistrationPolicy = Optional<EventRegistrationPolicy>("RegistrationPolicy");
        entity.ParticipationConfiguration = Optional<EventParticipationConfiguration>("ParticipationConfiguration");
        if (entity.ParticipationConfiguration is { } participation)
        {
            var value = row.GetProperty("ParticipationConfiguration");
            Navigation<ParticipationHandlingMode>(participation, value, "ParticipationHandlingMode");
            Navigation<AdvanceRegistrationObligation>(participation, value, "AdvanceRegistrationObligation");
            Navigation<IdentityAccessMode>(participation, value, "IdentityAccessMode");
        }
        var sessionValue = row.GetProperty("Session");
        var session = Materialize<EventSession>(context, sessionValue);
        Navigation<EventLocation>(session, sessionValue, "EventLocation");
        entity.SetDiscoveryOccurrence(session, row.GetProperty("MatchingCount").GetInt32());
        var presentationValue = row.GetProperty("Presentation");
        var presentation = presentationValue.Deserialize<EventDiscoveryPresentation>()
            ?? throw new InvalidOperationException("Native discovery public presentation is required.");
        // Keep the actual Domain URI parser; SQL cannot substitute for System.Uri's rules.
        // This scalar is a public profile reference, never a storage object key or provider credential.
        string? externalPicture = presentationValue.GetProperty("ActorExternalProfilePictureUri").GetString();
        if (externalPicture is not null)
            presentation = presentation with
            {
                ActorProfilePictureUri = ActorPii.IsValidProfilePicture(null, externalPicture) ? externalPicture : null
            };
        entity.SetDiscoveryPresentation(presentation);
        return entity;

        T? Optional<T>(string name) where T : class => row.GetProperty(name) is { ValueKind: not JsonValueKind.Null } value
            ? Materialize<T>(context, value) : null;

        void Navigation<T>(object owner, JsonElement value, string property) where T : class
        {
            object? target = value.GetProperty(property) is { ValueKind: not JsonValueKind.Null } nested
                ? Materialize<T>(context, nested) : null;
            context.Model.FindEntityType(owner.GetType())!.FindNavigation(property)!.PropertyInfo!.SetValue(owner, target);
        }
    }

    internal static T Materialize<T>(ExploreDbContext context, JsonElement row) where T : class
    {
        var entity = context.Model.FindEntityType(typeof(T))!;
        var properties = entity.GetProperties().ToArray();
        object?[] values = new object?[properties.Max(property => property.GetIndex()) + 1];
        foreach (var property in properties)
        {
            object? value = property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null;
            if (row.TryGetProperty(property.Name, out var json) && json.ValueKind != JsonValueKind.Null)
            {
                var converter = property.GetTypeMapping().Converter;
                Type type = converter?.ProviderClrType ?? property.ClrType;
                object? scalar = (Nullable.GetUnderlyingType(type) ?? type) == typeof(DateTime)
                    ? json.GetDateTimeOffset().UtcDateTime : json.Deserialize(type);
                value = converter is null ? scalar : converter.ConvertFromProvider(scalar);
            }
            values[property.GetIndex()] = value;
        }
        var materializer = context.GetService<IStructuralTypeMaterializerSource>().GetMaterializer(entity);
        return (T)materializer(new MaterializationContext(new ValueBuffer(values), context));
    }
}
