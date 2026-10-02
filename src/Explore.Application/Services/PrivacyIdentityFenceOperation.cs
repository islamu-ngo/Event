using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class PrivacyIdentityFenceOperation(
    IPrivacyIdentityFenceAuthority fenceAuthority,
    IPrivacyErasureAuthority authority,
    IPrivacyIdentityFenceKeyProvider keyProvider,
    IPrivacyIdentityBindingReader bindings)
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
        return ExecuteWithKeyAsync(async (key, token) =>
        {
            if (await fenceAuthority.FindAsync(key.Fingerprint(account), token) is not null)
                throw new InvalidOperationException("privacy_identity_erased");
            return await enrollment(token);
        }, cancellationToken);
    }

    public Task<PrivacyErasureIntent> CaptureAndAppendAsync(
        PrivacyErasureRequest request, CancellationToken cancellationToken) =>
        ExecuteWithKeyAsync(async (key, token) =>
        {
            List<UserExternalLogin> current = await bindings.GetByUser(request.SubjectId);
            token.ThrowIfCancellationRequested();
            PrivacyIdentityFingerprint[] fingerprints = current
                .Where(binding => binding.AuthenticationProviderId != (int)AuthenticationProviderKind.Local)
                .Select(binding => key.Fingerprint(new ProviderAccountKey(
                    (AuthenticationProviderKind)binding.AuthenticationProviderId, binding.ProviderKey)))
                .Distinct().ToArray();
            return await authority.AppendAsync(new PrivacyErasureRequest(
                request.IntentId, request.SubjectKind, request.SubjectId, request.ReasonCode,
                request.PolicyVersion, fingerprints, key.KeyId, key.VerificationTag), token);
        }, cancellationToken);

    /// <summary>
    /// Restored or imported bindings may reference a different UUID, including after
    /// legal-hold audit pseudonymization. Append ordinary erasure facts for those
    /// users before the existing replay engine is permitted to report readiness.
    /// </summary>
    public Task<int> CaptureRestoredBindingsAsync(int policyVersion, CancellationToken cancellationToken) =>
        ExecuteWithKeyAsync(async (key, token) =>
        {
            Guid? after = null;
            var captured = new HashSet<Guid>();
            while (true)
            {
                IReadOnlyList<UserExternalLogin> batch = await bindings.ReadExternalBindingsAfterAsync(after, 100, token);
                if (batch.Count == 0)
                    return captured.Count;
                foreach (UserExternalLogin binding in batch)
                {
                    var fingerprint = key.Fingerprint(new ProviderAccountKey(
                        (AuthenticationProviderKind)binding.AuthenticationProviderId, binding.ProviderKey));
                    if (!captured.Contains(binding.UserId)
                        && await fenceAuthority.FindAsync(fingerprint, token) is not null)
                    {
                        await CaptureAndAppendAsync(PrivacyErasureRequest.Create(
                            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, binding.UserId,
                            PrivacyErasureReasonCode.PrivacyIncidentRemediation, policyVersion), token);
                        captured.Add(binding.UserId);
                    }
                    after = binding.Id;
                }
            }
        }, cancellationToken);

    private async Task<T> ExecuteWithKeyAsync<T>(
        Func<PrivacyIdentityFenceKey, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (_activeScope.Value is { } current)
            return await operation(current.Key, cancellationToken);
        // The selected secret authority may perform network I/O. Resolve before
        // taking either database's write transaction.
        using PrivacyIdentityFenceKey key = await keyProvider.ResolveAsync(cancellationToken);
        var scope = new IdentityGateScope(key);
        T result = await fenceAuthority.ExecuteSerializedAsync(async token =>
        {
            await fenceAuthority.ValidateKeyAsync(key.KeyId, key.VerificationTag, token);
            _activeScope.Value = scope;
            try { return await operation(key, token); }
            finally { _activeScope.Value = null; }
        }, cancellationToken);
        foreach (Func<Task> afterCommit in scope.AfterCommit)
            await afterCommit();
        return result;
    }

    private sealed class IdentityGateScope(PrivacyIdentityFenceKey key)
    {
        public PrivacyIdentityFenceKey Key { get; } = key;
        public List<Func<Task>> AfterCommit { get; } = [];
    }
}
