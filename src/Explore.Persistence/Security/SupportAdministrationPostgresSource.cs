using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Native = Explore.Persistence.Security.PostgresSupportAdministrationContract;

namespace Explore.Persistence.Security;

/// <summary>Retains the original ordinary Runtime owner and its closed DATA question.</summary>
internal sealed class SupportAdministrationPostgresSource : ISupportAdministrationSource
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease execution;
    private readonly PostgresNativeQueueEnrollment enrollment;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly Guid tenant;
    private readonly HumanCommitSubject subject;
    private Native.Request? request;
    private string? originalImage;
    private bool acquired;
    private static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false };

    public SupportAdministrationPostgresSource(ExploreDbContext context,
        ExploreDbContextExecutionLease execution, PostgresNativeQueueEnrollment enrollment)
    {
        execution.RequireOriginalOwner(context);
        if (!context.Database.IsNpgsql() || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || execution.PolicyPurpose is not null)
            throw new NotSupportedException("Support DATA requires its ordinary idle PostgreSQL Runtime owner.");
        subject = execution.HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("Support DATA requires its original human.");
        if (subject.AccountKey is null || execution.HumanLifetime is not { } lifetime
            || lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("Support DATA requires original credential custody.");
        tenant = context.BindTenantLease()
            ?? throw new NotSupportedException("Global support DATA is not an original Runtime owner.");
        this.context = context;
        this.execution = execution;
        this.enrollment = enrollment;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
    }

    public void Select(StartSupportAccessSessionCommand operation) => Bind(new("Start", subject.PrincipalId, tenant,
        TargetTenantId: operation.TargetTenantId, TargetTenantUserId: operation.TargetTenantUserId,
        ModeId: (int)operation.Mode, DurationMinutes: operation.DurationMinutes,
        ReasonCode: operation.ReasonCode.Trim(), ReasonText: operation.ReasonText.Trim(),
        TicketReference: operation.TicketReference?.Trim() ?? string.Empty));
    public void Select(StopSupportAccessSessionCommand operation) => Bind(new("OwnStop", subject.PrincipalId,
        tenant, operation.SessionId, EndReasonText: Normalize(operation.EndReasonText)));
    public void Select(ForceStopSupportAccessSessionCommand operation) => Bind(new("ForceStop", subject.PrincipalId,
        tenant, operation.SessionId, EndReasonText: Normalize(operation.EndReasonText)));
    public void Select(ListSupportAccessSessionsQuery operation) => Bind(new("List", subject.PrincipalId, tenant,
        TargetTenantId: operation.TargetTenantId, Limit: Math.Clamp(operation.Limit, 1, PaginatedResult<object>.MaxPageSize)));
    public void Select(GetSupportAccessAuditEventsQuery operation) => Bind(new("Audit", subject.PrincipalId, tenant,
        operation.SessionId, TargetTenantId: operation.TargetTenantId,
        Limit: Math.Clamp(operation.Limit, 1, PaginatedResult<object>.MaxPageSize)));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Bind(Native.Request selected)
    {
        RequireSelection();
        if (request is not null) throw new InvalidOperationException("Support DATA selection cannot replay.");
        request = selected;
    }

    public async Task<SupportAdministrationSessionFact?> ObserveAsync(Guid? sessionId, CancellationToken cancellationToken)
    {
        RequireSelection();
        if (request is null || request.SessionId != sessionId || originalImage is not null
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Support DATA observation requires one original idle question.");
        bool opened = connection.State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            originalImage = await Native.ReadAsync(context, enrollment, request, cancellationToken);
            RequireSelection();
            return SessionFact(originalImage, sessionId);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }

    public async Task<ISupportAdministrationLease> AcquireAsync(Guid? sessionId, IPolicySourceSetLease sourceSet,
        CancellationToken cancellationToken)
    {
        var transaction = RequireOwner();
        if (acquired || request is null || originalImage is null || request.SessionId != sessionId)
            throw new InvalidOperationException("Support DATA acquisition requires its one original observation.");
        acquired = true;
        await sourceSet.ValidateAsync(cancellationToken);
        string image = await Native.AcquireAsync(context, enrollment, request, originalImage, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("The original support DATA owner changed.");
        return new Lease(this, sourceSet, transaction, request, image);
    }

    private void RequireSelection()
    {
        execution.RequireOriginalOwner(context);
        context.ValidateTenantLease();
        if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || execution.PolicyPurpose is not null || !ReferenceEquals(execution.HumanScope?.Subject, subject)
            || !connection.Matches(context))
            throw new InvalidOperationException("Original support DATA selection changed.");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (!context.HumanCommitAttemptOwnsTransaction || System.Transactions.Transaction.Current is not null
            || connection.State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable || !connection.Owns(transaction))
            throw new InvalidOperationException("Support DATA requires the original non-replaying Serializable owner.");
        return transaction;
    }

    private SupportAdministrationSessionFact? SessionFact(string image, Guid? id) =>
        id is null ? null : Rows<SupportAccessSession>(image, "Sessions").Where(row => row.Id == id)
            .Select(SupportAdministrationSessionFact.From).SingleOrDefault();

    internal static T Materialize<T>(ExploreDbContext context, JsonElement row) where T : class, new()
    {
        // Hydrate only the mapped CLR properties of a new detached Domain entity.
        // EntityEntry setters can enlist a write; no tracker entry is created here.
        var entity = new T();
        var metadata = context.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException("The original support Domain mapping is absent.");
        foreach (var property in metadata.GetProperties())
        {
            var member = property.PropertyInfo
                ?? throw new InvalidOperationException("Support DATA does not admit shadow properties.");
            member.SetValue(entity, row.GetProperty(property.Name).Deserialize(property.ClrType, Json));
        }
        return entity;
    }

    private IReadOnlyList<T> Rows<T>(string image, string field) where T : class, new()
    {
        using var document = JsonDocument.Parse(image);
        return document.RootElement.GetProperty("Data").GetProperty(field).EnumerateArray()
            .Select(row => Materialize<T>(context, row)).ToArray();
    }

    private sealed class Lease(SupportAdministrationPostgresSource source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, Native.Request request, string acquiredImage) : ISupportAdministrationLease, ISupportAdministrationData
    {
        private string image = acquiredImage;
        private bool applied;
        private bool validated;
        private bool completed;
        private Guid? effectSessionId;
        private Guid? effectAuditId;
        public SupportAdministrationSessionFact? Original { get; } = source.SessionFact(acquiredImage, request.SessionId);
        public ISupportAdministrationData NativeData => this;

        private void Require(string? operation = null)
        {
            if (completed || !ReferenceEquals(transaction, source.RequireOwner())
                || operation is not null && request.Operation != operation)
                throw new InvalidOperationException("The original support DATA operation ended or changed.");
        }

        public Task<SupportAdministrationStartTargets> ReadStartTargetsAsync(DateTimeOffset transitionAt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require("Start");
            using var data = JsonDocument.Parse(image);
            var facts = data.RootElement.GetProperty("Data");
            bool matches = request.TargetTenantUserId is null || facts.GetProperty("Members").EnumerateArray()
                .Any(row => row.GetProperty("TenantId").GetGuid() == request.TargetTenantId);
            return Task.FromResult(new SupportAdministrationStartTargets(facts.GetProperty("Targets").GetArrayLength() == 1,
                matches, source.Rows<SupportAccessSession>(image, "Sessions").Any(row => row.IsActiveAt(transitionAt))));
        }

        public Task<SupportAccessSession?> ReadSessionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require();
            if (request.Operation is not ("OwnStop" or "ForceStop" or "Audit"))
                throw new InvalidOperationException("This original operation has no single session read.");
            var session = source.Rows<SupportAccessSession>(image, "Sessions").SingleOrDefault();
            if (request.Operation == "OwnStop" && session?.ActorUserId != request.ActorId
                || request.Operation == "Audit" && session?.TargetTenantId != request.TargetTenantId)
                session = null;
            return Task.FromResult(session);
        }

        public Task<IReadOnlyList<SupportAccessSession>> ListSessionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require("List");
            return Task.FromResult(source.Rows<SupportAccessSession>(image, "Sessions"));
        }

        public Task<IReadOnlyList<SupportAccessAuditEvent>> ListAuditAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require("Audit");
            return Task.FromResult(source.Rows<SupportAccessAuditEvent>(image, "Audits"));
        }

        public Task SaveStartedAsync(SupportAccessSession session, SupportAccessAuditEvent audit, DateTimeOffset transitionAt,
            CancellationToken cancellationToken) => Save("Start", session, audit, transitionAt, cancellationToken);
        public Task SaveOwnStoppedAsync(SupportAccessSession session, SupportAccessAuditEvent audit, DateTimeOffset transitionAt,
            CancellationToken cancellationToken) => Save("OwnStop", session, audit, transitionAt, cancellationToken);
        public Task SaveForceStoppedAsync(SupportAccessSession session, SupportAccessAuditEvent audit, DateTimeOffset transitionAt,
            CancellationToken cancellationToken) => Save("ForceStop", session, audit, transitionAt, cancellationToken);

        private async Task Save(string operation, SupportAccessSession session, SupportAccessAuditEvent audit,
            DateTimeOffset transitionAt, CancellationToken token)
        {
            Require(operation);
            if (applied || validated) throw new InvalidOperationException("Support DATA effects cannot replay.");
            var before = source.Rows<SupportAccessSession>(image, "Sessions").SingleOrDefault(row => row.Id == session.Id);
            if (operation == "Start"
                ? session.CreatedAt != default || session.CreatedBy is not null || session.UpdatedAt is not null
                    || session.UpdatedBy is not null || session.ConcurrencyStamp != Guid.Empty
                : before is null || session.CreatedAt != before.CreatedAt || session.CreatedBy != before.CreatedBy
                    || session.UpdatedAt != before.UpdatedAt || session.UpdatedBy != before.UpdatedBy
                    || session.ConcurrencyStamp != before.ConcurrencyStamp)
                throw new UnauthorizedAccessException("Support DATA metadata differed from its original Domain image.");
            var expected = SupportAdministrationSessionFact.From(session);
            Guid stamp = Guid.CreateVersion7();
            await Native.CompileAsync(source.context, source.enrollment, request,
                new(transitionAt.ToUniversalTime(), session.Id, audit.Id, stamp), token);
            string persisted = await Native.ApplyPlanAsync(source.context, source.enrollment, request, token);
            Require(operation);
            var stored = source.Rows<SupportAccessSession>(persisted, "Sessions").Single(row => row.Id == session.Id);
            var evidence = source.Rows<SupportAccessAuditEvent>(persisted, "Audits").Single();
            if (SupportAdministrationSessionFact.From(stored) != (expected with { ConcurrencyStamp = stamp })
                || SupportAdministrationAuditFact.From(evidence) != SupportAdministrationAuditFact.From(audit))
                throw new UnauthorizedAccessException("Native support effects differed from the original Domain transition.");
            session.ConcurrencyStamp = stored.ConcurrencyStamp;
            session.CreatedAt = stored.CreatedAt;
            session.CreatedBy = stored.CreatedBy;
            session.UpdatedAt = stored.UpdatedAt;
            session.UpdatedBy = stored.UpdatedBy;
            image = persisted;
            effectSessionId = session.Id;
            effectAuditId = audit.Id;
            applied = true;
        }

        public async Task<SupportAdministrationPersistedFacts> ValidateAsync(Guid? sessionId, Guid? auditId,
            CancellationToken cancellationToken)
        {
            Require();
            if (validated) throw new InvalidOperationException("Support DATA final validation cannot replay.");
            if (effectSessionId.HasValue && (sessionId != effectSessionId || auditId != effectAuditId))
                throw new UnauthorizedAccessException("Native support effects require the original prepared completion.");
            await sourceSet.ValidateAsync(cancellationToken);
            if (!applied)
            {
                await Native.CompileAsync(source.context, source.enrollment, request, new(), cancellationToken);
                image = await Native.ApplyPlanAsync(source.context, source.enrollment, request, cancellationToken);
                applied = true;
            }
            string current = await Native.ReadAsync(source.context, source.enrollment, request, cancellationToken);
            using var expected = JsonDocument.Parse(image);
            using var actual = JsonDocument.Parse(current);
            if (!JsonElement.DeepEquals(expected.RootElement, actual.RootElement))
                throw new UnauthorizedAccessException("The full support DATA image changed.");
            await sourceSet.ValidateAsync(cancellationToken);
            Require();
            validated = true;
            var audit = auditId is null ? null : source.Rows<SupportAccessAuditEvent>(current, "Audits")
                .Where(row => row.Id == auditId).Select(SupportAdministrationAuditFact.From).SingleOrDefault();
            return new(source.SessionFact(current, sessionId), audit);
        }

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            Require();
            if (!validated) throw new InvalidOperationException("Support DATA requires original final source admission.");
            await Native.CompleteAsync(source.context, source.enrollment, request, image, cancellationToken);
            Require();
            completed = true;
        }
    }
}
