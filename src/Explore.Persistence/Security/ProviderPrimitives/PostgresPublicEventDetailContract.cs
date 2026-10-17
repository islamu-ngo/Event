using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// An exact-tenant public detail read, not an event-table or management capability.
/// The original Runtime context retains its lease and Serializable transaction.
/// </summary>
public static partial class PostgresPublicEventDetailContract
{
    public const string OwnerRole = "event_public_detail_owner";
    public const string MigratorRole = "event_public_detail_migrator";
    public const string RuntimeRole = "event_public_detail_runtime";
    private const string Routine = "public_event_detail";
    private const string Signature = "uuid,uuid,text";
    private const string Policy = "public_event_detail_read";
    private const string Stamp = "public-event-detail:v1:";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("public_detail_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    /// <summary>Attests the compiled body and declared privilege graph; never installs or repairs them.</summary>
    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("public_detail_original_open_connection_required");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = AdmissionSql(context, enrollment);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<Event?> ReadAsync(ExploreDbContext context, PublicEventDiscoveryReadBinding binding,
        Guid? id, string? code, CancellationToken token)
    {
        binding.RequireOriginalOwner(context);
        if (!context.Database.IsNpgsql() || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("public_detail_original_postgresql_owner_required");
        if ((id is null) == (code is null) || id == Guid.Empty || code is { Length: 0 or > 128 })
            throw new ArgumentException("Public detail requires one nonempty event ID or public code.");
        if (context.Database.CurrentTransaction is null)
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                binding.RequireOriginalOwner(context);
                await using var owned = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var result = await ReadAsync(context, binding, id, code, token);
                await owned.CommitAsync(token);
                return result;
            });
        }

