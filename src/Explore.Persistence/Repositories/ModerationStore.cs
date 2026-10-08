using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Owns the primary-store moderation transaction, exact receipt and qualified entity/effect persistence.</summary>
public sealed class ModerationStore(
    ExploreDbContext context, IModerationAuthorityContext authority, TimeProvider clock) : IModerationStore
{
    public async Task<ModerationOperationResult> ExecuteAsync(
        ModerationOperation operation,
        Func<IModerationSession, CancellationToken, Task<ModerationOperationResult>> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(execute);
        if (authority is not IModerationAuthorityParticipant participant || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId is not null || context.ChangeTracker.Entries().Any()
            || context.TenantFilterTenantId == Guid.Empty)
        {
            return ModerationOperationResult.Unavailable();
        }

        var tenantId = context.TenantFilterTenantId;
        var targetTenant = Ancestor(operation.Target, ModerationScopeKind.Tenant);
        if (targetTenant is not null && targetTenant != tenantId)
        {
            return ModerationOperationResult.Unavailable();
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Kind = (int)operation.Kind,
            SubjectKind = (int)operation.Subject.Kind,
            operation.Subject.Id,
            Origin = Coordinates(operation.Subject.Origin),
            Target = Coordinates(operation.Target),
            operation.OperationId,
            operation.EffectId,
            operation.DecisionId,
            operation.ExpectedRevision,
            operation.ExpectedPolicyRevision,
            operation.ReferenceId,
            operation.Proposal,
            operation.ExpectedSanctionRevision,
            operation.SupersededSanctionId,
            SupersededScope = operation.SupersededScope is { } supersededScope ? Coordinates(supersededScope) : null,
            Capability = operation.Capability is { } capability ? (int?)capability : null
        })));
        var session = new Session(context, operation, clock);
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                        || !await ProviderTenantMatchesAsync(tenantId, token))
                    {
                        throw new AbortOperation(ModerationOperationResult.Unavailable());
                    }

                    var resolution = await participant.AcquireAsync(session, operation, token);
                    if (resolution.Outcome != ModerationOperationOutcome.Known || resolution.Grant is not { } grant)
                    {
                        throw new AbortOperation(resolution.Outcome == ModerationOperationOutcome.Denied
                            ? ModerationOperationResult.Denied() : ModerationOperationResult.Unavailable());
                    }

                    if (grant.Subject != operation.Subject || grant.Purpose != operation.Kind
                        || !grant.Scope.Contains(operation.Target))
                    {
                        throw new AbortOperation(ModerationOperationResult.Denied());
                    }

                    session.Grant = grant;
                    context.ModerationTrustDomainId = grant.Scope.TrustDomainId;
                    if (operation.Kind != ModerationOperationKind.Evaluate)
                    {
                        await using var operationFence = await RelationalNamedLock.AcquireTransactionAsync(
                            context, $"moderation.operation:{operation.OperationId:D}", token);
                        var receipt = await context.ModerationOperationReceipts.AsNoTracking()
                            .SingleOrDefaultAsync(entity => entity.Id == operation.OperationId, token);
                        if (receipt is not null)
                        {
                            return ReceiptResult(receipt, operation, digest, grant);
                        }
                    }

                    session.Saved = false;
                    var result = await execute(session, token);
                    if (result.Outcome != ModerationOperationOutcome.Known)
                    {
                        throw new AbortOperation(result);
                    }
                    if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                        || !await ProviderTenantMatchesAsync(tenantId, token))
                    {
                        throw new AbortOperation(ModerationOperationResult.Unavailable());
                    }

                    if (operation.Kind != ModerationOperationKind.Evaluate)
                    {
                        if (!session.Saved || result.DecisionId is not { } decisionId || result.Revision is not { } revision
                            || session.Grant != grant)
                        {
                            throw new AbortOperation(ModerationOperationResult.Unavailable());
                        }

                        context.ModerationOperationReceipts.Add(ModerationOperationReceipt.Complete(
                            operation.OperationId, digest, operation.Target.TrustDomainId, (int)operation.Target.Kind,
                            operation.Target.Id, (int)operation.Subject.Kind, operation.Subject.Id, grant.Principal.Id,
                            PrincipalDigest(grant), decisionId, result.SanctionId, revision, grant.PolicyRevision, grant.QualificationRevision,
                            clock.GetUtcNow()));
                        await context.SaveChangesAsync(token);
                    }

                    return result;
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    await using var verification = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    if (operation.Kind == ModerationOperationKind.Evaluate
                        || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed
                        || !await ProviderTenantMatchesAsync(tenantId, token))
                    {
                        return false;
                    }

                    var resolution = await participant.AcquireAsync(session, operation, token);
                    if (resolution.Grant is not { } grant || resolution.Outcome != ModerationOperationOutcome.Known
                        || grant.Subject != operation.Subject || grant.Purpose != operation.Kind
                        || !grant.Scope.Contains(operation.Target))
                    {
                        return false;
                    }

                    context.ModerationTrustDomainId = grant.Scope.TrustDomainId;
                    var receipt = await context.ModerationOperationReceipts.AsNoTracking()
                        .SingleOrDefaultAsync(entity => entity.Id == operation.OperationId, token);
                    return receipt is not null
                        && ReceiptResult(receipt, operation, digest, grant).Outcome == ModerationOperationOutcome.Known;
                },
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortOperation aborted)
        {
            return aborted.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ModerationOperationResult.Conflict();
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            return ModerationOperationResult.Conflict();
        }
        finally
        {
            context.ModerationTrustDomainId = null;
            context.ChangeTracker.Clear();
        }
    }

    private async Task<bool> ProviderTenantMatchesAsync(Guid? tenantId, CancellationToken token)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
        {
            return true;
        }

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        var value = await command.ExecuteScalarAsync(token);
        if (tenantId is null)
        {
            return value is null or DBNull || value is string { Length: 0 };
        }
        return value is string setting && Guid.TryParse(setting, out var providerTenant) && providerTenant == tenantId;
    }

    private static ModerationOperationResult ReceiptResult(
        ModerationOperationReceipt receipt, ModerationOperation operation, string digest, ModerationAuthorityGrant grant) =>
        receipt.RequestDigest == digest && receipt.TrustDomainId == operation.Target.TrustDomainId
            && receipt.ScopeKind == (int)operation.Target.Kind && receipt.ScopeId == operation.Target.Id
            && receipt.SubjectKind == (int)operation.Subject.Kind && receipt.SubjectId == operation.Subject.Id
            && receipt.PrincipalId == grant.Principal.Id && receipt.PrincipalDigest == PrincipalDigest(grant)
                ? ModerationOperationResult.Applied(receipt.DecisionId, receipt.SanctionId, receipt.ResultRevision)
                : ModerationOperationResult.Conflict();

    private static string PrincipalDigest(ModerationAuthorityGrant grant) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Kind = (int)grant.Principal.Kind,
            grant.Principal.Id,
            Origin = Coordinates(grant.Principal.Origin)
        })));

    private static Guid? Ancestor(AuthorityScope scope, ModerationScopeKind kind)
    {
        for (AuthorityScope? current = scope; current is not null; current = current.Parent)
        {
            if (current.Kind == kind) return current.Id;
        }
        return null;
    }

    private static IReadOnlyList<object> Coordinates(AuthorityScope scope)
    {
        var coordinates = new List<object>();
        for (AuthorityScope? current = scope; current is not null; current = current.Parent)
        {
            coordinates.Add(new { Kind = (int)current.Kind, current.Id });
        }
        return coordinates.AsReadOnly();
    }

    private sealed class AbortOperation(ModerationOperationResult result) : Exception
    {
        public ModerationOperationResult Result { get; } = result;
    }

    private sealed class Session(
        ExploreDbContext db, ModerationOperation operation, TimeProvider clock) : IModerationSession
    {
        internal ModerationAuthorityGrant? Grant { get; set; }
        internal bool Saved { get; set; }

        public async Task<ModerationCase?> FindCaseAsync(
            Guid caseId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            var query = QualifiedSubject(db.ModerationCases, subject).Where(
                entity => entity.Id == caseId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                    && entity.ScopeKind == scope.Kind);
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(db, caseId, token);
            var allegation = await query.SingleOrDefaultAsync(token);
            allegation?.ValidatePersistedState();
            return allegation?.Subject == subject ? allegation : null;
        }

        public async Task<ModerationDecision?> FindDecisionAsync(
            Guid decisionId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            var query = QualifiedSubject(db.ModerationDecisions, subject).Where(
                entity => entity.Id == decisionId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                    && entity.ScopeKind == scope.Kind);
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(db, decisionId, token);
            var decision = await query.SingleOrDefaultAsync(token);
            decision?.ValidatePersistedState();
            return decision?.Subject == subject ? decision : null;
        }

        public async Task<ModerationSanction?> FindSanctionAsync(
            Guid sanctionId, SubjectReference subject, AuthorityScope scope, CancellationToken token)
        {
            var query = QualifiedSubject(db.ModerationSanctions, subject).Where(
                entity => entity.Id == sanctionId && entity.SubjectKind == subject.Kind && entity.SubjectId == subject.Id
                    && entity.ScopeTrustDomainId == scope.TrustDomainId
                    && entity.ScopeSolutionId == Ancestor(scope, ModerationScopeKind.Solution)
                    && entity.ScopeInstanceId == Ancestor(scope, ModerationScopeKind.Instance)
                    && entity.ScopeTenantId == Ancestor(scope, ModerationScopeKind.Tenant)
                    && entity.ScopeCommunityId == Ancestor(scope, ModerationScopeKind.Community)
                    && entity.ScopeResourceId == Ancestor(scope, ModerationScopeKind.Resource)
                    && entity.ScopeKind == scope.Kind);
            if (!await query.AnyAsync(token)) return null;
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationSanction>(db, sanctionId, token);
            var sanction = await query.SingleOrDefaultAsync(token);
            sanction?.ValidatePersistedState();
            return sanction?.Subject == subject ? sanction : null;
        }

        public async Task<IReadOnlyList<ModerationSanction>> ReadSanctionsAsync(SubjectReference subject, CancellationToken token)
        {
            var rows = await QualifiedSubject(db.ModerationSanctions, subject).AsNoTracking().ToListAsync(token);
            foreach (var row in rows) row.ValidatePersistedState();
            return rows.Where(row => row.Subject == subject && row.Scope.Contains(operation.Target)).ToArray();
        }

        public async Task<IReadOnlyList<ModerationProtectiveMeasure>> ReadProtectionAsync(
            SubjectReference subject, CancellationToken token)
        {
            var rows = await QualifiedSubject(db.ModerationProtectiveMeasures, subject).AsNoTracking().ToListAsync(token);
            foreach (var row in rows) row.ValidatePersistedState();
            return rows.Where(row => row.Subject == subject && row.Scope.Contains(operation.Target)).ToArray();
        }

        private static IQueryable<TEntity> QualifiedSubject<TEntity>(DbSet<TEntity> rows, SubjectReference subject)
            where TEntity : class =>
            rows.Where(entity =>
                EF.Property<ModerationSubjectKind>(entity, nameof(ModerationCase.SubjectKind)) == subject.Kind
                && EF.Property<Guid>(entity, nameof(ModerationCase.SubjectId)) == subject.Id
                && EF.Property<ModerationScopeKind>(entity, nameof(ModerationCase.SubjectOriginKind)) == subject.Origin.Kind
                && EF.Property<Guid>(entity, nameof(ModerationCase.SubjectOriginTrustDomainId)) == subject.TrustDomainId
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginSolutionId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Solution)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginInstanceId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Instance)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginTenantId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Tenant)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginCommunityId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Community)
                && EF.Property<Guid?>(entity, nameof(ModerationCase.SubjectOriginResourceId))
                    == Ancestor(subject.Origin, ModerationScopeKind.Resource));

        public Task SaveAsync(
            ModerationDecision decision, ModerationSanction? sanction, ModerationSanction? supersededSanction,
            ModerationAuthorityGrant grant,
            ModerationPlannedEffect effect, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Grant != grant || decision.Subject != operation.Subject || decision.Scope != operation.Target
                || effect.OperationId != operation.OperationId || effect.Id != operation.EffectId
                || effect.DecisionId != decision.Id || effect.DecisionRevision != decision.Revision
                || effect.SanctionId != sanction?.Id || effect.SanctionRevision != sanction?.Revision
                || effect.Subject != decision.Subject || effect.Scope != decision.Scope
                || effect.Kind != operation.Kind || Saved)
            {
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            }

            decision.ValidatePersistedState();
            sanction?.ValidatePersistedState();
            supersededSanction?.ValidatePersistedState();
            if (effect.SupersededSanctionId != supersededSanction?.Id
                || effect.SupersededSanctionRevision != supersededSanction?.Revision
                || supersededSanction is not null && (operation.Kind != ModerationOperationKind.Supersede
                    || supersededSanction.Id != operation.SupersededSanctionId
                    || supersededSanction.Scope != operation.SupersededScope
                    || supersededSanction.Subject != operation.Subject
                    || !grant.Scope.Contains(supersededSanction.Scope)
                    || db.Entry(supersededSanction).State == EntityState.Detached))
            {
                throw new AbortOperation(ModerationOperationResult.Unavailable());
            }
            if (db.Entry(decision).State == EntityState.Detached)
            {
                if (operation.Kind != ModerationOperationKind.Propose
                    || decision.Issuer != grant.Principal || decision.IssuingScope != grant.Scope
                    || operation.Proposal is not { } proposal || decision.CaseId != proposal.CaseId)
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                db.ModerationDecisions.Add(decision);
            }
            if (sanction is not null && db.Entry(sanction).State == EntityState.Detached)
            {
                if (operation.Kind is not (ModerationOperationKind.Activate or ModerationOperationKind.Supersede))
                    throw new AbortOperation(ModerationOperationResult.Unavailable());
                db.ModerationSanctions.Add(sanction);
            }
            db.ModerationEffectIntents.Add(ModerationEffectIntent.Create(
                effect.Id, effect.OperationId, effect.DecisionId, effect.DecisionRevision, effect.SanctionId,
                effect.SanctionRevision, effect.Scope.TrustDomainId, (int)effect.Scope.Kind, effect.Scope.Id,
                (int)effect.Subject.Kind, effect.Subject.Id, (int)effect.Kind,
                grant.PolicyRevision, grant.QualificationRevision, clock.GetUtcNow(),
                effect.SupersededSanctionId, effect.SupersededSanctionRevision));
            Saved = true;
            return Task.CompletedTask;
        }
    }
}
