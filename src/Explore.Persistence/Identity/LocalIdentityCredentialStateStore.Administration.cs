using System.Data;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Responses;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private DbContext IdentityOwner => identityDbContext;
    private ExploreDbContext ApplicationOwner => applicationDbContext;
    private OriginalOwnerSettlement? _administrationSettlement;
    private HumanAdministrationAttempt? _administrationAttempt;
    private SelfResetPostimage? _selfReset;

    private sealed record HumanAdministrationAttempt(ILocalAdministrationInvocation Invocation,
        LocalAdministrationStage Stage, IDbContextTransaction Transaction);

    /// <summary>
    /// Enters the real Identity owner. A private attempt is the only join accepted by
    /// its existing effect bodies; neither callers nor an Application UoW can create it.
    /// Earlier acknowledged stages remain durable if a later stage loses authority.
    /// </summary>
    internal sealed class HumanAdministration(LocalIdentityCredentialStateStore store,
        ILocalAdministrationPreparation preparation, IServiceProvider services,
        Func<PostgresNativeQueueEnrollment> nativeEnrollment)
        : ILocalHumanCredentialAdministration, ILocalAdministrationOwnerSource
    {
        public Task<LocalCredentialCreateResult> CreatePendingAsync(ILocalAdministrationInvocation invocation,
            CreateLocalIdentityCommand request, CancellationToken cancellationToken) =>
            Own(invocation, request, LocalAdministrationStage.CreatePending, async (attempt, token) =>
            {
                var result = store._nativeAdministration is { } native
                    ? await store.NativeCreateAsync(native, services, token)
                    : await store.CreateAttemptAsync(new(request.OperationId, invocation.PrincipalId,
                        request.Email, request.FirstName, request.LastName), null, null, token, attempt);
                return result.Outcome is LocalCredentialCreateOutcome.Created or LocalCredentialCreateOutcome.Replayed
                    ? OriginalOwnerDecision<LocalCredentialCreateResult>.Commit(result)
                    : OriginalOwnerDecision<LocalCredentialCreateResult>.RollBack(result);
            }, cancellationToken);

        public Task<LocalCredentialResetResult> ResetAsync(ILocalAdministrationInvocation invocation,
            ResetLocalCredentialCommand request, CancellationToken cancellationToken) =>
            Own(invocation, request, LocalAdministrationStage.Reset, async (attempt, token) =>
            {
                var result = store._nativeAdministration is { } native
                    ? await store.NativeResetAsync(native, services, token)
                    : await store.ResetAttemptAsync(new(request.OperationId, invocation.PrincipalId,
                        request.LocalSubjectId, request.ExpectedCurrentOperationId,
                        request.ExpectedCurrentOperationConcurrencyStamp, request.Reason), token, attempt);
                return result.Outcome is LocalCredentialResetOutcome.Reset or LocalCredentialResetOutcome.Replayed
                    ? OriginalOwnerDecision<LocalCredentialResetResult>.Commit(result)
                    : OriginalOwnerDecision<LocalCredentialResetResult>.RollBack(result);
            }, cancellationToken);

        public Task<LocalCredentialProvisioningSnapshot?> ReadProvisioningAsync(ILocalAdministrationInvocation invocation,
            object originalRequest, CancellationToken cancellationToken) =>
            Own(invocation, originalRequest, LocalAdministrationStage.ReadProvisioning, async (_, token) =>
            {
                if (store._nativeAdministration is not { } native)
                    return OriginalOwnerDecision<LocalCredentialProvisioningSnapshot?>.Commit(
                        await store.ReadProvisioningCoreAsync(Operation(originalRequest), token));
                await native.ReadOnlyAsync(token);
                return OriginalOwnerDecision<LocalCredentialProvisioningSnapshot?>.Commit(
                    NativeProvisioning(native.Expected, Operation(originalRequest)));
            }, cancellationToken);

        public Task<BaseCommandResponse<Guid>> BindAsync(ILocalAdministrationInvocation invocation, object originalRequest,
            LocalCredentialProvisioningSnapshot snapshot, CancellationToken cancellationToken) =>
            Own(invocation, originalRequest, LocalAdministrationStage.Bind, async (_, token) =>
            {
                if (store._nativeAdministration is { } native)
                {
                    var binding = await store.NativeBindAsync(native, snapshot, token);
                    return binding.IsSuccess ? OriginalOwnerDecision<BaseCommandResponse<Guid>>.Commit(binding)
                        : OriginalOwnerDecision<BaseCommandResponse<Guid>>.RollBack(binding);
                }
                if (snapshot.Receipt.OperationId != Operation(originalRequest)
                    || snapshot != await store.ReadProvisioningCoreAsync(Operation(originalRequest), token))
                    return OriginalOwnerDecision<BaseCommandResponse<Guid>>.RollBack(
                        BaseCommandResponse.Conflict(id: Operation(originalRequest)));
                var result = await store.BindAdministrationAsync(snapshot, token);
                return result.IsSuccess ? OriginalOwnerDecision<BaseCommandResponse<Guid>>.Commit(result)
                    : OriginalOwnerDecision<BaseCommandResponse<Guid>>.RollBack(result);
            }, cancellationToken, snapshot);

        public Task<LocalCredentialActivationOutcome> ActivateAsync(ILocalAdministrationInvocation invocation,
            object originalRequest, LocalCredentialProvisioningSnapshot snapshot, CancellationToken cancellationToken) =>
            Own(invocation, originalRequest, LocalAdministrationStage.Activate, async (attempt, token) =>
            {
                if (store._nativeAdministration is { } native)
                {
                    var activation = await store.NativeActivateAsync(native, snapshot, token);
                    return activation is LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated
                        ? OriginalOwnerDecision<LocalCredentialActivationOutcome>.Commit(activation)
                        : OriginalOwnerDecision<LocalCredentialActivationOutcome>.RollBack(activation);
                }
                if (snapshot.Receipt.OperationId != Operation(originalRequest)
                    || snapshot != await store.ReadProvisioningCoreAsync(Operation(originalRequest), token))
                    return OriginalOwnerDecision<LocalCredentialActivationOutcome>.RollBack(
                        LocalCredentialActivationOutcome.Conflict);
                var result = await store.ActivateAttemptAsync(
                    new(Operation(originalRequest), snapshot.OperationConcurrencyStamp), token, attempt);
                return result is LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated
                    ? OriginalOwnerDecision<LocalCredentialActivationOutcome>.Commit(result)
                    : OriginalOwnerDecision<LocalCredentialActivationOutcome>.RollBack(result);
            }, cancellationToken, snapshot);

        public Task<LocalCredentialOperationStatus?> ReadOperationAsync(ILocalAdministrationInvocation invocation,
            object originalRequest, CancellationToken cancellationToken) =>
            Own(invocation, originalRequest, LocalAdministrationStage.Disclose, async (_, token) =>
            {
                if (store._nativeAdministration is not { } native)
                    return OriginalOwnerDecision<LocalCredentialOperationStatus?>.Commit(
                        await store.ReadOperationAsync(Operation(originalRequest), token));
                await native.ReadOnlyAsync(token);
                return OriginalOwnerDecision<LocalCredentialOperationStatus?>.Commit(
                    NativeStatus(native.Expected, Operation(originalRequest)));
            }, cancellationToken);

        public Task<LocalIdentityPage> ListAsync(ILocalAdministrationInvocation invocation,
            ListLocalIdentitiesQuery request, CancellationToken cancellationToken) =>
            Own(invocation, request, LocalAdministrationStage.Disclose, async (_, token) =>
            {
                if (store._nativeAdministration is not { } native)
                    return OriginalOwnerDecision<LocalIdentityPage>.Commit(
                        await store.ListAsync(new(request.PageNumber, request.PageSize), token));
                await native.ReadOnlyAsync(token);
                return OriginalOwnerDecision<LocalIdentityPage>.Commit(
                    NativePage(native.Expected, request.PageNumber, request.PageSize));
            }, cancellationToken);

        void ILocalAdministrationOwnerSource.RequireOwner(ILocalAdministrationInvocation invocation,
            LocalAdministrationStage stage)
        {
            if (store._administrationAttempt is not { } attempt || !ReferenceEquals(attempt.Invocation, invocation)
                || attempt.Stage != stage || !ReferenceEquals(store.IdentityOwner, store.ApplicationOwner)
                || !ReferenceEquals(store.IdentityOwner.Database.CurrentTransaction, attempt.Transaction)
                || !store.ApplicationOwner.HumanCommitAttemptOwnsTransaction
                || !store.ApplicationOwner.HasHumanCommitContextLease)
                throw new UnauthorizedAccessException("Only the original Local physical owner can acquire administration sources.");
        }

        Task<LocalCredentialResetReceipt?> ILocalAdministrationOwnerSource.ValidateAsync(
            ILocalAdministrationInvocation invocation, CancellationToken cancellationToken) =>
            store._nativeAdministration is not null
                ? store.ValidateNativeSelfResetAsync(invocation, cancellationToken)
                : store.ValidateSelfResetAsync(invocation, cancellationToken);

        private async Task<T> Own<T>(ILocalAdministrationInvocation invocation, object request,
            LocalAdministrationStage stage,
            Func<HumanAdministrationAttempt, CancellationToken, Task<OriginalOwnerDecision<T>>> body,
            CancellationToken cancellationToken, LocalCredentialProvisioningSnapshot? snapshot = null)
        {
            store.RequireHumanAdministrationOwner();
            var owner = store._administrationSettlement ??= new OriginalOwnerSettlement(store.ApplicationOwner);
            bool committed = false;
            try
            {
                var settled = await owner.ExecuteAsync(invocation, async attempt =>
                {
                    await using var transaction = await store.IdentityOwner.Database
                        .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                    var human = new HumanAdministrationAttempt(invocation, stage, transaction);
                    store._administrationAttempt = human;
                    return await owner.SettleAsync(attempt, transaction, async token =>
                    {
                        if (store.ApplicationOwner.Database.IsNpgsql())
                        {
                            store._nativeAdministration = new(store, human, request,
                                store.NativeRequest(invocation, request, stage, snapshot), nativeEnrollment());
                            await store._nativeAdministration.ReadAsync(token);
                            store.RequireIssuedImage(store._nativeAdministration);
                        }
                        await preparation.AcquireAsync(invocation, request, stage, token);
                        if (store._nativeAdministration is { } acquired)
                            await acquired.AcquireAsync(token);
                        var decision = await body(human, token);
                        if (store._nativeAdministration is { } native
                            && decision.Disposition == OriginalOwnerDisposition.Committed)
                        {
                            // Preparation has registered every original asynchronous
                            // S/P/route/H comparison. DATA Complete is registered last;
                            // all authentic synchronous deadlines still run after it.
                            store.ApplicationOwner.RegisterHumanCommitAdmission(
                                native.CompleteAsync, native.RequireCompleted);
                        }
                        return decision;
                    }, cancellationToken);
                }, cancellationToken);
                var value = owner.Consume(invocation, settled, settled.Disposition);
                committed = settled.Disposition == OriginalOwnerDisposition.Committed;
                if (stage == LocalAdministrationStage.Reset && store._selfReset is { } reset
                    && ReferenceEquals(reset.Invocation, invocation))
                    reset.Committed = committed;
                preparation.Settle(invocation, stage, committed);
                if (store._nativeIssued is { } issued)
                    issued.Committed = committed;
                return value;
            }
            catch
            {
                preparation.Settle(invocation, stage, committed: false);
                throw;
            }
            finally
            {
                store._administrationAttempt = null;
                store._nativeAdministration = null;
                if (!committed || stage == LocalAdministrationStage.Disclose)
                {
                    store._selfReset = null;
                    store._nativeIssued = null;
                }
            }
        }

        private static Guid Operation(object request) => request switch
        {
            CreateLocalIdentityCommand create => create.OperationId,
            ResetLocalCredentialCommand reset => reset.OperationId,
            ReconcileLocalCredentialOperationCommand reconcile => reconcile.OperationId,
            GetLocalCredentialOperationQuery query => query.OperationId,
            _ => throw new UnauthorizedAccessException("This Local request has no credential operation.")
        };
    }

    private void RequireHumanAdministrationOwner()
    {
        EnsureReconciliationContextsIdle();
        if (_administrationAttempt is not null || System.Transactions.Transaction.Current is not null
            || !ReferenceEquals(identityDbContext, applicationDbContext)
            || !applicationDbContext.HasHumanCommitContextLease || applicationDbContext.IsTenantFilterBypassed
            || applicationDbContext.IsConfigurationInstanceImportOwner
            || !(applicationDbContext.Database.IsSqlite() || applicationDbContext.Database.IsNpgsql()))
            throw new NotSupportedException(
                "Local administration requires its original co-located SQLite or PostgreSQL Runtime human owner.");
    }
}

/// <summary>Resolves both human administration ports from the same scoped physical-owner adapter.</summary>
public static class LocalAdministrationSourceRegistration
{
    public static IServiceCollection AddLocalAdministrationSources(this IServiceCollection services)
    {
        services.AddScoped<LocalIdentityCredentialStateStore.HumanAdministration>(provider => new(
            provider.GetRequiredService<LocalIdentityCredentialStateStore>(),
            provider.GetRequiredService<ILocalAdministrationPreparation>(), provider,
            () => PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>())));
        services.AddScoped<ILocalHumanCredentialAdministration>(provider =>
            provider.GetRequiredService<LocalIdentityCredentialStateStore.HumanAdministration>());
        services.AddScoped<ILocalAdministrationOwnerSource>(provider =>
            provider.GetRequiredService<LocalIdentityCredentialStateStore.HumanAdministration>());
        return services;
    }
}
