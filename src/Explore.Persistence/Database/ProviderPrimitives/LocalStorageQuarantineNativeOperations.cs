using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class LocalStorageQuarantineNativeOperations
{
    private readonly ExploreDbContext context;
    private readonly PostgresNativeQueueEnrollment enrollment;

    private LocalStorageQuarantineNativeOperations(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment)
    {
        this.context = context;
        this.enrollment = enrollment;
    }

    internal static LocalStorageQuarantineNativeOperations? TryBind(
        ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment) =>
        context.Database.IsNpgsql()
            ? new(context, enrollment ?? throw new InvalidOperationException("local_storage_quarantine_enrollment_required"))
            : null;

    internal Task RequireReadyAsync(CancellationToken cancellationToken) =>
        PostgresLocalStorageQuarantineContract.RequireReadyAsync(context, enrollment, cancellationToken);

    internal Task<bool> InspectAsync(Guid bindingId, string objectKey, CancellationToken cancellationToken) =>
        context.Database.SqlQueryRaw<bool>(
            $"SELECT {PostgresLocalStorageQuarantineContract.FunctionSql(context, "inspect")}({{0}}, {{1}}) AS \"Value\"",
            bindingId, objectKey).SingleAsync(cancellationToken);

    internal async Task<LocalStorageQuarantine?> AdmitAsync(
        Guid bindingId, string objectKey, long observedSize, DateTime observedLastModifiedUtc,
        DateTime modifiedBeforeUtc, string admissionReason, CancellationToken ct)
    {
        if (observedLastModifiedUtc.Kind != DateTimeKind.Utc || modifiedBeforeUtc.Kind != DateTimeKind.Utc
            || observedLastModifiedUtc == default || modifiedBeforeUtc == default)
            throw new ArgumentException("Admission timestamps must be non-default UTC values.");
        var result = await context.Set<LocalStorageQuarantine>().FromSqlRaw(
            $"SELECT * FROM {PostgresLocalStorageQuarantineContract.FunctionSql(context, "admit")}({{0}},{{1}},{{2}},{{3}},{{4}},{{5}},{{6}},{{7}})",
            bindingId, objectKey, observedSize, observedLastModifiedUtc.Ticks, modifiedBeforeUtc.Ticks,
            admissionReason, Guid.CreateVersion7(), Guid.CreateVersion7()).AsNoTracking().ToListAsync(ct);
        return result.SingleOrDefault();
    }

    internal async Task<IReadOnlyList<LocalStorageQuarantine>> ClaimAsync(
        int batchSize, TimeSpan lease, CancellationToken ct) =>
        await context.Set<LocalStorageQuarantine>().FromSqlRaw(
            $"SELECT * FROM {PostgresLocalStorageQuarantineContract.FunctionSql(context, "claim")}({{0}},{{1}},{{2}})",
            batchSize, lease.TotalSeconds, Guid.CreateVersion7()).AsNoTracking().ToListAsync(ct);

    internal Task<bool> ValidateClaimAsync(FileStorageQuarantineInput claim, CancellationToken ct) =>
        context.Database.SqlQueryRaw<bool>(
            $"SELECT {PostgresLocalStorageQuarantineContract.FunctionSql(context, "validate_claim")}({{0}},{{1}},{{2}},{{3}},{{4}},{{5}},{{6}},{{7}},{{8}}) AS \"Value\"",
            claim.QuarantineId, claim.ClaimStamp, claim.ProviderBindingId, claim.ObjectKey,
            claim.ExpectedSize, claim.ExpectedLastModifiedUtc.Ticks, claim.LeaseExpiresAtUtc.Ticks,
            claim.CanonicalPhysicalPath, claim.PhysicalPathHash).SingleAsync(ct);

    internal Task<bool> SettleAsync(
        Guid id, Guid claimStamp, LocalStorageQuarantineOutcome? outcome, DateTime? retry, CancellationToken ct) =>
        context.Database.SqlQueryRaw<bool>(
            $"SELECT {PostgresLocalStorageQuarantineContract.FunctionSql(context, "settle")}({{0}},{{1}},{{2}}::smallint,{{3}}::bigint,{{4}}) AS \"Value\"",
            id, claimStamp, (object?)outcome is null ? DBNull.Value : (short)outcome.Value,
            retry is { } nextRetry ? nextRetry.Ticks : DBNull.Value, Guid.CreateVersion7()).SingleAsync(ct);
}
