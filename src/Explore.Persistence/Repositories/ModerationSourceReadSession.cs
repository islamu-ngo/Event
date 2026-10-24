using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Fenced source provenance contains no viewer, grant or permission. Authenticated
/// acquisitions retain their original session checks; public acquisitions have
/// only the closed current-calendar purpose and cannot borrow candidate authority.
/// </summary>
internal interface IModerationSourceProvenance
{
    ModerationHostBinding Binding { get; }
    ModerationQualificationEpoch Epoch { get; }
    AuthorityScope Instance { get; }
    AuthorityScope Tenant { get; }
    IReadOnlyList<Guid> FencedHumans { get; }
    IReadOnlyList<Guid> Administrators { get; }
    IReadOnlyDictionary<Guid, EventAuthorityProviderOperations.Ownership> MaterialGenerations { get; }
    EventAuthorityProviderOperations.AuthorityPlan? MaterialAuthority { get; }
}

internal sealed record ModerationSourceReadAcquisition(
    ExploreDbContext Context, IDbContextTransaction Transaction, IModerationSourceProvenance Authority,
    ModerationPolicyCompiledDraft Policy, bool IsPublicCalendar,
    ModerationOriginalRecipeAuthority? OriginalRecipe = null);

internal abstract class ModerationSourceReadSession
{
    private ModerationSourceReadSession() { }

    internal static async Task<bool> ProviderTenantMatchesAsync(
        ExploreDbContext context, Guid tenantId, CancellationToken token)
    {
        if (context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed) return false;
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider) return true;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT current_setting('app.current_tenant_id', true)";
        return Guid.TryParse((await command.ExecuteScalarAsync(token))?.ToString(), out var actual) && actual == tenantId;
    }

    internal abstract Task<ModerationSourceReadAcquisition?> AcquireNativeAsync(
        Guid transactionId, DateTimeOffset observedAt, CancellationToken token);

    internal static ModerationSourceReadSession? Authenticated(IModerationConsumerEvidenceSession session) =>
        session is ModerationConsumerEvidenceSession native ? new AuthenticatedSession(native) : null;

    public static explicit operator ModerationSourceReadSession(ModerationConsumerEvidenceSession session) =>
        new AuthenticatedSession(session);

    internal static ModerationSourceReadSession PublicCalendar(
        ModerationSourceReadAcquisition acquisition, Func<CancellationToken, Task<bool>> current, TimeProvider clock)
    {
        if (!acquisition.IsPublicCalendar || acquisition.OriginalRecipe is not null
            || acquisition.Authority is ModerationSourceAuthority)
            throw new ArgumentException("Public provenance cannot carry authenticated or original-effect authority.");
        return new PublicCalendarSession(acquisition, current, clock);
    }

    private sealed class AuthenticatedSession(ModerationConsumerEvidenceSession session) : ModerationSourceReadSession
    {
        internal override async Task<ModerationSourceReadAcquisition?> AcquireNativeAsync(
            Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
        {
            var native = await session.AcquireNativeAsync(transactionId, observedAt, token);
            return native is null ? null : new(native.Context, native.Transaction, native.Authority,
                native.Policy, false, native.OriginalRecipe);
        }
    }

    private sealed class PublicCalendarSession(
        ModerationSourceReadAcquisition acquisition, Func<CancellationToken, Task<bool>> current, TimeProvider clock)
        : ModerationSourceReadSession
    {
        internal override async Task<ModerationSourceReadAcquisition?> AcquireNativeAsync(
            Guid transactionId, DateTimeOffset observedAt, CancellationToken token) =>
            transactionId != Guid.Empty && transactionId == acquisition.Transaction.TransactionId
            && observedAt.Offset == TimeSpan.Zero && observedAt != DateTimeOffset.MinValue
            && observedAt != DateTimeOffset.MaxValue && observedAt <= clock.GetUtcNow()
            && await current(token) ? acquisition : null;
    }
}
