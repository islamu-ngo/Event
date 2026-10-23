using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Native federation operations on the original selected connection.</summary>
internal sealed class AtprotoRepositoryNativeOperations
{
    private readonly ExploreDbContext context;

    private AtprotoRepositoryNativeOperations(ExploreDbContext context) => this.context = context;

    internal static AtprotoRepositoryNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<bool> MoveActorReferencesAsync(Guid source, Guid target, int targetType, CancellationToken token) =>
        PostgresActorConsolidationContract.MoveAsync(context, source, target, targetType, token);

    internal Task<AtprotoMaterializationProviderOperations> MaterializeAsync(
        AtprotoJetstreamApplyRequest request, CancellationToken token) =>
        AtprotoMaterializationProviderOperations.CreateAsync(context, request, token);

    internal Task<AtprotoMaterializationProviderOperations> MaterializeAsync(
        AtprotoPdsSnapshotApplyRequest request, CancellationToken token) =>
        AtprotoMaterializationProviderOperations.CreateAsync(context, request, token);

    internal async Task<bool> ConsumeBootstrapAsync(PostgresNativeQueueEnrollment? enrollment,
        Guid tenant, string jti, DateTime now, DateTime expires, CancellationToken token)
    {
        bool opened = RelationalConnectionIdentity.Capture(context).State != ConnectionState.Open;
        if (opened) await context.Database.OpenConnectionAsync(token);
        try
        {
            return await PostgresIdempotencyReceiptContract.ConsumeAsync(context, enrollment
                ?? throw new InvalidOperationException("Native receipt enrollment is required."),
                tenant, jti, now, expires, token);
        }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }
}
