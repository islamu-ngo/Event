using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class PrivacyIdentityFenceOperation(IPrivacyIdentityFenceAuthority fenceAuthority)
{
    private readonly AsyncLocal<IdentityGateScope?> _activeScope = new();

    public Task AfterEnrollmentCommitAsync(Func<Task> operation)
    {
        if (_activeScope.Value is not { } scope)
            return operation();
        scope.AfterCommit.Add(operation);
        return Task.CompletedTask;
    }

    public async Task EnsureSubjectMayEnrollAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_activeScope.Value is not { CanCheckSubjects: true })
            throw new InvalidOperationException("Existing-user linkage requires the identity enrollment gate.");
        if (await fenceAuthority.IsSubjectFencedAsync(userId, cancellationToken))
            throw new InvalidOperationException("privacy_identity_erased");
    }

    public Task<T> ExecuteEnrollmentAsync<T>(
        ProviderAccountKey account,
        Func<CancellationToken, Task<T>> enrollment,
        CancellationToken cancellationToken)
    {
        // Local subjects are the native credential authority's UUIDs, not reusable
        // external identifiers; their existing receipt and UUID fences still apply.
        if (account.ProviderKind == AuthenticationProviderKind.Local)
            return enrollment(cancellationToken);
        return ExecuteSerializedAsync(enrollment, cancellationToken);
    }

    /// <summary>
    /// Owns one human enrollment settlement. A co-located embedded authority is itself
    /// the physical owner; a separate authority gate encloses, but never replaces, the
    /// selected application owner. Result publication follows disposal and receipt consumption.
    /// </summary>
    public async Task<T> ExecuteSettledEnrollmentAsync<T>(
        ProviderAccountKey account,
        object invocation,
        IOriginalOwnerSettlementBoundary owner,
        Func<CancellationToken, Task<OriginalOwnerDecision<T>>> enrollment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(enrollment);
        if (_activeScope.Value is not null)
            throw new InvalidOperationException("Human enrollment cannot borrow or replay an active enrollment scope.");

        bool ownerIsIdentityGate = ReferenceEquals(owner, fenceAuthority);
        bool requiresIdentityGate = account.ProviderKind != AuthenticationProviderKind.Local;
        var scope = new IdentityGateScope(requiresIdentityGate || ownerIsIdentityGate);
        IOriginalOwnerSettlement<T>? settlement = null;
        OriginalOwnerDisposition? proposedDisposition = null;
        try
        {
            async Task<IOriginalOwnerSettlement<T>> ExecuteOwner(CancellationToken token)
            {
                settlement = await owner.ExecuteAsync(invocation, async ownerToken =>
                {
                    _activeScope.Value = scope;
                    try
                    {
                        var decision = await enrollment(ownerToken);
                        proposedDisposition = decision.Disposition;
                        return decision;
                    }
                    finally
                    {
                        _activeScope.Value = null;
                    }
                }, token);
                return settlement;
            }

            // Embedded settlement already opens the identity gate and enlists the
            // original application context. Calling ExecuteSerializedAsync first
            // would borrow its transaction and make a physical receipt impossible.
            var receipt = requiresIdentityGate && !ownerIsIdentityGate
                ? await (fenceAuthority as IOriginalPrivacyIdentityFenceAuthority
                    ?? throw new NotSupportedException("Original human enrollment requires an original identity-gate authority."))
                    .ExecuteOriginalEnrollmentAsync(ExecuteOwner, cancellationToken)
                : await ExecuteOwner(cancellationToken);
            var disposition = proposedDisposition
                ?? throw new InvalidOperationException("Enrollment did not propose a physical disposition.");
            T result = owner.Consume(invocation, receipt, disposition);
            if (disposition == OriginalOwnerDisposition.Committed)
                foreach (Func<Task> afterCommit in scope.AfterCommit)
                    await afterCommit();
            return result;
        }
        catch (Exception exception) when (settlement is not null
            && exception is not HumanMutationOutcomeUnknownException)
        {
            // The application owner has already settled. A surrounding authority's
            // acknowledgement/disposal or result-publication failure is not a fresh
            // authorization denial or permission to replay the provisioning graph.
            throw new HumanMutationOutcomeUnknownException(exception);
        }
    }

    private async Task<T> ExecuteSerializedAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_activeScope.Value is not null)
            return await operation(cancellationToken);
        var scope = new IdentityGateScope();
        T result = await fenceAuthority.ExecuteSerializedAsync(async token =>
        {
            _activeScope.Value = scope;
            try { return await operation(token); }
            finally { _activeScope.Value = null; }
        }, cancellationToken);
        foreach (Func<Task> afterCommit in scope.AfterCommit)
            await afterCommit();
        return result;
    }

    private sealed class IdentityGateScope(bool canCheckSubjects = true)
    {
        public bool CanCheckSubjects { get; } = canCheckSubjects;
        public List<Func<Task>> AfterCommit { get; } = [];
    }
}
