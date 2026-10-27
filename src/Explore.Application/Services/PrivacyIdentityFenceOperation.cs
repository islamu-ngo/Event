using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Application.Exceptions;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class PrivacyIdentityFenceOperation(
    IPrivacyIdentityFenceAuthority fenceAuthority,
    IModerationRecognitionContinuity recognition,
    TimeProvider clock,
    IUserExternalLoginRepository logins,
    Func<IHumanActionCommitFinalizer> commitFinalizer)
{
    private readonly AsyncLocal<IdentityGateScope?> _activeScope = new();

    public Task AfterEnrollmentCommitAsync(Func<Task> operation)
    {
        if (_activeScope.Value is not { } scope)
            return operation();
        scope.AfterCommit.Add(operation);
        return Task.CompletedTask;
    }

    public void DeferNativeIdentityCommit(IAsyncDisposable transaction, Func<Task> commit)
    {
        IdentityGateScope scope = _activeScope.Value
            ?? throw new InvalidOperationException("Native identity ownership requires the enrollment gate.");
        scope.Resources.Add(transaction);
        scope.NativeCommits.Add(commit);
    }

    public async Task EnsureSubjectMayEnrollAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_activeScope.Value is not { CanCheckSubjects: true })
            throw new InvalidOperationException("Existing-user linkage requires the identity enrollment gate.");
        if (_activeScope.Value.RequireOriginalOwner is { } requireOwner)
            await requireOwner(cancellationToken);
        if (await fenceAuthority.IsSubjectFencedAsync(userId, cancellationToken))
            throw new InvalidOperationException("privacy_identity_erased");
        if (await recognition.GuardExistingSubjectAsync(userId, cancellationToken)
            != ModerationRecognitionContinuityFailure.None)
            throw new InvalidOperationException("identity_continuity_unavailable");
    }

    public async Task MatchAndBindAsync(ProviderAccountKey account, Guid subjectId, Guid bindingId, Guid? tenantId,
        bool replacementProvisioning, CancellationToken cancellationToken)
    {
        IdentityGateScope scope = _activeScope.Value
            ?? throw new InvalidOperationException("Identity recognition requires the enrollment gate.");
        if (scope.RequireOriginalOwner is { } requireOwner)
            await requireOwner(cancellationToken);
        if (!replacementProvisioning)
        {
            // An exact already-bound account is signing in, not obtaining replacement
            // enrollment. Its protected actions use committed continuity (including
            // deferred recovery); an enrollment-purpose sanction is not a login ban.
            UserExternalLogin? current = await logins.GetByProviderAndKey(account);
            if (current is null || current.Id != bindingId || current.UserId != subjectId
                || current.AuthenticationProviderId != (int)account.ProviderKind
                || !string.Equals(current.ProviderKey, account.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("identity_continuity_binding_changed");
            await EnsureSubjectMayEnrollAsync(subjectId, cancellationToken);
            scope.Bindings.Add(new(account, subjectId, bindingId));
            return;
        }
        ModerationRecognitionContinuityResult result = await recognition.MatchAndBindAsync(
            new(account, subjectId, tenantId) { VerifiedBindingId = bindingId }, cancellationToken);
        if (result.AdmissionFailure != ModerationRecognitionContinuityFailure.None
            || result.ActionFailure != ModerationRecognitionContinuityFailure.None && !result.ActionRecoveryStaged)
            throw new InvalidOperationException("identity_continuity_unavailable");
        if (result.AdmissionExcluded)
            throw new InvalidOperationException("identity_enrollment_excluded");
        if (result.CurrentUntil is { } until && (scope.CurrentUntil is null || until < scope.CurrentUntil))
            scope.CurrentUntil = until;
        scope.Bindings.Add(new(account, subjectId, bindingId));
        EnsureCurrent();
    }

    public async Task EnsureCurrentAsync(CancellationToken token)
    {
        IdentityGateScope scope = _activeScope.Value
            ?? throw new InvalidOperationException("Identity commit requires the enrollment gate.");
        await EnsureCurrentAsync(scope, token);
    }

    private async Task EnsureCurrentAsync(IdentityGateScope scope, CancellationToken token)
    {
        if (scope.RequireOriginalOwner is { } requireOwner)
            await requireOwner(token);
        foreach (BindingProof proof in scope.Bindings)
        {
            token.ThrowIfCancellationRequested();
            UserExternalLogin? current = await logins.GetByProviderAndKey(proof.Account);
            if (current is null || current.Id != proof.BindingId || current.UserId != proof.SubjectId
                || current.AuthenticationProviderId != (int)proof.Account.ProviderKind
                || !string.Equals(current.ProviderKey, proof.Account.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("identity_continuity_binding_changed");
        }
        token.ThrowIfCancellationRequested();
        EnsureCurrent(scope);
    }

    public void EnsureCurrent()
    {
        IdentityGateScope scope = _activeScope.Value
            ?? throw new InvalidOperationException("Identity commit requires the enrollment gate.");
        EnsureCurrent(scope);
    }

    private void EnsureCurrent(IdentityGateScope scope)
    {
        if (scope.CurrentUntil is { } until && clock.GetUtcNow() >= until)
            throw new InvalidOperationException("identity_continuity_expired");
    }

    public Task<T> ExecuteEnrollmentAsync<T>(
        ProviderAccountKey account,
        Func<CancellationToken, Task<T>> enrollment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        return ExecuteIdentityMutationAsync(enrollment, null, cancellationToken);
    }

    public Task<T> ExecuteEnrollmentAsync<T>(ProviderAccountKey account,
        Func<CancellationToken, Task<T>> enrollment, Func<CancellationToken, Task> beforePrimaryCommit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(beforePrimaryCommit);
        return ExecuteIdentityMutationAsync(enrollment, beforePrimaryCommit, cancellationToken);
    }

    /// <summary>
    /// Supplies recognition continuity to an original physical owner without opening or
    /// borrowing a transaction or an identity gate. The owner callback must acquire its
    /// shared privacy sources, register the supplied guard at every actual commit, settle
    /// and dispose its physical owner, and consume its receipt before returning a decision.
    /// The decision controls after-commit callbacks only; it is not a settlement witness.
    /// </summary>
    public async Task<T> ExecuteOriginalOwnerIdentityMutationAsync<T>(
        Func<IdentityEnrollmentCommitGuard, CancellationToken, Task<OriginalOwnerDecision<T>>> settleOriginalOwner,
        Func<CancellationToken, Task> requireOriginalOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settleOriginalOwner);
        ArgumentNullException.ThrowIfNull(requireOriginalOwner);
        if (_activeScope.Value is not null)
            throw new InvalidOperationException("Original identity ownership cannot borrow an active enrollment scope.");

        IdentityGateScope? committed = null;
        int entered = 0;
        OriginalOwnerDecision<T> decision = await recognition.ExecuteWithRecognitionLockAsync(async token =>
        {
            if (Interlocked.Exchange(ref entered, 1) != 0)
                throw new InvalidOperationException("Original identity ownership cannot be replayed.");
            var scope = new IdentityGateScope { RequireOriginalOwner = requireOriginalOwner };
            var guard = new IdentityEnrollmentCommitGuard(
                inner => EnsureCurrentAsync(scope, inner), () => EnsureCurrent(scope));
            _activeScope.Value = scope;
            try
            {
                OriginalOwnerDecision<T> result = await settleOriginalOwner(guard, token);
                if (result.Disposition == OriginalOwnerDisposition.Committed)
                {
                    foreach (Func<Task> commit in scope.NativeCommits)
                        await commit();
                    committed = scope;
                }
                return result;
            }
            finally
            {
                _activeScope.Value = null;
                for (int index = scope.Resources.Count - 1; index >= 0; index--)
                    await scope.Resources[index].DisposeAsync();
            }
        }, cancellationToken);
        if (committed is not null)
            foreach (Func<Task> afterCommit in committed.AfterCommit)
                await afterCommit();
        return decision.Value;
    }

    public sealed class IdentityEnrollmentCommitGuard
    {
        private readonly Func<CancellationToken, Task> validate;
        private readonly Action requireCurrent;

        internal IdentityEnrollmentCommitGuard(Func<CancellationToken, Task> validate, Action requireCurrent)
        {
            this.validate = validate;
            this.requireCurrent = requireCurrent;
        }

        public Task ValidateAsync(CancellationToken cancellationToken) => validate(cancellationToken);
        public void RequireCurrent(DateTimeOffset _) => requireCurrent();
    }

    /// <summary>
    /// Owns one human enrollment settlement. A co-located embedded authority is itself
    /// the physical owner; a separate authority gate encloses, but never replaces, the
    /// selected application owner. Result publication follows disposal and receipt consumption.
    /// </summary>
    public Task<T> ExecuteSettledEnrollmentAsync<T>(
        ProviderAccountKey account,
        object invocation,
        IOriginalOwnerSettlementBoundary owner,
        Func<CancellationToken, Task<OriginalOwnerDecision<T>>> enrollment,
        CancellationToken cancellationToken) =>
        recognition.ExecuteWithRecognitionLockAsync(
            token => ExecuteSettledEnrollmentCoreAsync(account, invocation, owner, enrollment, token), cancellationToken);

    private async Task<T> ExecuteSettledEnrollmentCoreAsync<T>(
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
        var scope = new IdentityGateScope();
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
                        commitFinalizer().Register(token => EnsureCurrentAsync(scope, token), _ => EnsureCurrent(scope));
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
                    .ExecuteOriginalEnrollmentAsync(ExecuteOwner,
                        token => EnsureCurrentAsync(scope, token), _ => EnsureCurrent(scope), cancellationToken)
                : await ExecuteOwner(cancellationToken);
            var disposition = proposedDisposition
                ?? throw new InvalidOperationException("Enrollment did not propose a physical disposition.");
            T result = owner.Consume(invocation, receipt, disposition);
            if (disposition == OriginalOwnerDisposition.Committed)
            {
                foreach (Func<Task> commit in scope.NativeCommits)
                    await commit();
                foreach (Func<Task> afterCommit in scope.AfterCommit)
                    await afterCommit();
            }
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
        finally
        {
            for (int index = scope.Resources.Count - 1; index >= 0; index--)
                await scope.Resources[index].DisposeAsync();
        }
    }

    public Task<T> ExecuteIdentityMutationAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => ExecuteIdentityMutationAsync(operation, null, cancellationToken);

    private async Task<T> ExecuteIdentityMutationAsync<T>(Func<CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task>? beforePrimaryCommit, CancellationToken cancellationToken)
    {
        if (_activeScope.Value is not null)
        {
            if (beforePrimaryCommit is not null)
                throw new InvalidOperationException("A native commit participant requires the owning enrollment gate.");
            return await operation(cancellationToken);
        }
        IdentityGateScope? committedScope = null;
        T result = await recognition.ExecuteWithRecognitionLockAsync(async outer =>
        {
            // A provider execution strategy may replay the entire boundary. Never carry
            // a previous attempt's lease, pending callback or external native transaction.
            var scope = new IdentityGateScope();
            try
            {
                T value = await fenceAuthority.ExecuteEnrollmentSerializedAsync(async token =>
                {
                    _activeScope.Value = scope;
                    try { return await operation(token); }
                    finally { _activeScope.Value = null; }
                }, async token =>
                {
                    await EnsureCurrentAsync(scope, token);
                    if (beforePrimaryCommit is not null) await beforePrimaryCommit(token);
                }, outer);
                foreach (Func<Task> commit in scope.NativeCommits)
                    await commit();
                committedScope = scope;
                return value;
            }
            finally
            {
                for (int index = scope.Resources.Count - 1; index >= 0; index--)
                    await scope.Resources[index].DisposeAsync();
            }
        }, cancellationToken);
        foreach (Func<Task> afterCommit in committedScope!.AfterCommit)
            await afterCommit();
        return result;
    }

    private sealed class IdentityGateScope(bool canCheckSubjects = true)
    {
        public bool CanCheckSubjects { get; } = canCheckSubjects;
        public Func<CancellationToken, Task>? RequireOriginalOwner { get; init; }
        public List<Func<Task>> AfterCommit { get; } = [];
        public DateTimeOffset? CurrentUntil { get; set; }
        public HashSet<BindingProof> Bindings { get; } = [];
        public List<IAsyncDisposable> Resources { get; } = [];
        public List<Func<Task>> NativeCommits { get; } = [];
    }

    private sealed record BindingProof(ProviderAccountKey Account, Guid SubjectId, Guid BindingId);
}
