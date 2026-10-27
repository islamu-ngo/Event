using System.Data;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Native case-purpose authority on the primary serializable connection. No protected payload,
/// responsible-human attribution or permission inferred from a public/disclosure capability.
/// </summary>
public sealed partial class ModerationCaseTargetProofOwner(
    ExploreDbContext context, ICurrentUserService caller, IMachinePrincipalAccessor machine,
    TimeProvider clock, IDataProtectionProvider? investigationProtection = null)
    : IModerationCaseTargetProofOwner, IModerationCaseTargetIssuer
{
    private HeldProof? _proof;
    private PendingWrite? _pending;
    private EventActorPlan? _submissionEventPlan;
    private Guid? _stateTransactionId;
    private readonly HashSet<(Type Type, Guid Id)> _fences = [];

    public async Task<ModerationCaseTargetObservation?> AcquireAsync(
        ModerationCaseAccessRequest request, ModerationCaseTargetReceipt authenticatedReceipt,
        Guid transactionId, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        ResetExpiredTransactionState();
        if (_proof is not null || observedAt > clock.GetUtcNow()
            || !Enum.IsDefined(request.Purpose))
            return null;
        if (_protectedReadPlan is { } readPlan)
        {
            if (!ProtectedReadRequestMatches(request, authenticatedReceipt)
                || !await ProtectedReadCoordinatesMatchAsync(cancellationToken)) return null;
            // Custody qualification has already acquired these exact original anchors before Tenant.
            foreach (var id in new[] { readPlan.Coordinates.PublisherActorId, readPlan.Coordinates.OrganizerActorId }
                .OfType<Guid>().Distinct().Order())
                if (!await FenceAsync<Actor>(id, true, cancellationToken)) return null;
        }
        var environment = await EnvironmentAsync(true, cancellationToken);
        if (environment is null || environment.TransactionId != transactionId
            || authenticatedReceipt.CallerUserId != environment.CallerId
            || authenticatedReceipt.HostBindingId != environment.HostId
            || authenticatedReceipt.QualificationEpochId != environment.EpochId
            || authenticatedReceipt.QualificationRevision != environment.EpochRevision)
            return null;
        var authority = await ReceiptAuthorityAsync(
            authenticatedReceipt, request.Purpose, request.CaseId, environment, true, observedAt, cancellationToken);
        if (authority is null) return null;
        var observation = new ModerationCaseTargetObservation(transactionId, request.Purpose,
            request.CaseId, request.OperationId, request.ExpectedCaseRevision, authenticatedReceipt.Id,
            environment.CallerId, environment.HostId, environment.EpochId, environment.EpochRevision,
            authenticatedReceipt.RelationshipSourceId, authenticatedReceipt.RelationshipGeneration,
            environment.Scope, authority.Source.Evidence(environment.Scope), observedAt, authenticatedReceipt.ExpiresAt);
        _proof = new(observation, authenticatedReceipt, environment, authority);
        return observation;
    }

    public async Task<ModerationCaseTargetObservation?> RecheckAsync(
        ModerationCaseTargetObservation acquired, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        if (_proof is not { } held || held.Observation != acquired
            || observedAt < acquired.ObservedAt || observedAt >= acquired.ValidUntil)
            return null;
        if (_protectedReadPlan is not null && !await ProtectedReadCoordinatesMatchAsync(cancellationToken)) return null;
        var environment = await EnvironmentAsync(false, cancellationToken);
        if (environment != held.Environment) return null;
        var authority = await ReceiptAuthorityAsync(held.Receipt, acquired.Purpose,
            acquired.CaseId, environment!, false, observedAt, cancellationToken);
        var completedAt = clock.GetUtcNow();
        return authority is not null && authority.Fingerprint == held.Authority.Fingerprint
            && completedAt >= observedAt && completedAt < acquired.ValidUntil
            ? acquired with { ObservedAt = observedAt } : null;
    }

    private async Task<ReceiptAuthority?> ReceiptAuthorityAsync(
        ModerationCaseTargetReceipt receipt, ModerationCaseAccessPurpose purpose, Guid caseId,
        NativeEnvironment environment, bool acquire, DateTimeOffset now, CancellationToken token)
    {
        if (receipt.TenantId != environment.TenantId || now < receipt.IssuedAt || now >= receipt.ExpiresAt
            || (purpose == ModerationCaseAccessPurpose.ReadAllegation)
                != (receipt.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant))
            return null;
        if (!await FenceAsync<ModerationCaseTargetSource>(receipt.SourceId, acquire, token)
            || !await FenceAsync<ModerationCaseTargetPolicy>(receipt.LifetimePolicyId, acquire, token))
            return null;
        var source = await context.ModerationCaseTargetSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receipt.SourceId, token);
        var policy = await context.ModerationCaseTargetPolicies.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receipt.LifetimePolicyId, token);
        if (source is null || policy is null || !policy.Enabled || policy.TargetKind != source.Kind
            || policy.Revision != receipt.LifetimePolicyRevision
            || source.Evidence(environment.Scope) != receipt.Evidence || source.RecordedAt > now)
            return null;
        if (_protectedReadPlan is { } protectedRead && ReadSourceCoordinates.From(source) != protectedRead.Sources.Source)
            return null;
        var lifetime = purpose == ModerationCaseAccessPurpose.SubmitAllegation
            ? policy.SubmissionLifetime : policy.ReadLifetime;
        if (receipt.ExpiresAt - receipt.IssuedAt > lifetime)
            return null;
        var native = await NativeTargetAsync(source.NativeKind, source.NativeTargetId,
            source.EventId, acquire, token);
        if (native is null || native.TargetVersion != source.NativeTargetVersion
            || native.EventVersion != source.EventVersion || native.Fingerprint != source.NativeFactsDigest)
            return null;
        var relationship = await RelationshipAsync(receipt.RelationshipKind, receipt.RelationshipSourceId,
            source, native, environment, caseId, acquire, now, token);
        if (relationship is null || relationship.Generation != receipt.RelationshipGeneration
            || receipt.RelationshipKind == ModerationCaseRelationshipKind.InstanceAdministrator
                && receipt.RelationshipFactsDigest != Digest(Encoding.UTF8.GetBytes(relationship.Fingerprint))
            || receipt.ExpiresAt > relationship.Until)
            return null;
        return new(source, $"{source.Version:D}|{policy.Id:D}|{policy.Revision}|{native.Fingerprint}|{relationship.Fingerprint}");
    }

    private async Task<NativeEnvironment?> EnvironmentAsync(
        bool acquire, CancellationToken token, Guid? submissionEventId = null, Guid? readGrantId = null)
    {
        var transaction = context.Database.CurrentTransaction;
        if (!context.Database.IsRelational() || transaction is null
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || !caller.IsAuthenticated || machine.Current is not null
            || caller.UserId is not { } userId || userId == Guid.Empty
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return null;
        if (context.Database.ProviderName == RelationalNamedLock.PostgreSqlProvider)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
            if (await command.ExecuteScalarAsync(token) is not string value
                || !Guid.TryParse(value, out var tenant) || tenant != tenantId)
                return null;
        }
        if (!await AccountAsync(userId, acquire, token)) return null;
        if (acquire && readGrantId is { } grantId
            && !await AcquireReadIssuancePlanAsync(grantId, userId, tenantId, token))
            return null;
        if (acquire && submissionEventId is { } eventId
            && !await AcquireSubmissionEventActorsAsync(eventId, tenantId, token))
            return null;
        if (!await FenceAsync<Tenant>(tenantId, acquire, token)
            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId, token))
            return null;
        var installed = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (installed is null || context.ModerationTrustDomainId is { } trust && trust != installed.TrustDomainId
            || !await FenceAsync<ModerationHostBinding>(installed.Id, acquire, token))
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == installed.Id, token);
        if (_readIssuancePlan is { } readPlan && (host is null || ReadHostCoordinates.From(host) != readPlan.Host))
            return null;
        if (_protectedReadPlan is { } protectedRead && (host is null || ReadHostCoordinates.From(host) != protectedRead.Sources.Host))
            return null;
        if (host is null || !await FenceAsync<ServicePrincipal>(host.MaintenanceServicePrincipalId, acquire, token)
            || !await context.ServicePrincipals.AsNoTracking()
                .AnyAsync(row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token))
            return null;
        context.ModerationTrustDomainId = host.TrustDomainId;
        var discovered = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.HostBindingId == host.Id, token);
        if (discovered is null || !await FenceAsync<ModerationQualificationEpoch>(discovered.Id, acquire, token))
            return null;
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id && row.HostBindingId == host.Id, token);
        return epoch is not null && epoch.Revision > 0
            && ReferenceEquals(transaction, context.Database.CurrentTransaction)
            && caller.IsAuthenticated && caller.UserId == userId && machine.Current is null
            && context.TenantFilterTenantId == tenantId
            ? new(transaction.TransactionId, userId, tenantId, host.Id, host.TrustDomainId,
                host.SolutionId, host.InstanceId, host.MaintenanceServicePrincipalId, epoch.Id, epoch.Revision) : null;
    }

    private async Task<bool> AccountAsync(Guid id, bool acquire, CancellationToken token) =>
        await FenceAsync<User>(id, acquire, token)
        && await context.Users.AsNoTracking().AnyAsync(row => row.Id == id && !row.IsDeleted, token)
        && !await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == id, token);

    private void ResetExpiredTransactionState()
    {
        var transactionId = context.Database.CurrentTransaction?.TransactionId;
        if (_stateTransactionId == transactionId) return;
        _proof = null;
        _pending = null;
        _submissionEventPlan = null;
        _readIssuancePlan = null;
        _protectedReadPlan = null;
        _fences.Clear();
        _stateTransactionId = transactionId;
    }

    private async Task<bool> FenceAsync<TEntity>(Guid id, bool acquire, CancellationToken token) where TEntity : class
    {
        if (id == Guid.Empty) return false;
        if (!acquire) return _fences.Contains((typeof(TEntity), id));
        if (!await context.Set<TEntity>().AsNoTracking()
            .AnyAsync(row => EF.Property<Guid>(row, "Id") == id, token))
            return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<TEntity>(context, id, token);
        _fences.Add((typeof(TEntity), id));
        return true;
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // PostgreSQL and MySQL persist microseconds; never let round-trip precision extend a grant.
    private static DateTimeOffset PersistedTime(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);

    private sealed record NativeEnvironment(
        Guid TransactionId, Guid CallerId, Guid TenantId, Guid HostId, Guid TrustId, Guid SolutionId,
        Guid InstanceId, Guid MaintenanceServiceId, Guid EpochId, long EpochRevision)
    {
        public AuthorityScope Scope => AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(TrustId), SolutionId), InstanceId), TenantId);
    }

    private sealed record NativeTarget(
        Guid TargetVersion, Guid? EventVersion, Guid? SubmitterId, Guid? ActorId = null,
        Guid? ActorVersion = null, Guid? ParentId = null, Guid? ParentVersion = null,
        Guid? ProviderId = null, Guid? ProviderVersion = null, Guid? SchemaId = null, string? SchemaHash = null,
        string? StorageProvider = null, Guid? EventId = null, string? IdentityFacts = null)
    {
        public string Fingerprint => Digest(Encoding.UTF8.GetBytes(
            $"v2|{TargetVersion:D}|{EventId:D}|{EventVersion:D}|{SubmitterId:D}|{ActorId:D}|{ActorVersion:D}|" +
            $"{ParentId:D}|{ParentVersion:D}|{ProviderId:D}|{ProviderVersion:D}|{SchemaId:D}|{SchemaHash}|{StorageProvider}|{IdentityFacts}"));
    }

    private sealed record Relationship(ModerationCaseRelationshipGeneration Generation, DateTimeOffset Until, string Fingerprint);
    private sealed record ReceiptAuthority(ModerationCaseTargetSource Source, string Fingerprint);
    private sealed record HeldProof(ModerationCaseTargetObservation Observation, ModerationCaseTargetReceipt Receipt,
        NativeEnvironment Environment, ReceiptAuthority Authority);
    private sealed record PendingWrite(
        NativeEnvironment Environment, DateTimeOffset NotBefore, DateTimeOffset ValidUntil,
        Func<CancellationToken, Task<bool>> Recheck);
}