        binding.RequireSerializableOwner(context);
        var transaction = context.Database.CurrentTransaction.GetDbTransaction();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = AdmissionSql(context, binding.Enrollment)
            + $"\nSELECT {FunctionSql(context)}(@tenant,@id,@code)::text;";
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = context.TenantFilterTenantId!.Value });
        command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = (object?)id ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("code", NpgsqlDbType.Text) { Value = (object?)code ?? DBNull.Value });
        string? payload = await command.ExecuteScalarAsync(token) as string;
        binding.RequireSerializableOwner(context);
        if (!ReferenceEquals(transaction, context.Database.CurrentTransaction!.GetDbTransaction()))
            throw new UnauthorizedAccessException("public_detail_transaction_substitution");
        if (payload is null) return null;
        using var document = JsonDocument.Parse(payload);
        return Materialize(context, document.RootElement);
    }

    private static Event Materialize(ExploreDbContext context, JsonElement row)
    {
        var entity = PublicEventDiscoveryProviderOperations.Materialize<Event>(context, row);
        entity.EventType = Optional<EventType>(row, "EventType");
        entity.EventProvenanceType = Optional<EventProvenanceType>(row, "EventProvenanceType");
        entity.AudienceGender = Optional<AudienceGender>(row, "AudienceGender");
        entity.AudienceAge = Optional<AudienceAge>(row, "AudienceAge");
        entity.EventStatus = Optional<EventStatus>(row, "EventStatus")!;
        entity.VisibilityType = Optional<VisibilityType>(row, "VisibilityType")!;
        entity.EventFormat = Optional<EventFormat>(row, "EventFormat")!;
        entity.Madhab = Optional<Madhab>(row, "Madhab");
        entity.RegistrationPolicy = Optional<EventRegistrationPolicy>(row, "RegistrationPolicy");
        entity.AtprotoRecord = Optional<AtprotoRecord>(row, "AtprotoRecord");
        entity.Actor = Optional<Actor>(row, "Actor")!;
        if (entity.Actor is { } actor)
        {
            var value = row.GetProperty("Actor");
            actor.ActorType = Optional<ActorType>(value, "ActorType")!;
            actor.Pii = Optional<ActorPii>(value, "Pii")!;
        }
        entity.OrganizerActor = Optional<Actor>(row, "OrganizerActor");
        entity.IslamicAspect = Optional<EventIslamicAspect>(row, "IslamicAspect");
        if (entity.IslamicAspect is { } islamic)
        {
            var value = row.GetProperty("IslamicAspect");
            islamic.Madhab = Optional<Madhab>(value, "Madhab");
            islamic.PrimaryLanguage = Optional<Language>(value, "PrimaryLanguage");
        }
        entity.TechAspect = Optional<EventTechAspect>(row, "TechAspect");
        entity.ParticipationConfiguration = Optional<EventParticipationConfiguration>(row, "ParticipationConfiguration");
        if (entity.ParticipationConfiguration is { } participation)
        {
            var value = row.GetProperty("ParticipationConfiguration");
            Navigation<ParticipationHandlingMode>(participation, value, "ParticipationHandlingMode");
            Navigation<AdvanceRegistrationObligation>(participation, value, "AdvanceRegistrationObligation");
            Navigation<IdentityAccessMode>(participation, value, "IdentityAccessMode");
            if (Optional<ParticipationRequirementAttachment>(row, "QuestionnaireAttachment") is { } attachment)
            {
                var questionnaire = row.GetProperty("QuestionnaireAttachment");
                Navigation<RegistrationRequirement>(attachment, questionnaire, "RegistrationRequirement");
                Navigation<RegistrationFormVersion>(attachment, questionnaire, "RegistrationFormVersion");
                context.Model.FindEntityType(typeof(EventParticipationConfiguration))!
                    .FindNavigation(nameof(EventParticipationConfiguration.RequirementAttachments))!
                    .GetCollectionAccessor()!.Add(participation, attachment, forMaterialization: true);
            }
        }
        foreach (var value in row.GetProperty("PublicActions").EnumerateArray())
        {
            var action = PublicEventDiscoveryProviderOperations.Materialize<EventPublicAction>(context, value);
            action.Event = entity;
            action.EventPublicActionKind = Optional<EventPublicActionKind>(value, "EventPublicActionKind");
            action.HealthState = Optional<EventPublicActionHealthState>(value, "HealthState");
            context.Model.FindEntityType(typeof(Event))!.FindNavigation(nameof(Event.PublicActions))!
                .GetCollectionAccessor()!.Add(entity, action, forMaterialization: true);
        }
        foreach (var value in row.GetProperty("TicketCatalogVersions").EnumerateArray())
        {
            var catalog = PublicEventDiscoveryProviderOperations.Materialize<EventTicketCatalogVersion>(context, value);
            foreach (var ticketValue in value.GetProperty("TicketTypes").EnumerateArray())
            {
                var ticket = PublicEventDiscoveryProviderOperations.Materialize<EventTicketType>(context, ticketValue);
                context.Model.FindEntityType(typeof(EventTicketCatalogVersion))!.FindNavigation(nameof(EventTicketCatalogVersion.TicketTypes))!
                    .GetCollectionAccessor()!.Add(catalog, ticket, forMaterialization: true);
            }
            context.Model.FindEntityType(typeof(Event))!.FindNavigation(nameof(Event.TicketCatalogVersions))!
                .GetCollectionAccessor()!.Add(entity, catalog, forMaterialization: true);
        }
        var presentation = row.GetProperty("Presentation").Deserialize<EventDiscoveryPresentation>()
            ?? throw new InvalidOperationException("public_detail_presentation_required");
        if (Optional<EventModerationRecord>(row, "LatestModeration") is { } latestModeration)
            presentation = presentation with { IsUnmoderationEligible = latestModeration.AllowsUnmoderation };
        string? external = entity.Actor?.Pii?.ExternalProfilePictureUri;
        if (external is not null && ActorPii.IsValidProfilePicture(null, external))
            presentation = presentation with { ActorProfilePictureUri = external };
        entity.SetDiscoveryPresentation(presentation);
        MaterializeProgram(context, entity, row);
        return entity;

        T? Optional<T>(JsonElement value, string property) where T : class =>
            value.GetProperty(property) is { ValueKind: not JsonValueKind.Null } nested
                ? PublicEventDiscoveryProviderOperations.Materialize<T>(context, nested) : null;

        void Navigation<T>(object owner, JsonElement value, string property) where T : class =>
            context.Model.FindEntityType(owner.GetType())!.FindNavigation(property)!.PropertyInfo!
                .SetValue(owner, Optional<T>(value, property));
    }

    private static string AdmissionSql(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        context.ValidateTenantLease();
        var definition = Define(context, enrollment);
        string tenant = context.TenantFilterTenantId is { } selected ? Text(selected.ToString()) + "::uuid" : "NULL::uuid";
        return $"""
            DO $ready$ BEGIN
                {definition.Guard}
                {ExactBody(context, definition.Body)}
                {PostgresPrivilegeGuard.RestrictedCallerSql}
                IF session_user <> {Text(enrollment.RuntimeLogin)} OR current_user <> session_user
                    OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
                    OR {tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid
                    OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}
                THEN RAISE EXCEPTION 'public_detail_caller_mismatch' USING ERRCODE = '42501'; END IF;
            END $ready$;
            """;
    }

    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(" + Signature + ")")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.prorettype = 'jsonb'::regtype AND NOT f.proretset
                AND f.proargdefaults IS NULL AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                AND f.proargnames = ARRAY['p_tenant','p_id','p_code']::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'public_detail_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
