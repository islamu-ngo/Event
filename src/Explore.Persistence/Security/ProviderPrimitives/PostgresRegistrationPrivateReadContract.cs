using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>Execute-only registration reads; no Runtime source grants or role switching.</summary>
public static partial class PostgresRegistrationPrivateReadContract
{
    public const string OwnerRole = "registration_private_read_owner";
    public const string MigratorRole = "registration_private_read_migrator";
    public const string RuntimeRole = "registration_private_read_runtime";
    private const string Routine = "registration_private_read";
    private const string Signature = "uuid,uuid,integer,uuid,uuid,uuid,uuid,uuid,uuid,uuid,text,boolean,timestamp with time zone";
    private const string Arguments = "p_tenant uuid,p_user uuid,p_kind integer,p_event uuid,p_order uuid,p_line uuid,p_participant uuid,p_assignment uuid,p_ticket uuid,p_transfer uuid,p_hash text,p_manage boolean,p_at timestamp with time zone";
    private const string Policy = "registration_private_read";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];
    private const string Stamp = "registration-private-read:v1:";

    internal static RecoveryOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal sealed class RecoveryOperations(ExploreDbContext context)
    {
        // The caller has authenticated the protected original challenge. Its canonical
        // digest is the only bearer authority; recovery never refreshes an order deadline.
        internal Task<RegistrationOrder?> ReadGuestRecoveryAsync(
            PostgresNativeQueueEnrollment enrollment, Func<ISerializableAttemptOwner> resolveAttempt,
            DateTime evaluatedAt, Guid orderId, Guid tenantId, Guid eventId,
            CapabilityTokenHash digest, CancellationToken token)
        {
            context.ValidateTenantLease();
            var originalContext = context.ContextId;
            if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || tenantId == Guid.Empty || context.BindTenantLease() != tenantId)
                throw new UnauthorizedAccessException("Guest recovery requires its original selected tenant.");
            var request = new RegistrationPrivateRead(RegistrationPrivateReadKind.GuestRecovery, eventId, orderId);
            var admission = new RegistrationPrivateReadAdmission(tenantId, null, digest.Value, false, evaluatedAt);
            return context.Database.CurrentTransaction is null
                ? resolveAttempt().ExecuteAsync(ReadOriginalAsync, token)
                : ReadOriginalAsync(token);

            async Task<RegistrationOrder?> ReadOriginalAsync(CancellationToken cancellation)
            {
                var connection = RelationalConnectionIdentity.Capture(context);
                var transaction = context.Database.CurrentTransaction;
                var graph = await ReadAsync(context, enrollment, request, admission, cancellation);
                context.ValidateTenantLease();
                if (context.ContextId != originalContext || !connection.Matches(context)
                    || connection.State != ConnectionState.Open
                    || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                    || context.TenantFilterTenantId != tenantId
                    || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                    || transaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
                    || !connection.Owns(transaction.GetDbTransaction()))
                    throw new UnauthorizedAccessException("Guest recovery cannot substitute its original physical owner.");
                return graph?.Order;
            }
        }
    }

    public static string FunctionSql(ExploreDbContext context) => new HumanPrincipalSourceSql(context).Name(Routine);

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("registration_private_read_requires_postgresql");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await context.Database.ExecuteSqlRawAsync(BuildSql(context, enrollment), cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    internal static async Task<RegistrationPrivateGraph?> ReadAsync(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, RegistrationPrivateRead request,
        RegistrationPrivateReadAdmission admission, CancellationToken token)
    {
        context.ValidateTenantLease();
        if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != admission.TenantId
            || admission.TenantId == Guid.Empty || context.Database.GetDbConnection().State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("registration_private_read_original_owner_required");
        var definition = Define(context, enrollment);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        command.CommandText = $"""
            DO $admit$ BEGIN
                {definition.Guard} {ExactBody(context, definition.Body)}
                {Caller(enrollment, "session_user", Text(admission.TenantId.ToString("D")) + "::uuid")}
            END $admit$;
            SELECT {FunctionSql(context)}(@tenant,@user,@kind,@event,@order,@line,@participant,@assignment,
                @ticket,@transfer,@hash,@manage,@at)::text;
            """;
        void Add(string name, NpgsqlDbType type, object? value) =>
            command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        Add("tenant", NpgsqlDbType.Uuid, admission.TenantId);
        Add("user", NpgsqlDbType.Uuid, admission.PrincipalId);
        Add("kind", NpgsqlDbType.Integer, (int)request.Kind);
        Add("event", NpgsqlDbType.Uuid, request.EventId);
        Add("order", NpgsqlDbType.Uuid, request.OrderId);
        Add("line", NpgsqlDbType.Uuid, request.LineId);
        Add("participant", NpgsqlDbType.Uuid, request.ParticipantId);
        Add("assignment", NpgsqlDbType.Uuid, request.AssignmentId);
        Add("ticket", NpgsqlDbType.Uuid, request.TicketId);
        Add("transfer", NpgsqlDbType.Uuid, request.TransferId);
        Add("hash", NpgsqlDbType.Text, admission.CapabilityHash);
        Add("manage", NpgsqlDbType.Boolean, admission.CanManageAttendees);
        Add("at", NpgsqlDbType.TimestampTz, admission.EvaluatedAt);
        object? payload = await command.ExecuteScalarAsync(token);
        if (payload is null or DBNull) return null;
        using var document = JsonDocument.Parse((string)payload);
        return Materialize(context, request, document.RootElement);
    }

    private sealed record Definition(GraphSql Sql, PostgresPrivilegeGuard.Contract Privileges,
        IReadOnlyDictionary<Type, string> Policies, string Guard, string Body);

    private static Definition Define(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        enrollment.Validate();
        if (new[] { enrollment.RuntimeLogin, enrollment.GlobalLogin, enrollment.MigratorLogin,
                enrollment.EmailInstanceMutationLogin, enrollment.EmailTenantMutationLogin }.Any(Roles.Contains))
            throw new InvalidOperationException("registration_private_read_enrollment_collision");
        var s = new GraphSql(context);
        string selection = Selection(s);
        string projections = string.Join("\n", Enum.GetValues<RegistrationPrivateReadKind>().Select(kind =>
            $"WHEN {(int)kind} THEN RETURN {Projection(s, kind)};"));
        var policies = Policies(context, s);
        var privileges = new PostgresPrivilegeGuard.Contract(s.Names.Schema, Roles,
            [new(Routine, Signature, OwnerRole, [RuntimeRole])],
            s.Sources.Select(source => new PostgresPrivilegeGuard.Relation(
                context.Model.FindEntityType(source.Key)!.GetTableName()!, OwnerRole, [],
                source.Value.Order(StringComparer.Ordinal).Select(property =>
                    new PostgresPrivilegeGuard.Column(s.Names.ColumnName(source.Key, property), ["SELECT"])).ToArray())).ToArray(),
            [new(OwnerRole, MigratorRole, false, true, false),
                new(MigratorRole, enrollment.MigratorLogin, false, true, false),
                new(RuntimeRole, enrollment.RuntimeLogin, true, false, false)]);
        string guard = PostgresPrivilegeGuard.BuildAssertSql(privileges)
            + SourceGuard(context, s, enrollment) + PolicyGuard(s, policies) + CallerPrivileges(s, enrollment);
        string body = $"""
            DECLARE v_order uuid; v_entry uuid; v_offer uuid; v_supply uuid; v_binding uuid;
            BEGIN
                {guard}
                {Caller(enrollment, Text(OwnerRole), "p_tenant")}
                IF p_kind IS NULL OR p_kind NOT BETWEEN 0 AND {(int)RegistrationPrivateReadKind.GuestRecovery}
                    OR p_event IS NULL OR p_at IS NULL OR p_manage IS NULL
                    OR (p_user IS NULL AND p_manage)
                THEN RAISE EXCEPTION 'registration_private_read_selector_invalid' USING ERRCODE = '22023'; END IF;
                {selection}
                CASE p_kind
                    {projections}
                    ELSE RAISE EXCEPTION 'registration_private_read_unknown_operation' USING ERRCODE = '22023';
                END CASE;
            END;
            """;
        return new(s, privileges, policies, guard, body);
    }

    private static string Selection(GraphSql s)
    {
        string Order(string property) => s.Column<RegistrationOrder>("o", property);
        string Transfer(string property) => s.Column<AdmissionTicketTransfer>("x", property);
        string Ticket(string property) => s.Column<AdmissionTicket>("t", property);
        string Participant(string property) => s.Column<RegistrationParticipant>("p", property);
        string Eligibility(string property) => s.Column<ParticipantAdmissionEligibility>("a", property);
        string transfer = $"""
            {s.Live<AdmissionTicketTransfer>("x")} AND {Transfer("Id")} = p_transfer
                AND {Transfer("EventId")} = p_event AND {Transfer("AdmissionTicketId")} = p_ticket
                AND {Transfer("RegistrationOrderId")} = {Order("Id")}
            """;
        string ticket = $"{s.Live<AdmissionTicket>("t")} AND {Ticket("Id")} = p_ticket AND {Ticket("EventId")} = p_event AND {Ticket("RegistrationOrderId")} = v_order";
        string participant = $"{s.Live<RegistrationParticipant>("p")} AND {Participant("RegistrationOrderId")} = v_order";
        string readinessParticipant = participant + $" AND {Participant("Id")} = p_participant";
        string eligibility = $"""
            {s.Live<ParticipantAdmissionEligibility>("a")} AND {Eligibility("EventId")} = p_event
                AND {Eligibility("RegistrationOrderId")} = v_order AND {Eligibility("ParticipantId")} = p_participant
                AND {Eligibility("RegistrationTicketAssignmentId")} = p_assignment
            """;
        string hash = $"{Order("GuestAccessTokenHash")} COLLATE \"C\" = p_hash COLLATE \"C\"";
        string unexpired = $"({Order("ExpiresAt")} IS NULL OR {Order("ExpiresAt")} > p_at)";
        string account = $"p_user IS NOT NULL AND {Order("AccountUserId")} = p_user";
        string subject = $"""
            p_user IS NOT NULL AND ({account} OR p_manage
                OR EXISTS (SELECT 1 FROM {s.Table<RegistrationParticipant>()} p WHERE {readinessParticipant} AND {Participant("LinkedUserId")} = p_user)
                OR EXISTS (SELECT 1 FROM {s.Table<ParticipantAdmissionEligibility>()} a WHERE {eligibility} AND {Eligibility("SubjectUserId")} = p_user))
            """;
        string transferAuthority = $"""
            EXISTS (SELECT 1 FROM {s.Table<AdmissionTicketTransfer>()} x WHERE {transfer} AND
                (({Transfer("StatusId")} = {(int)AdmissionTicketTransferStatus.Offered} AND {Transfer("ExpiresAt")} >= p_at
                    AND {Transfer("CapabilityDigest")} COLLATE "C" = p_hash COLLATE "C")
                OR p_user IS NOT NULL AND ({account}
                    OR EXISTS (SELECT 1 FROM {s.Table<AdmissionTicket>()} t WHERE {ticket} AND {Ticket("HolderSubjectUserId")} = p_user)
                    OR EXISTS (SELECT 1 FROM {s.Table<RegistrationParticipant>()} p WHERE {participant}
                        AND {Participant("Id")} IN ({Transfer("FromParticipantId")},{Transfer("ToParticipantId")})
                        AND {Participant("LinkedUserId")} = p_user))))
            """;
        return $"""
            SELECT {Order("Id")} INTO v_order FROM {s.Table<RegistrationOrder>()} o
                WHERE {s.Live<RegistrationOrder>("o")} AND {Order("EventId")} = p_event
                    AND ((p_kind = {(int)RegistrationPrivateReadKind.Transfer}
                        AND EXISTS (SELECT 1 FROM {s.Table<AdmissionTicketTransfer>()} x WHERE {transfer}))
                    OR (p_kind <> {(int)RegistrationPrivateReadKind.Transfer} AND {Order("Id")} = p_order))
                ;
            IF v_order IS NULL THEN RETURN NULL; END IF;
            IF p_kind = {(int)RegistrationPrivateReadKind.Readiness} THEN
                PERFORM 1 FROM {s.Table<RegistrationParticipant>()} p WHERE {readinessParticipant};
                IF NOT FOUND THEN RETURN NULL; END IF;
                PERFORM 1 FROM {s.Table<ParticipantAdmissionEligibility>()} a WHERE {eligibility};
                IF NOT FOUND THEN RETURN NULL; END IF;
            END IF;
            IF p_kind = {(int)RegistrationPrivateReadKind.Transfer} THEN
                PERFORM 1 FROM {s.Table<AdmissionTicketTransfer>()} x
                    WHERE {s.Live<AdmissionTicketTransfer>("x")} AND {Transfer("Id")} = p_transfer;
                PERFORM 1 FROM {s.Table<AdmissionTicket>()} t WHERE {ticket};
                IF NOT FOUND THEN RETURN NULL; END IF;
                PERFORM 1 FROM {s.Table<RegistrationParticipant>()} p
                    WHERE {participant} AND {Participant("Id")} = (SELECT {Transfer("FromParticipantId")}
                        FROM {s.Table<AdmissionTicketTransfer>()} x WHERE {Transfer("Id")} = p_transfer AND {s.Live<AdmissionTicketTransfer>("x")})
                    ;
                IF NOT FOUND THEN RETURN NULL; END IF;
                PERFORM 1 FROM {s.Table<RegistrationParticipant>()} p
                    WHERE {participant} AND {Participant("Id")} = (SELECT {Transfer("ToParticipantId")}
                        FROM {s.Table<AdmissionTicketTransfer>()} x WHERE {Transfer("Id")} = p_transfer AND {s.Live<AdmissionTicketTransfer>("x")})
                    ;
            END IF;
            IF NOT EXISTS (SELECT 1 FROM {s.Table<RegistrationOrder>()} o
                WHERE {s.Live<RegistrationOrder>("o")} AND {Order("Id")} = v_order AND CASE p_kind
                    WHEN {(int)RegistrationPrivateReadKind.AddOns} THEN ({account}) OR ({Order("AccountUserId")} IS NULL AND {hash})
                    WHEN {(int)RegistrationPrivateReadKind.Waitlist} THEN ({account}) OR ({hash})
                    WHEN {(int)RegistrationPrivateReadKind.Readiness} THEN ({subject}) OR ({hash} AND {unexpired})
                    WHEN {(int)RegistrationPrivateReadKind.PurchaseOrder} THEN {hash} AND {unexpired}
                    WHEN {(int)RegistrationPrivateReadKind.AccountOrder} THEN {account}
                    WHEN {(int)RegistrationPrivateReadKind.GuestRecovery} THEN p_user IS NULL AND NOT p_manage AND {hash}
                    WHEN {(int)RegistrationPrivateReadKind.Transfer} THEN {transferAuthority}
                    ELSE false END)
                THEN RETURN NULL; END IF;
            {WaitlistSelection(s)}
            """;
    }

    private static string Caller(PostgresNativeQueueEnrollment enrollment, string owner, string tenant) => $"""
        {PostgresPrivilegeGuard.RestrictedCallerSql}
        IF current_user <> {owner} OR session_user <> {Text(enrollment.RuntimeLogin)}
            OR pg_catalog.current_setting('role') NOT IN ('none',session_user::text)
            OR pg_catalog.current_setting('transaction_isolation') <> 'serializable'
            OR pg_catalog.current_setting('session_replication_role') <> 'origin'
            OR {tenant} IS NULL OR {tenant} = '00000000-0000-0000-0000-000000000000'::uuid
            OR NULLIF(pg_catalog.current_setting('app.current_tenant_id',true),'')::uuid IS DISTINCT FROM {tenant}
        THEN RAISE EXCEPTION 'registration_private_read_caller_mismatch' USING ERRCODE = '42501'; END IF;
        """;

    private static string ExactBody(ExploreDbContext context, string body) => $"""
        IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_proc f
            WHERE f.oid = pg_catalog.to_regprocedure({Text(FunctionSql(context) + "(" + Signature + ")")})
                AND f.provolatile = 'v' AND f.proparallel = 'u' AND NOT f.proleakproof AND NOT f.proisstrict
                AND f.prorettype = 'jsonb'::regtype AND NOT f.proretset AND f.proargdefaults IS NULL
                AND f.provariadic = 0 AND f.proallargtypes IS NULL AND f.proargmodes IS NULL
                AND f.proargnames = ARRAY['p_tenant','p_user','p_kind','p_event','p_order','p_line','p_participant',
                    'p_assignment','p_ticket','p_transfer','p_hash','p_manage','p_at']::text[]
                AND f.prolang = (SELECT oid FROM pg_catalog.pg_language WHERE lanname = 'plpgsql')
                AND pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(f.prosrc,'UTF8')),'hex') = {Text(Hash(body))})
        THEN RAISE EXCEPTION 'registration_private_read_body_drift' USING ERRCODE = '55000'; END IF;
        """;

    private static string Boolean(bool value) => value ? "true" : "false";
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
