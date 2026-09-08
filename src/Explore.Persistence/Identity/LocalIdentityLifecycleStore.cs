// ABOUTME: Owns selected-store, purpose-bound Local lifecycle operations and one-use Identity mutations.
// ABOUTME: Uses native token/password providers and independent mirror receipts without creating sessions.

using System.Data;
using System.Data.Common;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityLifecycleStore(
    DbContext identityDbContext,
    ExploreDbContext applicationDbContext,
    UserManager<LocalIdentityUser> userManager,
    TimeProvider timeProvider,
    LocalIdentityCredentialStateStore credentialStates) : ILocalIdentityLifecycleStore
{
    public async Task<LocalIdentityLifecycleRequest?> FindRequestByIdentifierAsync(string identifier,
        LocalIdentityLifecyclePurpose purpose, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        EnsureIdle();
        if (identifier.Length > 256 || purpose is not (LocalIdentityLifecyclePurpose.EmailVerification or LocalIdentityLifecyclePurpose.PasswordRecovery)) return null;
        string value = identifier.Trim();
        bool email = value.Contains('@', StringComparison.Ordinal);
        string? normalized = email ? userManager.NormalizeEmail(value) : userManager.NormalizeName(value);
        LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking().SingleOrDefaultAsync(
            row => email ? row.NormalizedEmail == normalized : row.NormalizedUserName == normalized, cancellationToken);
        if (user?.Email is null || user.EmailConfirmed != (purpose == LocalIdentityLifecyclePurpose.PasswordRecovery)) return null;
        LocalIdentityBinding? binding = await credentialStates.ReadLinkedIdentityAsync(user.Id, cancellationToken);
        return binding?.CredentialState != LocalCredentialState.Ready ? null : new LocalIdentityLifecycleRequest(
            user.Id, binding.PersonalActorId, binding.ExternalLoginId, purpose, user.Email);
    }

    public Task<LocalIdentityLifecycleOutcome> ChangePasswordAsync(LocalIdentityPasswordChangeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureIdle();
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => ChangePasswordAttemptAsync(request, cancellationToken), cancellationToken));
    }

    private async Task<LocalIdentityLifecycleOutcome> ChangePasswordAttemptAsync(LocalIdentityPasswordChangeRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        LocalIdentityUser? user = await credentialStates.ReadReadySessionAsync(request.Authority, cancellationToken);
        LocalIdentityBinding? binding = await credentialStates.ReadLinkedIdentityAsync(request.Authority.LocalSubjectId, cancellationToken);
        if (user is null || binding is null || binding.PersonalActorId != request.PersonalActorId || binding.ExternalLoginId != request.ExternalLoginId
            || request.CurrentPassword.Length is < LocalIdentityOptions.MinimumPasswordLength or > LocalIdentityOptions.MaximumPasswordLength
            || userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, request.CurrentPassword) == PasswordVerificationResult.Failed)
            return LocalIdentityLifecycleOutcome.Invalid;
        LocalIdentityLifecycleOutcome? invalid = await ValidatePasswordAsync(user, request.NewPassword, cancellationToken);
        if (invalid is { } rejected) return rejected;
        if (await credentialStates.ReadReadySessionAsync(request.Authority, cancellationToken) is null) return LocalIdentityLifecycleOutcome.Invalid;
        string hash = userManager.PasswordHasher.HashPassword(user, request.NewPassword);
        string securityStamp = Guid.CreateVersion7().ToString("N");
        string concurrencyStamp = Guid.CreateVersion7().ToString("N");
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        int rows = await identityDbContext.Set<LocalIdentityUser>().Where(row => row.Id == user.Id
                && row.SecurityStamp == request.Authority.SecurityStamp && row.ConcurrencyStamp == user.ConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordHash, hash)
                .SetProperty(row => row.SecurityStamp, securityStamp).SetProperty(row => row.ConcurrencyStamp, concurrencyStamp)
                .SetProperty(row => row.UpdatedAt, now), cancellationToken);
        if (rows != 1) return LocalIdentityLifecycleOutcome.Conflict;
        await transaction.CommitAsync(cancellationToken);
        return LocalIdentityLifecycleOutcome.Consumed;
    }

    public Task<bool> ExecuteSynchronizationAsync(LocalIdentityLifecycleConsumption authority,
        Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(synchronize);
        EnsureIdle();
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => SynchronizeAttemptAsync(authority.Operation, authority.Token, synchronize, cancellationToken), cancellationToken));
    }

    public Task<bool> ExecuteSynchronizationAsync(LocalIdentityLifecyclePointer operation,
        Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(synchronize);
        EnsureIdle();
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => SynchronizeAttemptAsync(operation, null, synchronize, cancellationToken), cancellationToken));
    }

    private async Task<bool> SynchronizeAttemptAsync(LocalIdentityLifecyclePointer pointer, string? token,
        Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken)
    {
        await using var transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        LocalIdentityLifecycleOperation? operation = await ReadOperationAsync(pointer, cancellationToken);
        if (operation?.ConsumedAt is not { } consumedAt || operation.ResultSecurityStamp is null) return false;
        LocalIdentityUser? user = await ReadCurrentUserAsync(operation, operation.ResultSecurityStamp, cancellationToken);
        if (user is null) return false;
        if (token is not null)
        {
            if (!WithinDeadline(operation)) return false;
            user.SecurityStamp = operation.SecurityStamp;
            bool authorized = await userManager.VerifyUserTokenAsync(user, Provider(operation.Purpose), TokenPurpose(operation), token);
            user.SecurityStamp = operation.ResultSecurityStamp;
            cancellationToken.ThrowIfCancellationRequested();
            if (!authorized || !WithinDeadline(operation)) return false;
        }
        string concurrencyStamp = Guid.CreateVersion7().ToString("N");
        int locked = await identityDbContext.Set<LocalIdentityUser>().Where(row => row.Id == user.Id
                && row.SecurityStamp == operation.ResultSecurityStamp && row.ConcurrencyStamp == user.ConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, concurrencyStamp), cancellationToken);
        if (locked != 1) return false;
        // This native user write lock is also acquired by every existing credential mutation. In the external
        // topology keep it through the Application mirror commit, then acknowledge; retries are idempotent.
        await using var applicationTransaction = ReferenceEquals(identityDbContext, applicationDbContext) ? null
            : await applicationDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            LocalIdentityUser? current = await ReadCurrentUserAsync(operation, operation.ResultSecurityStamp, cancellationToken);
            if (current is null || (token is not null && !WithinDeadline(operation))) return false;
            var receipt = Synchronization(operation, current, consumedAt, operation.SynchronizedAt.HasValue);
            if (!await synchronize(receipt, cancellationToken) || (token is not null && !WithinDeadline(operation))) return false;
            await applicationDbContext.SaveChangesAsync(cancellationToken);
            if (token is not null && !WithinDeadline(operation)) return false;
            if (applicationTransaction is not null) await applicationTransaction.CommitAsync(cancellationToken);
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            await identityDbContext.Set<LocalIdentityLifecycleOperation>().Where(row => row.Id == operation.Id && row.Generation == operation.Generation)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SynchronizedAt, now), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        finally { applicationDbContext.ChangeTracker.Clear(); }
    }

    public Task<LocalIdentityLifecyclePointer?> BeginAsync(LocalIdentityLifecycleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureIdle();
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => BeginAttemptAsync(request, cancellationToken), cancellationToken));
    }

    private async Task<LocalIdentityLifecyclePointer?> BeginAttemptAsync(LocalIdentityLifecycleRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        LocalIdentityBinding? binding = await credentialStates.ReadLinkedIdentityAsync(request.LocalSubjectId, cancellationToken);
        if (binding is null || binding.CredentialState != LocalCredentialState.Ready
            || binding.PersonalActorId != request.PersonalActorId || binding.ExternalLoginId != request.ExternalLoginId) return null;
        LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.LocalSubjectId, cancellationToken);
        if (user is null || string.IsNullOrEmpty(user.SecurityStamp) || string.IsNullOrEmpty(user.ConcurrencyStamp)) return null;
        LocalCredentialStateMetadata? state = await credentialStates.ReadAsync(user.Id, user.SecurityStamp, cancellationToken);
        if (state?.State != LocalCredentialState.Ready) return null;
        string address = request.Address;
        string? normalizedAddress = userManager.NormalizeEmail(address);
        if (request.Purpose == LocalIdentityLifecyclePurpose.EmailChange)
        {
            if (!string.Equals(request.ExpectedSecurityStamp, user.SecurityStamp, StringComparison.Ordinal)
                || string.Equals(normalizedAddress, user.NormalizedEmail, StringComparison.Ordinal)) return null;
            user.Email = address;
            if (!await ValidateUserAsync(user, cancellationToken)) return null;
        }
        else
        {
            if (user.Email is null || !string.Equals(normalizedAddress, user.NormalizedEmail, StringComparison.Ordinal)
                || (request.Purpose == LocalIdentityLifecyclePurpose.EmailVerification && user.EmailConfirmed)
                || (request.Purpose == LocalIdentityLifecyclePurpose.PasswordRecovery && !user.EmailConfirmed)) return null;
            address = user.Email;
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        LocalIdentityLifecycleOperation? pending = await identityDbContext.Set<LocalIdentityLifecycleOperation>().AsNoTracking()
            .Where(row => row.LocalSubjectId == user.Id && row.Purpose == request.Purpose
                && row.SecurityStamp == user.SecurityStamp && row.CredentialOperationId == state.OperationId
                && row.PersonalActorId == binding.PersonalActorId && row.ExternalLoginId == binding.ExternalLoginId
                && row.ConsumedAt == null && row.CreatedAt <= now && row.ExpiresAt > now)
            .OrderBy(row => row.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (pending is not null)
        {
            // A different proposed address cannot silently revoke an in-flight link, either.
            return string.Equals(userManager.NormalizeEmail(pending.PendingAddress), normalizedAddress, StringComparison.Ordinal)
                ? pending.Pointer() : null;
        }
        DateTime budgetStart = now.AddHours(-1);
        if (await identityDbContext.Set<LocalIdentityLifecycleOperation>().CountAsync(row => row.LocalSubjectId == user.Id
            && row.Purpose == request.Purpose && row.CreatedAt > budgetStart, cancellationToken) >= 3) return null;
        var pointer = new LocalIdentityLifecyclePointer(Guid.CreateVersion7(), user.Id, binding.PersonalActorId,
            binding.ExternalLoginId, request.Purpose, Guid.CreateVersion7());
        var operation = new LocalIdentityLifecycleOperation(pointer, address, user.SecurityStamp, state.OperationId, now);
        // Serialize new intake against native user/reset writes without rotating the security stamp or invalidating links.
        string concurrencyStamp = Guid.CreateVersion7().ToString("N");
        int changed = await identityDbContext.Set<LocalIdentityUser>()
            .Where(row => row.Id == user.Id && row.SecurityStamp == user.SecurityStamp && row.ConcurrencyStamp == user.ConcurrencyStamp)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, concurrencyStamp), cancellationToken);
        if (changed != 1) return null;
        identityDbContext.Add(operation);
        try
        {
            await identityDbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { identityDbContext.Entry(operation).State = EntityState.Detached; }
        return pointer;
    }

    public async Task<LocalIdentityLifecycleTransport?> IssueTransportTokenAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureIdle();
        LocalIdentityLifecycleOperation? row = await ReadOperationAsync(operation, cancellationToken);
        if (row is null || !IsLive(row)) return null;
        LocalIdentityUser? user = await ReadCurrentUserAsync(row, row.SecurityStamp, cancellationToken);
        if (user is null) return null;
        string token = await userManager.GenerateUserTokenAsync(user, Provider(row.Purpose), TokenPurpose(row));
        cancellationToken.ThrowIfCancellationRequested();
        // Generation can await a provider; do not release material after expiry/reset/consumption.
        LocalIdentityLifecycleOperation? current = await ReadOperationAsync(operation, cancellationToken);
        if (current is null || !IsLive(current)
            || await ReadCurrentUserAsync(current, current.SecurityStamp, cancellationToken) is null || !IsLive(current)) return null;
        return new LocalIdentityLifecycleTransport(operation, row.PendingAddress, token, new DateTimeOffset(row.ExpiresAt));
    }

    public Task<LocalIdentityLifecycleResult> ConsumeAsync(LocalIdentityLifecycleConsumption request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureIdle();
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(() =>
            EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() => ConsumeAttemptAsync(request, cancellationToken), cancellationToken));
    }

    private async Task<LocalIdentityLifecycleResult> ConsumeAttemptAsync(LocalIdentityLifecycleConsumption request, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            LocalIdentityLifecycleOperation? operation = await ReadOperationAsync(request.Operation, cancellationToken);
            if (operation is null || !IsLive(operation)) return Rejected(LocalIdentityLifecycleOutcome.Invalid);
            LocalIdentityUser? user = await ReadCurrentUserAsync(operation, operation.SecurityStamp, cancellationToken);
            if (user is null || !await userManager.VerifyUserTokenAsync(user, Provider(operation.Purpose), TokenPurpose(operation), request.Token))
                return Rejected(LocalIdentityLifecycleOutcome.Invalid);
            cancellationToken.ThrowIfCancellationRequested();
            string? passwordHash = user.PasswordHash;
            if (operation.Purpose == LocalIdentityLifecyclePurpose.PasswordRecovery)
            {
                LocalIdentityLifecycleOutcome? invalid = await ValidatePasswordAsync(user, request.NewPassword, cancellationToken);
                if (invalid is { } rejected) return Rejected(rejected);
                passwordHash = userManager.PasswordHasher.HashPassword(user, request.NewPassword!);
            }
            else
            {
                if (request.NewPassword is not null) return Rejected(LocalIdentityLifecycleOutcome.Invalid);
                user.Email = operation.PendingAddress;
                if (!await ValidateUserAsync(user, cancellationToken)) return Rejected(LocalIdentityLifecycleOutcome.Conflict);
                user.NormalizedEmail = userManager.NormalizeEmail(user.Email);
                user.EmailConfirmed = true;
            }
            if (!IsLive(operation)
                || await ReadCurrentUserAsync(operation, operation.SecurityStamp, cancellationToken) is null)
                return Rejected(LocalIdentityLifecycleOutcome.Invalid);
            string securityStamp = Guid.CreateVersion7().ToString("N");
            string concurrencyStamp = Guid.CreateVersion7().ToString("N");
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            int users = await identityDbContext.Set<LocalIdentityUser>()
                .Where(row => row.Id == user.Id && row.SecurityStamp == operation.SecurityStamp && row.ConcurrencyStamp == user.ConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordHash, passwordHash)
                    .SetProperty(row => row.Email, user.Email).SetProperty(row => row.NormalizedEmail, user.NormalizedEmail)
                    .SetProperty(row => row.EmailConfirmed, user.EmailConfirmed)
                    .SetProperty(row => row.SecurityStamp, securityStamp).SetProperty(row => row.ConcurrencyStamp, concurrencyStamp)
                    .SetProperty(row => row.UpdatedAt, now), cancellationToken);
            // Recheck time after the awaited write; failure rolls back both receipt and Identity mutation.
            DateTime consumedAt = timeProvider.GetUtcNow().UtcDateTime;
            if (!IsLive(operation)) return Rejected(LocalIdentityLifecycleOutcome.Invalid);
            int operations = await identityDbContext.Set<LocalIdentityLifecycleOperation>()
                .Where(row => row.Id == operation.Id && row.Generation == operation.Generation && row.ConsumedAt == null
                    && row.SecurityStamp == operation.SecurityStamp && row.ExpiresAt > consumedAt && row.CreatedAt <= consumedAt)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConsumedAt, consumedAt)
                    .SetProperty(row => row.ResultSecurityStamp, securityStamp), cancellationToken);
            if (users != 1 || operations != 1) return Rejected(LocalIdentityLifecycleOutcome.Conflict);
            await transaction.CommitAsync(cancellationToken);
            return new LocalIdentityLifecycleResult(LocalIdentityLifecycleOutcome.Consumed,
                Synchronization(operation, user, consumedAt, synchronized: false));
        }
        catch (DbException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(new DbUpdateException("Native lifecycle uniqueness conflict.", exception)))
        {
            return Rejected(LocalIdentityLifecycleOutcome.Conflict);
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            return Rejected(LocalIdentityLifecycleOutcome.Conflict);
        }
    }

    public async Task<LocalIdentityLifecycleSynchronization?> ReadSynchronizationAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureIdle();
        return await ReadSynchronizationCoreAsync(operation, cancellationToken);
    }

    private async Task<LocalIdentityLifecycleSynchronization?> ReadSynchronizationCoreAsync(LocalIdentityLifecyclePointer pointer, CancellationToken cancellationToken)
    {
        LocalIdentityLifecycleOperation? operation = await ReadOperationAsync(pointer, cancellationToken);
        if (operation?.ConsumedAt is not { } consumedAt || operation.ResultSecurityStamp is null) return null;
        LocalIdentityUser? user = await ReadCurrentUserAsync(operation, operation.ResultSecurityStamp, cancellationToken);
        return user is null ? null : Synchronization(operation, user, consumedAt, operation.SynchronizedAt.HasValue);
    }

    public async Task<bool> MarkSynchronizedAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        EnsureIdle();
        return await identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await identityDbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            LocalIdentityLifecycleSynchronization? receipt = await ReadSynchronizationCoreAsync(operation, cancellationToken);
            if (receipt is null) return false;
            if (receipt.Synchronized) return true;
            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            int rows = await identityDbContext.Set<LocalIdentityLifecycleOperation>()
                .Where(row => row.Id == operation.OperationId && row.Generation == operation.Generation && row.ConsumedAt != null && row.SynchronizedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.SynchronizedAt, now), cancellationToken);
            if (rows != 1) return false;
            await transaction.CommitAsync(cancellationToken);
            return true;
        });
    }

    private Task<LocalIdentityLifecycleOperation?> ReadOperationAsync(LocalIdentityLifecyclePointer pointer, CancellationToken cancellationToken) =>
        identityDbContext.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == pointer.OperationId && row.LocalSubjectId == pointer.LocalSubjectId && row.PersonalActorId == pointer.PersonalActorId
            && row.ExternalLoginId == pointer.ExternalLoginId && row.Purpose == pointer.Purpose && row.Generation == pointer.Generation, cancellationToken);

    private async Task<LocalIdentityUser?> ReadCurrentUserAsync(LocalIdentityLifecycleOperation operation, string securityStamp, CancellationToken cancellationToken)
    {
        LocalIdentityBinding? binding = await credentialStates.ReadLinkedIdentityAsync(operation.LocalSubjectId, cancellationToken);
        if (binding is null || binding.CredentialState != LocalCredentialState.Ready
            || binding.PersonalActorId != operation.PersonalActorId || binding.ExternalLoginId != operation.ExternalLoginId) return null;
        LocalCredentialStateMetadata? state = await credentialStates.ReadAsync(operation.LocalSubjectId, securityStamp, cancellationToken);
        if (state?.State != LocalCredentialState.Ready || state.OperationId != operation.CredentialOperationId) return null;
        LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == operation.LocalSubjectId && row.SecurityStamp == securityStamp, cancellationToken);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(user.ConcurrencyStamp)) return null;
        if (operation.ConsumedAt is null && operation.Purpose != LocalIdentityLifecyclePurpose.EmailChange
            && (!string.Equals(user.NormalizedEmail, userManager.NormalizeEmail(operation.PendingAddress), StringComparison.Ordinal)
                || user.EmailConfirmed != (operation.Purpose == LocalIdentityLifecyclePurpose.PasswordRecovery))) return null;
        return user;
    }

    private async Task<LocalIdentityLifecycleOutcome?> ValidatePasswordAsync(LocalIdentityUser user, string? password, CancellationToken cancellationToken)
    {
        if (password is null || password.Length is < LocalIdentityOptions.MinimumPasswordLength or > LocalIdentityOptions.MaximumPasswordLength)
            return LocalIdentityLifecycleOutcome.InvalidPassword;
        if (userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, password) != PasswordVerificationResult.Failed)
            return LocalIdentityLifecycleOutcome.SamePassword;
        foreach (IPasswordValidator<LocalIdentityUser> validator in userManager.PasswordValidators)
        {
            IdentityResult validation = await validator.ValidateAsync(userManager, user, password);
            cancellationToken.ThrowIfCancellationRequested();
            if (!validation.Succeeded) return LocalIdentityLifecycleOutcome.InvalidPassword;
        }
        return null;
    }

    private async Task<bool> ValidateUserAsync(LocalIdentityUser user, CancellationToken cancellationToken)
    {
        if (user.Email is null || user.Email.Length > 256) return false;
        foreach (IUserValidator<LocalIdentityUser> validator in userManager.UserValidators)
        {
            IdentityResult result = await validator.ValidateAsync(userManager, user);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Succeeded) return false;
        }
        string? normalized = userManager.NormalizeEmail(user.Email);
        return !await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .AnyAsync(row => row.Id != user.Id && row.NormalizedEmail == normalized, cancellationToken);
    }

    private bool IsLive(LocalIdentityLifecycleOperation operation) => operation.ConsumedAt is null && WithinDeadline(operation);

    private bool WithinDeadline(LocalIdentityLifecycleOperation operation)
    {
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        return operation.CreatedAt <= now && now < operation.ExpiresAt;
    }

    private static string Provider(LocalIdentityLifecyclePurpose purpose) => purpose == LocalIdentityLifecyclePurpose.PasswordRecovery
        ? LocalIdentityLifecycleTokenProviders.Recovery : LocalIdentityLifecycleTokenProviders.Verification;

    private static string TokenPurpose(LocalIdentityLifecycleOperation operation)
    {
        string nativePurpose = operation.Purpose switch
        {
            LocalIdentityLifecyclePurpose.EmailVerification => UserManager<LocalIdentityUser>.ConfirmEmailTokenPurpose,
            LocalIdentityLifecyclePurpose.EmailChange => "ChangeEmail",
            LocalIdentityLifecyclePurpose.PasswordRecovery => UserManager<LocalIdentityUser>.ResetPasswordTokenPurpose,
            _ => throw new InvalidOperationException("Unknown lifecycle purpose.")
        };
        return $"{nativePurpose}:{operation.Id:D}:{operation.Generation:D}:{operation.PersonalActorId:D}:{operation.ExternalLoginId:D}:{operation.PendingAddress}";
    }

    private static LocalIdentityLifecycleSynchronization Synchronization(LocalIdentityLifecycleOperation operation,
        LocalIdentityUser user, DateTime consumedAt, bool synchronized) => new(operation.Pointer(), user.Email, user.EmailConfirmed,
            user.FirstName, user.LastName, new DateTimeOffset(consumedAt), synchronized);

    private static LocalIdentityLifecycleResult Rejected(LocalIdentityLifecycleOutcome outcome) => new(outcome);

    private void EnsureIdle()
    {
        if (!identityDbContext.Database.IsRelational() || !applicationDbContext.Database.IsRelational())
            throw new InvalidOperationException("Local lifecycle authority requires relational stores.");
        if (identityDbContext.Database.CurrentTransaction is not null || applicationDbContext.Database.CurrentTransaction is not null
            || identityDbContext.ChangeTracker.HasChanges() || applicationDbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Local lifecycle authority requires idle contexts without caller-owned transactions or pending writes.");
    }
}
