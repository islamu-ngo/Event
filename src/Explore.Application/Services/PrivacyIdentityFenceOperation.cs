using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
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
        if (_activeScope.Value is null)
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

    private sealed class IdentityGateScope
    {
        public List<Func<Task>> AfterCommit { get; } = [];
    }
}
