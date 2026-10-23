using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class PrivacyIdentityFenceOperation(
    IPrivacyIdentityFenceAuthority fenceAuthority,
    IModerationRecognitionContinuity recognition,
    TimeProvider clock,
    IUserExternalLoginRepository logins)
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
        if (_activeScope.Value is null)
            throw new InvalidOperationException("Existing-user linkage requires the identity enrollment gate.");
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

    private sealed class IdentityGateScope
    {
        public List<Func<Task>> AfterCommit { get; } = [];
        public DateTimeOffset? CurrentUntil { get; set; }
        public HashSet<BindingProof> Bindings { get; } = [];
        public List<IAsyncDisposable> Resources { get; } = [];
        public List<Func<Task>> NativeCommits { get; } = [];
    }

    private sealed record BindingProof(ProviderAccountKey Account, Guid SubjectId, Guid BindingId);
}
