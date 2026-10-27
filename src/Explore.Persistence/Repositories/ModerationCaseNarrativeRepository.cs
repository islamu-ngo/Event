using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
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
/// Finite confidential custody on the primary store. Uses the host's existing persistent
/// Data Protection authority with a distinct purpose and exact row binding; no key fallback.
/// Qualification and reporter/read authority are independent and both survive final flush.
/// </summary>
public sealed partial class ModerationCaseNarrativeRepository(
    ExploreDbContext context, IAdminContext admin, ICurrentUserService caller, IMachinePrincipalAccessor machine,
    IModerationCaseIntakeRepository intake, TimeProvider clock, IDataProtectionProvider? protection = null)
    : IModerationCaseNarrativeCustody
{
    private Lease? lease;
    private ModerationCaseNarrative? staged;
    private ModerationCaseIntakeAcknowledgement? retainedAcceptance;

    public Task<bool> AcquireAsync(Guid receiptId, CancellationToken cancellationToken) =>
        AcquireCoreAsync(receiptId, null, cancellationToken);

    private async Task<bool> AcquireCoreAsync(Guid receiptId, ModerationCaseReadSourcePlan? protectedRead,
        CancellationToken cancellationToken)
    {
        lease = null;
        staged = null;
        retainedAcceptance = null;
        var transaction = context.Database.CurrentTransaction;
        if (protection is null || !EnvironmentCurrent(transaction) || caller.UserId is not { } user
            || context.TenantFilterTenantId is not { } tenant) return false;
        ModerationSourceAuthority? authority = null;
        if (protectedRead is not null)
        {
            if (protectedRead.TransactionId != transaction!.TransactionId || protectedRead.CallerUserId != user
                || protectedRead.ReceiptId != receiptId || protectedRead.Scope.Kind != ModerationScopeKind.Tenant
                || protectedRead.Scope.Id != tenant) return false;
            // The native qualifier takes original issuer/role and Actor anchors before any Tenant fence.
            authority = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireCaseNarrativeAsync(protectedRead.Scope, protectedRead.TargetKind, cancellationToken,
                    protectedRead: protectedRead);
            if (authority is null) return false;
        }
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null || context.ModerationTrustDomainId is { } trust && trust != host.TrustDomainId
            || protectedRead is not null && (host.Id != protectedRead.HostBindingId
                || host.MaintenanceServicePrincipalId != protectedRead.MaintenanceServicePrincipalId)) return false;
        context.ModerationTrustDomainId = host.TrustDomainId;
        // These coordinates select discovery only. Stage/Read independently authenticate the
        // token and actual relationship/material using the native target owner.
        var receipt = await context.ModerationCaseTargetReceipts.AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == receiptId && row.CallerUserId == user && row.TenantId == tenant
            && row.HostBindingId == host.Id, cancellationToken);
        if (receipt is null) return false;
        receipt.ValidatePersistedState();
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId), tenant);
        if (protectedRead is not null && (protectedRead.Scope != scope || protectedRead.TargetKind != receipt.TargetKind)) return false;
        authority ??= await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireCaseNarrativeAsync(scope, receipt.TargetKind, cancellationToken);
        if (authority is null || authority.UserId != user || authority.Binding.Id != host.Id
            || authority.Epoch.Id != receipt.QualificationEpochId || authority.Epoch.Revision != receipt.QualificationRevision)
            return false;
        var policy = await new ModerationCaseNarrativePolicyReader(context, clock)
            .ReadAsync(authority, receipt.TargetKind, cancellationToken);
        var now = clock.GetUtcNow();
        if (policy is null || now < receipt.IssuedAt || now >= receipt.ExpiresAt
            || !EnvironmentCurrent(transaction) || caller.UserId != user || context.TenantFilterTenantId != tenant)
            return false;
        lease = new(transaction!, authority, policy.Value.Source, policy.Value.Adoption, receipt, now);
        return true;
    }

    public async Task<bool> StageAsync(
        ModerationCaseIntakeStage stage, ReadOnlyMemory<byte> material, CancellationToken cancellationToken)
    {
        if (lease is not { } held || staged is not null || retainedAcceptance is not null || protection is null
            || !EnvironmentCurrent(held.Transaction) || stage.Receipt.Id != held.Receipt.Id
            || stage.Receipt.Evidence != held.Receipt.Evidence || stage.Case.Target != held.Receipt.Evidence.Target
            || stage.Case.Scope != held.Authority.Tenant || stage.Case.Subject is not null
            || material.Length < 1 || material.Length > held.Source.MaximumMaterialBytes)
            return false;
        var owned = material.ToArray();
        try
        {
            var source = await context.ModerationCaseTargetSources.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == held.Receipt.SourceId && row.TenantId == held.Authority.Tenant.Id
                && row.TrustDomainId == held.Authority.Binding.TrustDomainId, cancellationToken);
            if (source is null || source.Evidence(held.Authority.Tenant) != held.Receipt.Evidence
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(owned), Convert.FromHexString(source.MaterialDigest)))
                return false;
            // Original acceptance is the anti-resurrection floor, surviving physical payload
            // deletion. A new token/operation for copied material cannot recreate purged text.
            var original = await (from acknowledgement in context.ModerationCaseIntakeAcknowledgements.AsNoTracking()
                                  join receipt in context.ModerationCaseTargetReceipts.AsNoTracking() on acknowledgement.ReceiptId equals receipt.Id
                                  where acknowledgement.TenantId == held.Authority.Tenant.Id && acknowledgement.CaseId == stage.Case.Id
                                      && receipt.SourceId == source.Id && receipt.SourceVersion == source.Version
                                      && receipt.MaterialFactId == source.MaterialFactId
                                  orderby acknowledgement.AcceptedAt, acknowledgement.Id
                                  select acknowledgement).FirstOrDefaultAsync(cancellationToken);
            if (original is not null)
            {
                // A new qualified reporter still receives an acceptance. Reuse the permanent
                // material floor internally without revealing deduplication or renewing custody.
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseIntakeAcknowledgement>(
                    context, original.Id, cancellationToken);
                original.ValidatePersistedState();
                retainedAcceptance = original;
                return true;
            }
            var actualNow = clock.GetUtcNow();
            var now = new DateTimeOffset(actualNow.UtcTicks - actualNow.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
            if (actualNow < held.ObservedAt || actualNow >= held.Adoption.ValidUntil || actualNow >= held.Receipt.ExpiresAt) return false;
            var remaining = held.Adoption.ValidUntil - now;
            var until = now.Add(held.Source.Terms.MaximumRetention < remaining ? held.Source.Terms.MaximumRetention : remaining);
            var purgeAt = new DateTimeOffset(until.UtcTicks - until.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
            if (purgeAt <= actualNow) return false;
            var id = Guid.CreateVersion7();
            var cipher = Protector(id, stage.Case.Id, held.Receipt.TenantId, held.Source.TrustDomainId,
                held.Receipt.SourceId, held.Receipt.SourceVersion, held.Receipt.MaterialFactId,
                held.Source.Id, held.Adoption.Id, now, purgeAt, held.Receipt.HostBindingId,
                held.Receipt.CallerUserId, held.Receipt.Id, held.Receipt.QualificationEpochId,
                held.Receipt.QualificationRevision, held.Source.WriterGeneration, held.Adoption.WriterGeneration).Protect(owned);
            try
            {
                staged = ModerationCaseNarrative.Capture(id, stage.Case.Id, stage.Receipt,
                    held.Source, held.Adoption, now, purgeAt, cipher);
                context.Set<ModerationCaseNarrative>().Add(staged);
            }
            finally { CryptographicOperations.ZeroMemory(cipher); }
            return true;
        }
        catch (CryptographicException) { return false; }
        finally { CryptographicOperations.ZeroMemory(owned); }
    }

    public async Task<bool> RecheckAsync(CancellationToken cancellationToken)
    {
        if (!await PolicyCurrentAsync(cancellationToken) || context.ChangeTracker.HasChanges())
            return false;
        if (retainedAcceptance is { } original)
            return await context.ModerationCaseIntakeAcknowledgements.AsNoTracking().AnyAsync(acceptance =>
                acceptance.Id == original.Id && acceptance.CaseId == original.CaseId
                && acceptance.ReceiptId == original.ReceiptId && acceptance.RequestDigest == original.RequestDigest
                && acceptance.EvidenceCaseRevision == original.EvidenceCaseRevision
                && acceptance.CaseRevision == original.CaseRevision && acceptance.AcceptedAt == original.AcceptedAt, cancellationToken);
        if (staged is not { } row) return false;
        var retained = await context.Set<ModerationCaseNarrative>().AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == row.Id && item.TenantId == row.TenantId && item.TrustDomainId == row.TrustDomainId, cancellationToken);
        return retained is not null && retained.CaseId == row.CaseId && retained.ReceiptId == row.ReceiptId
            && retained.SourceId == row.SourceId && retained.SourceVersion == row.SourceVersion
            && retained.MaterialFactId == row.MaterialFactId && retained.PolicySourceId == row.PolicySourceId
            && retained.AdoptionId == row.AdoptionId && retained.CapturedAt == row.CapturedAt && retained.PurgeAt == row.PurgeAt
            && retained.ReporterUserId == row.ReporterUserId && retained.HostBindingId == row.HostBindingId
            && retained.QualificationEpochId == row.QualificationEpochId && retained.QualificationRevision == row.QualificationRevision
            && retained.PolicyWriterGeneration == row.PolicyWriterGeneration
            && retained.AdoptionWriterGeneration == row.AdoptionWriterGeneration
            && CryptographicOperations.FixedTimeEquals(retained.ProtectedMaterial.Span, row.ProtectedMaterial.Span)
            && clock.GetUtcNow() >= row.CapturedAt && clock.GetUtcNow() < row.PurgeAt
            && EnvironmentCurrent(lease?.Transaction) && caller.UserId == lease?.Authority.UserId;
    }

    private async Task<bool> PolicyCurrentAsync(CancellationToken token)
    {
        if (lease is not { } held || !EnvironmentCurrent(held.Transaction) || caller.UserId != held.Authority.UserId
            || context.TenantFilterTenantId != held.Authority.Tenant.Id
            || context.ModerationTrustDomainId != held.Authority.Binding.TrustDomainId
            || await admin.ResolveUserIdAsync(token) != held.Authority.UserId
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(held.Authority.Tenant.Id, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == held.Authority.Epoch.Id && row.HostBindingId == held.Authority.Binding.Id
                && row.TenantId == held.Authority.Tenant.Id && row.Revision == held.Receipt.QualificationRevision, token))
            return false;
        var current = await new ModerationCaseNarrativePolicyReader(context, clock)
            .ReadAsync(held.Authority, held.Receipt.TargetKind, token);
        var now = clock.GetUtcNow();
        return current is { } actual && actual.Source.Id == held.Source.Id && actual.Adoption.Id == held.Adoption.Id
            && actual.Source.WriterGeneration == held.Source.WriterGeneration
            && actual.Adoption.WriterGeneration == held.Adoption.WriterGeneration
            && now >= held.ObservedAt && now < held.Receipt.ExpiresAt
            && actual.Adoption.MatchesCurrent(actual.Source, now)
            && EnvironmentCurrent(held.Transaction) && caller.UserId == held.Authority.UserId;
    }

    public async Task<ModerationCaseNarrativeDisclosure?> ReadAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken) =>
        (await ReadCoreAsync(request, null, cancellationToken)).Disclosure;

    public async Task<bool> DiscloseAsync(ModerationCaseAccessRequest request,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        return (await ReadCoreAsync(request, write, cancellationToken)).Delivered;
    }

    private async Task<(ModerationCaseNarrativeDisclosure? Disclosure, bool Delivered)> ReadCoreAsync(
        ModerationCaseAccessRequest request, Func<ReadOnlyMemory<byte>, CancellationToken, Task>? write,
        CancellationToken cancellationToken)
    {
        if (request.Purpose != ModerationCaseAccessPurpose.ReadAllegation || request.OperationId != Guid.Empty
            || request.ExpectedCaseRevision < 1 || !request.Narrative.IsEmpty || request.PresentedToken.Length != 32
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || context.ChangeTracker.Entries().Any() || context.ModerationTrustDomainId is not null)
            return (null, false);
        var ownedToken = request.PresentedToken.ToArray();
        request = request with { PresentedToken = ownedToken };
        ModerationCaseNarrativeDisclosure? disclosure = null;
        bool deliveryStarted = false;
        DateTimeOffset? deadline = null;
        Guid? user = caller.UserId;
        Guid? tenant = context.TenantFilterTenantId;
        try
        {
            async Task<ModerationCaseNarrativeDisclosure?> ReadQualifiedAsync()
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var readSources = await intake.DiscoverReadSourcesAsync(request, cancellationToken);
                if (readSources is null || !await AcquireCoreAsync(request.ReceiptId, readSources, cancellationToken) || lease is not { } held
                    || await intake.ReadAsync(request, cancellationToken) is not { } allegation)
                    return null;
                var row = await context.Set<ModerationCaseNarrative>().AsNoTracking().SingleOrDefaultAsync(item =>
                    item.TenantId == held.Authority.Tenant.Id && item.TrustDomainId == held.Authority.Binding.TrustDomainId
                    && item.CaseId == allegation.Id && item.SourceId == held.Receipt.SourceId
                    && item.MaterialFactId == held.Receipt.MaterialFactId, cancellationToken);
                if (row is null) return null;
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseNarrative>(context, row.Id, cancellationToken);
                row.ValidatePersistedState();
                var now = clock.GetUtcNow();
                if (row.PolicySourceId != held.Source.Id || row.AdoptionId != held.Adoption.Id
                    || row.PolicyWriterGeneration != held.Source.WriterGeneration
                    || row.AdoptionWriterGeneration != held.Adoption.WriterGeneration
                    || row.SourceVersion != held.Receipt.SourceVersion || now < row.CapturedAt || now >= row.PurgeAt
                    || !await PolicyCurrentAsync(cancellationToken) || !await intake.RecheckReadAsync(cancellationToken))
                    return null;
                var source = await context.ModerationCaseTargetSources.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == row.SourceId && item.TenantId == row.TenantId, cancellationToken);
                if (source is null || source.Evidence(held.Authority.Tenant) != held.Receipt.Evidence) return null;
                var plaintext = ModerationOriginalNarrativeAuthentication.Authenticate(
                    protection!, row, source, held.Source, held.Adoption, held.Authority.Tenant, now);
                if (plaintext is null) return null;
                try
                {
                    if (!await PolicyCurrentAsync(cancellationToken) || !await intake.RecheckReadAsync(cancellationToken)
                        || clock.GetUtcNow() < row.CapturedAt || clock.GetUtcNow() >= row.PurgeAt)
                        return null;
                    deadline = new[] { row.PurgeAt, held.Adoption.ValidUntil, held.Receipt.ExpiresAt }.Min();
                    if (write is not null)
                    {
                        // The same native primary transaction and already-held source/role/
                        // policy/material fences remain live through actual output. Do not
                        // wrap an external response write in a provider retry strategy.
                        deliveryStarted = true;
                        await write(plaintext, cancellationToken);
                        return null;
                    }
                    return new ModerationCaseNarrativeDisclosure(plaintext);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            disclosure = write is null
                ? await context.Database.CreateExecutionStrategy().ExecuteAsync(ReadQualifiedAsync)
                : await ReadQualifiedAsync();
            // Final local handoff after releasing the read transaction. This does not promise
            // to recall bytes already disclosed to a receiver.
            if (!deliveryStarted && disclosure is not null && (deadline is null || clock.GetUtcNow() >= deadline
                || !caller.IsAuthenticated || caller.UserId != user || machine.Current is not null
                || context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed))
            {
                disclosure.Dispose();
                return (null, false);
            }
            return (disclosure, deliveryStarted);
        }
        catch (CryptographicException) when (!deliveryStarted) { disclosure?.Dispose(); return (null, false); }
        catch (DbException) when (!deliveryStarted) { disclosure?.Dispose(); return (null, false); }
        finally
        {
            CryptographicOperations.ZeroMemory(ownedToken);
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
            lease = null;
        }
    }

    private IDataProtector Protector(Guid id, Guid caseId, Guid tenantId, Guid trustId,
        Guid sourceId, Guid sourceVersion, Guid materialId, Guid policyId, Guid adoptionId,
        DateTimeOffset capturedAt, DateTimeOffset purgeAt, Guid hostId, Guid reporterId, Guid receiptId,
        Guid epochId, long epochRevision, long policyGeneration, long adoptionGeneration) =>
        ModerationOriginalNarrativeAuthentication.Protector(protection!, id, caseId, tenantId, trustId,
            sourceId, sourceVersion, materialId, policyId, adoptionId, capturedAt, purgeAt,
            hostId, reporterId, receiptId, epochId, epochRevision, policyGeneration, adoptionGeneration);

    private bool EnvironmentCurrent(IDbContextTransaction? transaction) =>
        transaction is not null && ReferenceEquals(transaction, context.Database.CurrentTransaction)
        && context.Database.IsRelational() && transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && System.Transactions.Transaction.Current is null && !context.IsTenantFilterBypassed
        && context.TenantFilterTenantId is { } tenant && tenant != Guid.Empty
        && caller.IsAuthenticated && caller.UserId is { } user && user != Guid.Empty && machine.Current is null;

    private sealed record Lease(IDbContextTransaction Transaction, ModerationSourceAuthority Authority,
        ModerationCaseNarrativePolicySource Source, ModerationCaseNarrativeAdoption Adoption,
        ModerationCaseTargetReceipt Receipt, DateTimeOffset ObservedAt);
}
