using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class IncomingWebhookNativeQueue
{
    internal static async Task<IReadOnlyList<IncomingWebhookClaim>?> TryClaimMessagesAsync(
        ExploreDbContext context, IncomingWebhookClaimRequest request, string owner, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        return await context.Database.SqlQueryRaw<IncomingWebhookClaim>(
            $"SELECT * FROM {PostgresIncomingWebhookQueueContract.ClaimFunctionSql(context, effects: false)}({{0}}, {{1}}, {{2}}, {{3}})",
            owner, request.BatchSize, request.ClaimedAt, request.LeaseDuration).ToListAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<IncomingWebhookEffectClaim>?> TryClaimEffectsAsync(
        ExploreDbContext context, IncomingWebhookEffectClaimRequest request, string owner, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        return await context.Database.SqlQueryRaw<IncomingWebhookEffectClaim>(
            $"SELECT * FROM {PostgresIncomingWebhookQueueContract.ClaimFunctionSql(context, effects: true)}({{0}}, {{1}}, {{2}}, {{3}})",
            owner, request.BatchSize, request.ClaimedAt, request.LeaseDuration).ToListAsync(cancellationToken);
    }

    internal static async Task<bool?> TryRenewMessageAsync(
        ExploreDbContext context, IncomingWebhookClaim claim, DateTime observedAt, DateTime leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        return await context.Database.SqlQueryRaw<bool>(
            $"SELECT {PostgresIncomingWebhookQueueContract.RenewFunctionSql(context, effects: false)}({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}) AS \"Value\"",
            claim.TenantId, claim.IncomingWebhookMessageId, claim.LeaseToken, claim.ProcessingFence,
            claim.ProcessingGeneration, observedAt, leaseExpiresAt).SingleAsync(cancellationToken);
    }

    internal static async Task<bool?> TryRenewEffectAsync(
        ExploreDbContext context, IncomingWebhookEffectClaim claim, DateTime observedAt, DateTime leaseExpiresAt,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return null;
        return await context.Database.SqlQueryRaw<bool>(
            $"SELECT {PostgresIncomingWebhookQueueContract.RenewFunctionSql(context, effects: true)}({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}) AS \"Value\"",
            claim.TenantId, claim.EffectOutboxId, claim.LeaseToken, claim.ProcessingFence,
            claim.ProcessingGeneration, observedAt, leaseExpiresAt).SingleAsync(cancellationToken);
    }
}
