using System.Data;
using System.Security.Cryptography;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Authenticates confidential intake receipts without disclosing protected material. Native
/// target/relationship owners supply the separate final requalification, not receipt existence.
/// </summary>
public sealed class ModerationCaseTargetReceiptRepository(
    ExploreDbContext context, ICurrentUserService currentUser, IMachinePrincipalAccessor machine,
    TimeProvider clock) : IModerationCaseTargetReceiptRepository
{
    public async Task<ModerationCaseTargetReceipt?> ReadForCurrentCallerAsync(
        Guid receiptId, ReadOnlyMemory<byte> presentedToken, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is null
            || context.Database.CurrentTransaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is null)
            throw new InvalidOperationException("Intake receipt authentication requires the qualified native intake transaction.");
        if (receiptId == Guid.Empty || presentedToken.Length != 32
            || !currentUser.IsAuthenticated || machine.Current is not null
            || currentUser.UserId is not { } userId || userId == Guid.Empty
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, cancellationToken);
        if (!await context.Users.AsNoTracking().AnyAsync(row => row.Id == userId && !row.IsDeleted, cancellationToken)
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == userId, cancellationToken))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, cancellationToken);
        if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId, cancellationToken))
            return null;

        var installed = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (installed is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, installed.Id, cancellationToken);
        var host = await context.ModerationHostBindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == installed.Id, cancellationToken);
        if (host is null || host.TrustDomainId != context.ModerationTrustDomainId)
            return null;
        var discoveredEpoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, cancellationToken);
        if (discoveredEpoch is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(
            context, discoveredEpoch.Id, cancellationToken);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discoveredEpoch.Id
                && row.HostBindingId == host.Id && row.TenantId == tenantId, cancellationToken);
        if (epoch is null || epoch.Revision < 1) return null;

        if (!await context.ModerationCaseTargetReceipts.AsNoTracking().AnyAsync(
                row => row.Id == receiptId && row.CallerUserId == userId && row.TenantId == tenantId, cancellationToken))
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCaseTargetReceipt>(
            context, receiptId, cancellationToken);
        var receipt = await context.ModerationCaseTargetReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId && row.CallerUserId == userId
                && row.TenantId == tenantId, cancellationToken);
        if (receipt is null) return null;
        receipt.ValidatePersistedState();
        var now = clock.GetUtcNow();
        if (receipt.HostBindingId != host.Id || receipt.QualificationEpochId != epoch.Id
            || receipt.QualificationRevision != epoch.Revision
            || receipt.TargetOriginTrustDomainId != host.TrustDomainId
            || receipt.TargetOriginSolutionId != host.SolutionId
            || receipt.TargetOriginInstanceId != host.InstanceId
            || now < receipt.IssuedAt || now >= receipt.ExpiresAt
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(receipt.TokenDigest), SHA256.HashData(presentedToken.Span))
            || !currentUser.IsAuthenticated || currentUser.UserId != userId || machine.Current is not null
            || context.TenantFilterTenantId != tenantId)
            return null;
        return receipt;
    }
}
