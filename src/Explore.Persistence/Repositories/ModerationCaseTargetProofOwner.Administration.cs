using Explore.Domain;
using Explore.Application.Contracts.Moderation;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    public async Task<ModerationCaseTargetPolicy?> SetLifetimePolicyAsync(
        ModerationCaseTargetKind kind, long expectedRevision, TimeSpan submissionLifetime,
        TimeSpan readLifetime, bool enabled, CancellationToken cancellationToken)
    {
        ResetExpiredTransactionState();
        if (_pending is not null || !Enum.IsDefined(kind) || expectedRevision < 0)
            return null;
        var environment = await EnvironmentAsync(true, cancellationToken);
        if (environment is null) return null;
        var admin = await AdministratorAsync(environment, environment.CallerId, null, null, true, cancellationToken);
        if (admin is null) return null;
        await using var policyFence = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.case-policy:{environment.TenantId:D}:{(int)kind}", cancellationToken);
        var policy = await context.ModerationCaseTargetPolicies.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TargetKind == kind, cancellationToken);
        if (policy is null)
        {
            if (expectedRevision != 0 || !enabled) return null;
            policy = ModerationCaseTargetPolicy.Adopt(Guid.CreateVersion7(), environment.TenantId,
                environment.TrustId, kind, submissionLifetime, readLifetime, environment.CallerId);
            context.ModerationCaseTargetPolicies.Add(policy);
        }
        else
        {
            if (!await FenceAsync<ModerationCaseTargetPolicy>(policy.Id, true, cancellationToken)) return null;
            policy = await context.ModerationCaseTargetPolicies.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == policy.Id, cancellationToken);
            if (policy is null || policy.Revision != expectedRevision
                || context.ChangeTracker.Entries<ModerationCaseTargetPolicy>().Any(row => row.Entity.Id == policy.Id))
                return null;
            context.Attach(policy);
            policy.Change(expectedRevision, submissionLifetime, readLifetime, enabled, environment.CallerId);
        }
        _fences.Add((typeof(ModerationCaseTargetPolicy), policy.Id));
        var id = policy.Id;
        var revision = policy.Revision;
        _pending = new(environment, clock.GetUtcNow(), DateTimeOffset.MaxValue, async token =>
        {
            if (await AdministratorAsync(environment, environment.CallerId, admin.GrantId, admin.Platform, false, token) != admin)
                return false;
            return await context.ModerationCaseTargetPolicies.AsNoTracking().AnyAsync(row =>
                row.Id == id && row.Revision == revision && row.TargetKind == kind && row.Enabled == enabled
                && row.ChangedByUserId == environment.CallerId
                && row.SubmissionLifetimeTicks == submissionLifetime.Ticks && row.ReadLifetimeTicks == readLifetime.Ticks, token);
        });
        return policy;
    }

    public async Task<ModerationCaseReadGrant?> GrantReadAsync(
        ModerationCaseReadGrantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResetExpiredTransactionState();
        if (_pending is not null || request.OperationId == Guid.Empty || request.OperationId.Version != 7
            || request.OperationId.Variant is < 8 or > 11 || request.ExpectedCaseRevision < 1
            || request.ValidUntil == DateTimeOffset.MaxValue
            || request.ValidUntil.UtcTicks % TimeSpan.TicksPerMicrosecond != 0) return null;
        var caseId = request.CaseId;
        var sourceId = request.SourceId;
        var callerUserId = request.CallerUserId;
        var requestedUntil = PersistedTime(request.ValidUntil);
        var environment = await EnvironmentAsync(true, cancellationToken);
        if (environment is null) return null;
        await using var operationFence = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.read-grant:{environment.TenantId:D}:{request.OperationId:D}", cancellationToken);
        var original = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.OperationId, cancellationToken);
        if (original is not null && (original.IssuerUserId != environment.CallerId
            || original.CaseId != caseId || original.SourceId != sourceId || original.CallerUserId != callerUserId
            || original.CaseRevision != request.ExpectedCaseRevision || original.ExpiresAt != requestedUntil
            || original.Revision != 1 || !original.IsCurrentAt(clock.GetUtcNow())
            || !await FenceAsync<ModerationCaseReadGrant>(original.Id, true, cancellationToken)))
            return null;
        var admin = await AdministratorAsync(environment, environment.CallerId,
            original?.IssuerRoleGrantId, original?.IssuerPlatformGrant, true, cancellationToken);
        if (admin is null || !await AccountAsync(callerUserId, true, cancellationToken)
            || !await FenceAsync<ModerationCaseTargetSource>(sourceId, true, cancellationToken)
            || !await FenceAsync<ModerationCase>(caseId, true, cancellationToken))
            return null;
        var source = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId, cancellationToken);
        var allegation = await context.ModerationCases.AsNoTracking().Include(row => row.Evidence)
            .SingleOrDefaultAsync(row => row.Id == caseId, cancellationToken);
        if (source is null || allegation is null || allegation.Subject is not null
            || allegation.Revision != request.ExpectedCaseRevision
            || allegation.Scope != environment.Scope || allegation.Target != source.Evidence(environment.Scope).Target
            || !allegation.Evidence.Any(row => row.ToReference(allegation.Target!, allegation.OccurrenceId)
                == source.Evidence(environment.Scope)))
            return null;
        var native = await NativeTargetAsync(source.NativeKind, source.NativeTargetId, source.EventId, true, cancellationToken);
        var policy = await PolicyAsync(source.Kind, true, cancellationToken);
        var now = PersistedTime(clock.GetUtcNow());
        if (native is null || native.Fingerprint != source.NativeFactsDigest
            || native.TargetVersion != source.NativeTargetVersion || native.EventVersion != source.EventVersion || policy is null
            || source.RecordedAt > now || requestedUntil <= now
            || requestedUntil - (original?.IssuedAt ?? now) > policy.ReadLifetime)
            return null;
        var grant = original ?? ModerationCaseReadGrant.Grant(request.OperationId, environment.TenantId,
            environment.TrustId, caseId, allegation.Revision, sourceId, callerUserId, environment.CallerId,
            admin.GrantId, admin.Platform, now, requestedUntil);
        if (original is null) context.ModerationCaseReadGrants.Add(grant);
        _fences.Add((typeof(ModerationCaseReadGrant), grant.Id));
        var issuedAt = grant.IssuedAt;
        var grantId = grant.Id;
        _pending = new(environment, now, requestedUntil, async token =>
        {
            if (clock.GetUtcNow() < now || clock.GetUtcNow() >= requestedUntil
                || !await AccountAsync(callerUserId, false, token)
                || await AdministratorAsync(environment, environment.CallerId, admin.GrantId, admin.Platform, false, token) != admin
                || await NativeTargetAsync(source.NativeKind, source.NativeTargetId, source.EventId, false, token) != native)
                return false;
            var currentPolicy = await PolicyAsync(source.Kind, false, token);
            var currentSource = await context.ModerationCaseTargetSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == sourceId, token);
            var currentCase = await context.ModerationCases.AsNoTracking().Include(row => row.Evidence)
                .SingleOrDefaultAsync(row => row.Id == caseId, token);
            return currentPolicy?.Id == policy.Id && currentPolicy.Revision == policy.Revision
                && currentSource is not null && currentSource.Evidence(environment.Scope) == source.Evidence(environment.Scope)
                && currentSource.NativeFactsDigest == source.NativeFactsDigest && currentSource.RecordedAt == source.RecordedAt
                && currentSource.Kind == source.Kind && currentSource.NativeKind == source.NativeKind
                && currentSource.NativeTargetId == source.NativeTargetId && currentSource.EventId == source.EventId
                && currentSource.NativeTargetVersion == source.NativeTargetVersion && currentSource.EventVersion == source.EventVersion
                && currentSource.MaterialDigest == source.MaterialDigest && currentSource.OccurrenceDigest == source.OccurrenceDigest
                && currentCase is not null && currentCase.Revision == request.ExpectedCaseRevision && currentCase.Subject is null
                && currentCase.Scope == environment.Scope && currentCase.Target == allegation.Target
                && currentCase.Evidence.Any(row => row.ToReference(currentCase.Target!, currentCase.OccurrenceId)
                    == source.Evidence(environment.Scope))
                && await context.ModerationCaseReadGrants.AsNoTracking().AnyAsync(row =>
                    row.Id == grantId && row.Revision == 1 && row.RevokedAt == null
                    && row.CaseId == caseId && row.CaseRevision == request.ExpectedCaseRevision
                    && row.SourceId == sourceId && row.CallerUserId == callerUserId
                    && row.IssuerUserId == environment.CallerId && row.IssuerRoleGrantId == admin.GrantId
                    && row.IssuerPlatformGrant == admin.Platform && row.IssuedAt == issuedAt && row.ExpiresAt == requestedUntil, token);
        });
        return grant;
    }

    public async Task<ModerationCaseReadGrant?> RevokeReadAsync(
        Guid grantId, Guid operationId, long expectedRevision, CancellationToken cancellationToken)
    {
        ResetExpiredTransactionState();
        if (_pending is not null || operationId == Guid.Empty || operationId.Version != 7
            || operationId.Variant is < 8 or > 11 || expectedRevision != 1) return null;
        var environment = await EnvironmentAsync(true, cancellationToken);
        if (environment is null) return null;
        await using var operationFence = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.read-revoke:{environment.TenantId:D}:{operationId:D}", cancellationToken);
        var original = await context.ModerationCaseReadGrantRevocationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == operationId, cancellationToken);
        if (original is not null && (original.GrantId != grantId || original.ExpectedRevision != expectedRevision
            || original.AdministratorUserId != environment.CallerId
            || !await FenceAsync<ModerationCaseReadGrantRevocationReceipt>(operationId, true, cancellationToken)))
            return null;
        var admin = await AdministratorAsync(environment, environment.CallerId,
            original?.AdministratorRoleGrantId, original?.AdministratorPlatformGrant, true, cancellationToken);
        if (admin is null || !await FenceAsync<ModerationCaseReadGrant>(grantId, true, cancellationToken)) return null;
        var grant = await context.ModerationCaseReadGrants.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == grantId, cancellationToken);
        if (grant is null || context.ChangeTracker.Entries<ModerationCaseReadGrant>().Any(row => row.Entity.Id == grantId))
            return null;
        var now = PersistedTime(clock.GetUtcNow());
        if (original is null)
        {
            if (grant.Revision != expectedRevision || grant.RevokedAt is not null || now < grant.IssuedAt)
                return null;
            context.Attach(grant);
            grant.Revoke(expectedRevision, now);
            original = ModerationCaseReadGrantRevocationReceipt.Record(operationId, grant, expectedRevision,
                environment.CallerId, admin.GrantId, admin.Platform);
            context.ModerationCaseReadGrantRevocationReceipts.Add(original);
        }
        else if (grant.Revision != expectedRevision + 1 || grant.RevokedAt != original.RevokedAt)
            return null;
        var revokedAt = original.RevokedAt;
        var grantTuple = (grant.CaseId, grant.CaseRevision, grant.SourceId, grant.CallerUserId,
            grant.IssuerUserId, grant.IssuerRoleGrantId, grant.IssuerPlatformGrant, grant.IssuedAt, grant.ExpiresAt);
        _pending = new(environment, now, DateTimeOffset.MaxValue, async token =>
            await AdministratorAsync(environment, environment.CallerId, admin.GrantId, admin.Platform, false, token) == admin
            && await context.ModerationCaseReadGrants.AsNoTracking().AnyAsync(row =>
                row.Id == grantId && row.Revision == expectedRevision + 1 && row.RevokedAt == revokedAt
                && row.CaseId == grantTuple.CaseId && row.CaseRevision == grantTuple.CaseRevision && row.SourceId == grantTuple.SourceId
                && row.CallerUserId == grantTuple.CallerUserId && row.IssuerUserId == grantTuple.IssuerUserId
                && row.IssuerRoleGrantId == grantTuple.IssuerRoleGrantId && row.IssuerPlatformGrant == grantTuple.IssuerPlatformGrant
                && row.IssuedAt == grantTuple.IssuedAt && row.ExpiresAt == grantTuple.ExpiresAt, token)
            && await context.ModerationCaseReadGrantRevocationReceipts.AsNoTracking().AnyAsync(row =>
                row.Id == operationId && row.GrantId == grantId && row.ExpectedRevision == expectedRevision
                && row.AdministratorUserId == environment.CallerId && row.AdministratorRoleGrantId == admin.GrantId
                && row.AdministratorPlatformGrant == admin.Platform && row.RevokedAt == revokedAt, token));
        return grant;
    }
}
