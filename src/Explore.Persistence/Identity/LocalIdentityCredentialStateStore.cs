// ABOUTME: Owns fresh Local credential authority and atomic creation, activation, and first-use replacement.
// ABOUTME: Fences selected-store mutations with current operation/state/stamps while preserving native password policy.

using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed class LocalIdentityCredentialStateStore(
    DbContext identityDbContext,
    ExploreDbContext applicationDbContext,
    UserManager<LocalIdentityUser> userManager,
    TimeProvider timeProvider)
    : ILocalCredentialAdministration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowDuplicateProperties = false
    };

    public async Task<LocalIdentityPage> ListAsync(LocalIdentityListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        int skip = checked((request.PageNumber - 1) * request.PageSize);
        int totalCount = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .CountAsync(cancellationToken).ConfigureAwait(false);
        var users = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .OrderBy(user => user.CreatedAt).ThenBy(user => user.Id)
            .Skip(skip).Take(request.PageSize)
            .Select(user => new
            {
                user.Id, user.Email, user.FirstName, user.LastName, user.EmailConfirmed,
                TokenValue = identityDbContext.Set<IdentityUserToken<Guid>>()
                    .Where(token => token.UserId == user.Id
                        && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                        && token.Name == LocalCredentialStateMetadata.TokenName)
                    .Select(token => token.Value).SingleOrDefault()
            }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var states = users.ToDictionary(user => user.Id, user => DeserializeMetadata(user.TokenValue));
        Guid[] operationIds = states.Values.Where(state => state is not null)
            .Select(state => state!.OperationId).Distinct().ToArray();
        Dictionary<Guid, LocalIdentityCredentialOperation> operations = await identityDbContext
            .Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .Where(operation => operationIds.Contains(operation.Id))
            .ToDictionaryAsync(operation => operation.Id, cancellationToken).ConfigureAwait(false);
        Guid[] loginIds = operations.Values.Select(operation => operation.ExternalLoginId).Distinct().ToArray();
        Guid[] actorIds = operations.Values.Select(operation => operation.PersonalActorId).Distinct().ToArray();
        var bindings = await (
            from login in applicationDbContext.UserExternalLogins.AsNoTracking()
            join user in applicationDbContext.Users.AsNoTracking() on login.UserId equals user.Id
            join actor in applicationDbContext.Actors.AsNoTracking() on user.Id equals actor.UserId
            where loginIds.Contains(login.Id) && actorIds.Contains(actor.Id)
                && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                && !user.IsDeleted && !actor.IsDeleted && !actor.IsSuspended
                && actor.ActorTypeId == (int)ActorTypeEnum.User
            select new { LoginId = login.Id, login.UserId, login.ProviderKey, ActorId = actor.Id })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var exactBindings = bindings.Select(binding =>
            (binding.LoginId, binding.UserId, binding.ProviderKey, binding.ActorId)).ToHashSet();
        var summaries = new List<LocalIdentitySummary>(users.Count);
        foreach (var user in users)
        {
            LocalCredentialStateMetadata? state = states[user.Id];
            LocalIdentityCredentialOperation? operation = state is not null
                ? operations.GetValueOrDefault(state.OperationId) : null;
            bool isCurrent = operation is not null && operation.LocalSubjectId == user.Id
                && IsCurrentMetadata(operation, state);
            bool hasExactBinding = isCurrent && exactBindings.Contains((operation!.ExternalLoginId,
                user.Id, user.Id.ToString("D"), operation.PersonalActorId));
            summaries.Add(new LocalIdentitySummary(
                localSubjectId: user.Id, email: user.Email, firstName: user.FirstName, lastName: user.LastName,
                emailVerified: user.EmailConfirmed, credentialState: isCurrent ? state!.State : null,
                currentOperationId: isCurrent ? operation!.Id : null,
                currentOperationConcurrencyStamp: isCurrent ? operation!.ConcurrencyStamp : null,
                hasExactBinding: hasExactBinding));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new LocalIdentityPage(
            items: summaries, pageNumber: request.PageNumber, pageSize: request.PageSize, totalCount: totalCount);
    }

    public async Task<LocalIdentityBinding?> ReadLinkedIdentityAsync(Guid localSubjectId, CancellationToken cancellationToken)
    {
        if (localSubjectId == Guid.Empty)
            throw new ArgumentException("A Local subject is required.", nameof(localSubjectId));
        var current = await (
            from user in identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            join token in identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking() on user.Id equals token.UserId
            where user.Id == localSubjectId
                && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && token.Name == LocalCredentialStateMetadata.TokenName
            select new { token.Value, user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || string.IsNullOrWhiteSpace(current.PasswordHash)
            || string.IsNullOrWhiteSpace(current.SecurityStamp) || string.IsNullOrWhiteSpace(current.ConcurrencyStamp))
            return null;
        LocalCredentialStateMetadata? state = DeserializeMetadata(current.Value);
        if (state?.State is not (LocalCredentialState.ChangeRequired or LocalCredentialState.Ready)
            || state.ApplicationUserId != localSubjectId)
            return null;
        LocalIdentityCredentialOperation? operation = await identityDbContext.Set<LocalIdentityCredentialOperation>()
            .AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == state.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null || operation.LocalSubjectId != localSubjectId || !IsCurrentMetadata(operation, state)
            || !await HasExactApplicationBindingAsync(operation, cancellationToken).ConfigureAwait(false))
            return null;
        return new LocalIdentityBinding(localSubjectId, operation.PersonalActorId, operation.ExternalLoginId, state.State);
    }

    public async Task<LocalCredentialOperationStatus?> ReadOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        }
        cancellationToken.ThrowIfCancellationRequested();
        LocalIdentityCredentialOperation? operation = await identityDbContext.Set<LocalIdentityCredentialOperation>()
            .AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
        {
            return null;
        }
        string? value = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .Where(token => token.UserId == operation.LocalSubjectId
                && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && token.Name == LocalCredentialStateMetadata.TokenName)
            .Select(token => token.Value).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        LocalCredentialStateMetadata? state = DeserializeMetadata(value);
        bool isCurrent = IsCurrentMetadata(operation, state);
        var receipt = new LocalCredentialOperationReceipt(
            operationId: operation.Id, kind: operation.Kind, stage: operation.Stage,
            initiatingApplicationUserId: operation.InitiatingApplicationUserId,
            localSubjectId: operation.LocalSubjectId, personalActorId: operation.PersonalActorId,
            externalLoginId: operation.ExternalLoginId, createdAt: operation.CreatedAt);
        LocalCredentialResetReceipt? resetAudit = operation.Kind == LocalCredentialOperationKind.Reset
            ? new LocalCredentialResetReceipt(
                operation: receipt, previousOperationId: operation.PreviousOperationId!.Value,
                previousOperationConcurrencyStamp: operation.PreviousOperationConcurrencyStamp!.Value,
                reason: operation.ResetReason!)
            : null;
        cancellationToken.ThrowIfCancellationRequested();
        return new LocalCredentialOperationStatus(
            receipt: receipt, operationConcurrencyStamp: operation.ConcurrencyStamp,
            verifiedByApplicationUserId: operation.VerifiedByApplicationUserId,
            verifiedAt: operation.VerifiedAt, updatedAt: operation.UpdatedAt,
            isCurrent: isCurrent, credentialState: isCurrent ? state!.State : null, resetAudit: resetAudit);
    }

    private static bool IsCurrentMetadata(LocalIdentityCredentialOperation operation, LocalCredentialStateMetadata? state) =>
        state is not null && state.OperationId == operation.Id && state.ApplicationUserId == operation.LocalSubjectId
        && operation.Kind is LocalCredentialOperationKind.Create or LocalCredentialOperationKind.Reset
        && ((state.State == LocalCredentialState.ProvisioningPending
                && operation.Kind == LocalCredentialOperationKind.Create
                && operation.Stage == LocalCredentialOperationStage.ProvisioningPending)
            || (state.State == LocalCredentialState.ChangeRequired && operation.Stage == LocalCredentialOperationStage.ChangeRequired)
            || (state.State == LocalCredentialState.Ready && operation.Stage == LocalCredentialOperationStage.Replaced));

    public Task<LocalCredentialResetResult> ResetAsync(
        LocalCredentialResetRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationContextsIdle();
        if (!identityDbContext.Database.IsRelational() || !applicationDbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Credential reset requires relational stores.");
        }
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            () => ResetAttemptAsync(request, cancellationToken));
    }

    private async Task<LocalCredentialResetResult> ResetAttemptAsync(
        LocalCredentialResetRequest request,
        CancellationToken cancellationToken)
    {
        LocalIdentityCredentialOperation? ownedOperation = null;
        DbUpdateException? uniqueFailure = null;
        try
        {
            await using var transaction = await identityDbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            try
            {
                LocalCredentialResetResult? replay = await ResolveResetReplayAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (replay is not null)
                {
                    return replay;
                }
                LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == request.LocalSubjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (user is null)
                {
                    return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.NotFound);
                }
                LocalIdentityCredentialOperation? previous = await identityDbContext
                    .Set<LocalIdentityCredentialOperation>().AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == request.ExpectedCurrentOperationId, cancellationToken)
                    .ConfigureAwait(false);
                IdentityUserToken<Guid>? token = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.UserId == request.LocalSubjectId
                        && candidate.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                        && candidate.Name == LocalCredentialStateMetadata.TokenName, cancellationToken).ConfigureAwait(false);
                LocalCredentialStateMetadata? state = DeserializeMetadata(token?.Value);
                if (previous is null || previous.LocalSubjectId != request.LocalSubjectId
                    || previous.Kind is not (LocalCredentialOperationKind.Create or LocalCredentialOperationKind.Reset)
                    || previous.ConcurrencyStamp != request.ExpectedCurrentOperationConcurrencyStamp
                    || state is null || state.OperationId != previous.Id || state.ApplicationUserId != request.LocalSubjectId
                    || !((state.State == LocalCredentialState.ChangeRequired && previous.Stage == LocalCredentialOperationStage.ChangeRequired)
                        || (state.State == LocalCredentialState.Ready && previous.Stage == LocalCredentialOperationStage.Replaced))
                    || string.IsNullOrWhiteSpace(user.PasswordHash) || string.IsNullOrWhiteSpace(user.SecurityStamp)
                    || string.IsNullOrWhiteSpace(user.ConcurrencyStamp))
                {
                    return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Conflict);
                }
                if (!await HasExactApplicationBindingAsync(previous, cancellationToken).ConfigureAwait(false))
                {
                    return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.BindingIncomplete);
                }
                string temporaryPassword = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
                foreach (IPasswordValidator<LocalIdentityUser> validator in userManager.PasswordValidators)
                {
                    IdentityResult validation = await validator.ValidateAsync(userManager, user, temporaryPassword)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!validation.Succeeded)
                    {
                        return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Invalid);
                    }
                }
                string passwordHash = userManager.PasswordHasher.HashPassword(user, temporaryPassword);
                DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
                var receipt = new LocalCredentialResetReceipt(
                    operation: new LocalCredentialOperationReceipt(
                        operationId: request.OperationId, kind: LocalCredentialOperationKind.Reset,
                        stage: LocalCredentialOperationStage.ChangeRequired,
                        initiatingApplicationUserId: request.InitiatingApplicationUserId,
                        localSubjectId: previous.LocalSubjectId, personalActorId: previous.PersonalActorId,
                        externalLoginId: previous.ExternalLoginId, createdAt: createdAt),
                    previousOperationId: previous.Id,
                    previousOperationConcurrencyStamp: previous.ConcurrencyStamp, reason: request.Reason);
                ownedOperation = new LocalIdentityCredentialOperation(
                    receipt: receipt, concurrencyStamp: Guid.CreateVersion7(),
                    verifiedByApplicationUserId: previous.VerifiedByApplicationUserId, verifiedAt: previous.VerifiedAt);
                identityDbContext.Add(ownedOperation);
                await identityDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                Guid supersededStamp = Guid.CreateVersion7();
                string userStamp = Guid.CreateVersion7().ToString("N");
                string securityStamp = Guid.CreateVersion7().ToString("N");
                string resetState = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                    version: LocalCredentialStateMetadata.CurrentVersion, state: LocalCredentialState.ChangeRequired,
                    operationId: request.OperationId, applicationUserId: request.LocalSubjectId));
                int operationRows = await identityDbContext.Set<LocalIdentityCredentialOperation>()
                    .Where(candidate => candidate.Id == previous.Id && candidate.Kind == previous.Kind
                        && candidate.Stage == previous.Stage && candidate.ConcurrencyStamp == previous.ConcurrencyStamp)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.Stage, LocalCredentialOperationStage.Superseded)
                        .SetProperty(candidate => candidate.ConcurrencyStamp, supersededStamp)
                        .SetProperty(candidate => candidate.UpdatedAt, createdAt), cancellationToken).ConfigureAwait(false);
                int tokenRows = await identityDbContext.Set<IdentityUserToken<Guid>>()
                    .Where(candidate => candidate.UserId == request.LocalSubjectId
                        && candidate.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                        && candidate.Name == LocalCredentialStateMetadata.TokenName && candidate.Value == token!.Value)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Value, resetState), cancellationToken)
                    .ConfigureAwait(false);
                int userRows = await identityDbContext.Set<LocalIdentityUser>()
                    .Where(candidate => candidate.Id == request.LocalSubjectId
                        && candidate.ConcurrencyStamp == user.ConcurrencyStamp && candidate.SecurityStamp == user.SecurityStamp)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                        .SetProperty(candidate => candidate.ConcurrencyStamp, userStamp)
                        .SetProperty(candidate => candidate.SecurityStamp, securityStamp)
                        .SetProperty(candidate => candidate.UpdatedAt, createdAt), cancellationToken).ConfigureAwait(false);
                if (operationRows != 1 || tokenRows != 1 || userRows != 1)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Conflict);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return LocalCredentialResetResult.Reset(receipt: receipt, temporaryPassword: temporaryPassword);
            }
            catch
            {
                await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
                throw;
            }
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            uniqueFailure = exception;
        }
        finally
        {
            if (ownedOperation is not null)
            {
                identityDbContext.Entry(ownedOperation).State = EntityState.Detached;
            }
        }
        LocalCredentialResetResult? converged = await ResolveResetReplayAsync(request, cancellationToken).ConfigureAwait(false);
        if (converged is not null)
        {
            return converged;
        }
        ExceptionDispatchInfo.Capture(uniqueFailure!).Throw();
        throw new InvalidOperationException("An unresolved credential reset conflict occurred.");
    }

    private async Task<LocalCredentialResetResult?> ResolveResetReplayAsync(
        LocalCredentialResetRequest request,
        CancellationToken cancellationToken)
    {
        LocalIdentityCredentialOperation? operation = await identityDbContext.Set<LocalIdentityCredentialOperation>()
            .AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == request.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
        {
            return null;
        }
        if (operation.Kind != LocalCredentialOperationKind.Reset
            || operation.InitiatingApplicationUserId != request.InitiatingApplicationUserId
            || operation.LocalSubjectId != request.LocalSubjectId
            || operation.PreviousOperationId != request.ExpectedCurrentOperationId
            || operation.PreviousOperationConcurrencyStamp != request.ExpectedCurrentOperationConcurrencyStamp
            || !string.Equals(operation.ResetReason, request.Reason, StringComparison.Ordinal))
        {
            return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Conflict);
        }
        return LocalCredentialResetResult.Replayed(new LocalCredentialResetReceipt(
            operation: new LocalCredentialOperationReceipt(
                operationId: operation.Id, kind: operation.Kind, stage: operation.Stage,
                initiatingApplicationUserId: operation.InitiatingApplicationUserId,
                localSubjectId: operation.LocalSubjectId, personalActorId: operation.PersonalActorId,
                externalLoginId: operation.ExternalLoginId, createdAt: operation.CreatedAt),
            previousOperationId: operation.PreviousOperationId.Value,
            previousOperationConcurrencyStamp: operation.PreviousOperationConcurrencyStamp.Value,
            reason: operation.ResetReason!));
    }

    public Task<LocalCredentialReplacementOutcome> ReplaceAsync(
        LocalCredentialReplacementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationContextsIdle();
        if (!identityDbContext.Database.IsRelational() || !applicationDbContext.Database.IsRelational())
        {
            throw new InvalidOperationException("Credential replacement requires relational stores.");
        }
        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            () => ReplaceAttemptAsync(request, cancellationToken));
    }

    public async Task<LocalCredentialReplacementSubject?> ReadReplacementSubjectAsync(
        Guid localSubjectId,
        string expectedSecurityStamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationContextsIdle();
        LocalCredentialStateMetadata? state = await ReadAsync(
            localSubjectId: localSubjectId,
            expectedSecurityStamp: expectedSecurityStamp,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (state?.State != LocalCredentialState.ChangeRequired || state.ApplicationUserId != localSubjectId)
        {
            return null;
        }
        LocalIdentityCredentialOperation? operation = await identityDbContext
            .Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == state.OperationId, cancellationToken).ConfigureAwait(false);
        if (operation is null || operation.Kind is not (LocalCredentialOperationKind.Create or LocalCredentialOperationKind.Reset)
            || operation.Stage != LocalCredentialOperationStage.ChangeRequired
            || operation.LocalSubjectId != localSubjectId)
        {
            return null;
        }
        LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == localSubjectId, cancellationToken).ConfigureAwait(false);
        if (user is null || string.IsNullOrWhiteSpace(user.SecurityStamp)
            || !string.Equals(user.SecurityStamp, expectedSecurityStamp, StringComparison.Ordinal)
            || !await HasExactApplicationBindingAsync(operation, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        return new LocalCredentialReplacementSubject(
            localSubjectId: localSubjectId, operationId: operation.Id, securityStamp: user.SecurityStamp);
    }

    private async Task<LocalCredentialReplacementOutcome> ReplaceAttemptAsync(
        LocalCredentialReplacementRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await identityDbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            LocalCredentialReplacementAuthority authority = request.Authority;
            if (!IsReplacementAuthorityCurrent(authority))
            {
                return LocalCredentialReplacementOutcome.InvalidChallenge;
            }
            LocalIdentityCredentialOperation? operation = await identityDbContext
                .Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == authority.Subject.OperationId, cancellationToken)
                .ConfigureAwait(false);
            if (operation is null || operation.Kind is not (LocalCredentialOperationKind.Create or LocalCredentialOperationKind.Reset)
                || operation.Stage != LocalCredentialOperationStage.ChangeRequired
                || operation.LocalSubjectId != authority.Subject.LocalSubjectId)
            {
                return LocalCredentialReplacementOutcome.InvalidChallenge;
            }
            IdentityUserToken<Guid>? token = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == operation.LocalSubjectId
                    && candidate.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && candidate.Name == LocalCredentialStateMetadata.TokenName, cancellationToken).ConfigureAwait(false);
            LocalCredentialStateMetadata? state = DeserializeMetadata(token?.Value);
            if (state?.State != LocalCredentialState.ChangeRequired
                || state.OperationId != operation.Id || state.ApplicationUserId != operation.ApplicationUserId)
            {
                return LocalCredentialReplacementOutcome.InvalidChallenge;
            }
            LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == operation.LocalSubjectId, cancellationToken)
                .ConfigureAwait(false);
            if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || string.IsNullOrWhiteSpace(user.ConcurrencyStamp)
                || !string.Equals(user.SecurityStamp, authority.Subject.SecurityStamp, StringComparison.Ordinal)
                || !await HasExactApplicationBindingAsync(operation, cancellationToken).ConfigureAwait(false))
            {
                return LocalCredentialReplacementOutcome.InvalidChallenge;
            }
            if (request.NewPassword.Length is < LocalIdentityOptions.MinimumPasswordLength
                or > LocalIdentityOptions.MaximumPasswordLength)
            {
                return LocalCredentialReplacementOutcome.InvalidPassword;
            }
            if (userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.NewPassword)
                != PasswordVerificationResult.Failed)
            {
                return LocalCredentialReplacementOutcome.SamePassword;
            }
            foreach (IPasswordValidator<LocalIdentityUser> validator in userManager.PasswordValidators)
            {
                IdentityResult validation = await validator.ValidateAsync(userManager, user, request.NewPassword)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsReplacementAuthorityCurrent(authority))
                {
                    return LocalCredentialReplacementOutcome.InvalidChallenge;
                }
                if (!validation.Succeeded)
                {
                    return LocalCredentialReplacementOutcome.InvalidPassword;
                }
            }
            if (!IsReplacementAuthorityCurrent(authority))
            {
                return LocalCredentialReplacementOutcome.InvalidChallenge;
            }
            string passwordHash = userManager.PasswordHasher.HashPassword(user, request.NewPassword);
            DateTime updatedAt = timeProvider.GetUtcNow().UtcDateTime;
            Guid operationStamp = Guid.CreateVersion7();
            string userStamp = Guid.CreateVersion7().ToString("N");
            string securityStamp = Guid.CreateVersion7().ToString("N");
            string readyState = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                version: LocalCredentialStateMetadata.CurrentVersion, state: LocalCredentialState.Ready,
                operationId: operation.Id, applicationUserId: operation.ApplicationUserId));
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsReplacementAuthorityCurrent(authority))
            {
                return LocalCredentialReplacementOutcome.InvalidChallenge;
            }
            int operationRows = await identityDbContext.Set<LocalIdentityCredentialOperation>()
                .Where(candidate => candidate.Id == operation.Id
                    && (candidate.Kind == LocalCredentialOperationKind.Create || candidate.Kind == LocalCredentialOperationKind.Reset)
                    && candidate.Stage == LocalCredentialOperationStage.ChangeRequired
                    && candidate.ConcurrencyStamp == operation.ConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.Stage, LocalCredentialOperationStage.Replaced)
                    .SetProperty(candidate => candidate.ConcurrencyStamp, operationStamp)
                    .SetProperty(candidate => candidate.UpdatedAt, updatedAt), cancellationToken).ConfigureAwait(false);
            int tokenRows = await identityDbContext.Set<IdentityUserToken<Guid>>()
                .Where(candidate => candidate.UserId == operation.LocalSubjectId
                    && candidate.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && candidate.Name == LocalCredentialStateMetadata.TokenName && candidate.Value == token!.Value)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Value, readyState), cancellationToken)
                .ConfigureAwait(false);
            int userRows = await identityDbContext.Set<LocalIdentityUser>()
                .Where(candidate => candidate.Id == operation.LocalSubjectId
                    && candidate.ConcurrencyStamp == user.ConcurrencyStamp
                    && candidate.SecurityStamp == authority.Subject.SecurityStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                    .SetProperty(candidate => candidate.ConcurrencyStamp, userStamp)
                    .SetProperty(candidate => candidate.SecurityStamp, securityStamp)
                    .SetProperty(candidate => candidate.UpdatedAt, updatedAt), cancellationToken).ConfigureAwait(false);
            if (operationRows != 1 || tokenRows != 1 || userRows != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return LocalCredentialReplacementOutcome.Conflict;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return LocalCredentialReplacementOutcome.Replaced;
        }
        catch
        {
            await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private bool IsReplacementAuthorityCurrent(LocalCredentialReplacementAuthority authority)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        return authority.IssuedAtUtc <= now && now < authority.ExpiresAtUtc;
    }

    public async Task<LocalCredentialProvisioningSnapshot?> ReadProvisioningAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("An operation identifier is required.", nameof(operationId));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationContextsIdle();

        LocalIdentityCredentialOperation? operation = await identityDbContext
            .Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
            return null;

        LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == operation.LocalSubjectId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null || string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.FirstName))
            return null;

        return new LocalCredentialProvisioningSnapshot(
            receipt: new LocalCredentialOperationReceipt(
                operationId: operation.Id,
                kind: operation.Kind,
                stage: operation.Stage,
                initiatingApplicationUserId: operation.InitiatingApplicationUserId,
                localSubjectId: operation.LocalSubjectId,
                personalActorId: operation.PersonalActorId,
                externalLoginId: operation.ExternalLoginId,
                createdAt: operation.CreatedAt),
            operationConcurrencyStamp: operation.ConcurrencyStamp,
            email: user.Email,
            firstName: user.FirstName,
            lastName: user.LastName,
            emailVerified: user.EmailConfirmed,
            username: user.UserName);
    }

    public Task<LocalCredentialActivationOutcome> ActivateChangeRequiredAsync(
        LocalCredentialActivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationContextsIdle();
        if (!identityDbContext.Database.IsRelational() || !applicationDbContext.Database.IsRelational())
            throw new InvalidOperationException("Credential activation requires relational stores.");

        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            () => ActivateAttemptAsync(request, cancellationToken));
    }

    private async Task<LocalCredentialActivationOutcome> ActivateAttemptAsync(
        LocalCredentialActivationRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await identityDbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        try
        {
            LocalIdentityCredentialOperation? operation = await identityDbContext
                .Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == request.OperationId, cancellationToken)
                .ConfigureAwait(false);
            if (operation is null)
                return LocalCredentialActivationOutcome.NotFound;

            IdentityUserToken<Guid>? token = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.UserId == operation.LocalSubjectId
                    && candidate.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && candidate.Name == LocalCredentialStateMetadata.TokenName, cancellationToken)
                .ConfigureAwait(false);
            LocalCredentialStateMetadata? state = DeserializeMetadata(token?.Value);
            if (operation.Kind != LocalCredentialOperationKind.Create || state is null
                || state.OperationId != operation.Id || state.ApplicationUserId != operation.ApplicationUserId)
            {
                return LocalCredentialActivationOutcome.Conflict;
            }

            bool pending = operation.Stage == LocalCredentialOperationStage.ProvisioningPending
                && state.State == LocalCredentialState.ProvisioningPending;
            bool activated = operation.Stage == LocalCredentialOperationStage.ChangeRequired
                && state.State == LocalCredentialState.ChangeRequired;
            if (!pending && !activated)
                return LocalCredentialActivationOutcome.Conflict;
            if (pending && operation.ConcurrencyStamp != request.ExpectedOperationConcurrencyStamp)
                return LocalCredentialActivationOutcome.Conflict;

            bool bindingComplete = await HasExactApplicationBindingAsync(operation, cancellationToken).ConfigureAwait(false);
            if (!bindingComplete)
                return LocalCredentialActivationOutcome.BindingIncomplete;
            if (activated)
                return LocalCredentialActivationOutcome.AlreadyActivated;

            LocalIdentityUser? user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == operation.LocalSubjectId, cancellationToken)
                .ConfigureAwait(false);
            if (user is null || string.IsNullOrWhiteSpace(user.ConcurrencyStamp)
                || string.IsNullOrWhiteSpace(user.SecurityStamp))
            {
                return LocalCredentialActivationOutcome.Conflict;
            }

            DateTime updatedAt = timeProvider.GetUtcNow().UtcDateTime;
            Guid operationStamp = Guid.CreateVersion7();
            string userStamp = Guid.CreateVersion7().ToString("N");
            string securityStamp = Guid.CreateVersion7().ToString("N");
            string activatedState = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                version: LocalCredentialStateMetadata.CurrentVersion,
                state: LocalCredentialState.ChangeRequired,
                operationId: operation.Id,
                applicationUserId: operation.ApplicationUserId));

            int operationRows = await identityDbContext.Set<LocalIdentityCredentialOperation>()
                .Where(candidate => candidate.Id == operation.Id
                    && candidate.Kind == LocalCredentialOperationKind.Create
                    && candidate.Stage == LocalCredentialOperationStage.ProvisioningPending
                    && candidate.ConcurrencyStamp == request.ExpectedOperationConcurrencyStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.Stage, LocalCredentialOperationStage.ChangeRequired)
                    .SetProperty(candidate => candidate.ConcurrencyStamp, operationStamp)
                    .SetProperty(candidate => candidate.UpdatedAt, updatedAt), cancellationToken)
                .ConfigureAwait(false);
            int tokenRows = await identityDbContext.Set<IdentityUserToken<Guid>>()
                .Where(candidate => candidate.UserId == operation.LocalSubjectId
                    && candidate.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && candidate.Name == LocalCredentialStateMetadata.TokenName
                    && candidate.Value == token!.Value)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Value, activatedState), cancellationToken)
                .ConfigureAwait(false);
            int userRows = await identityDbContext.Set<LocalIdentityUser>()
                .Where(candidate => candidate.Id == operation.LocalSubjectId
                    && candidate.ConcurrencyStamp == user.ConcurrencyStamp
                    && candidate.SecurityStamp == user.SecurityStamp)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.ConcurrencyStamp, userStamp)
                    .SetProperty(candidate => candidate.SecurityStamp, securityStamp)
                    .SetProperty(candidate => candidate.UpdatedAt, updatedAt), cancellationToken)
                .ConfigureAwait(false);
            if (operationRows != 1 || tokenRows != 1 || userRows != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return LocalCredentialActivationOutcome.Conflict;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return LocalCredentialActivationOutcome.Activated;
        }
        catch
        {
            await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private Task<bool> HasExactApplicationBindingAsync(
        LocalIdentityCredentialOperation operation,
        CancellationToken cancellationToken)
    {
        string providerKey = operation.LocalSubjectId.ToString("D");
        return applicationDbContext.UserExternalLogins.AsNoTracking()
            .AnyAsync(login => login.Id == operation.ExternalLoginId
                && login.UserId == operation.ApplicationUserId
                && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                && login.ProviderKey == providerKey
                && applicationDbContext.Users.Any(user => user.Id == operation.ApplicationUserId && !user.IsDeleted)
                && applicationDbContext.Actors.Any(actor => actor.Id == operation.PersonalActorId
                    && actor.UserId == operation.ApplicationUserId
                    && actor.ActorTypeId == (int)ActorTypeEnum.User
                    && !actor.IsDeleted && !actor.IsSuspended), cancellationToken);
    }

    private void EnsureReconciliationContextsIdle()
    {
        if (identityDbContext.Database.CurrentTransaction is not null
            || applicationDbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException("Credential reconciliation cannot join a caller-owned transaction.");
        }

        if (identityDbContext.ChangeTracker.HasChanges() || applicationDbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Credential reconciliation requires contexts without pending changes.");
    }

    public async Task<bool> ValidateBootstrapCreationAsync(
        LocalCredentialCreateRequest request, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReconciliationContextsIdle();
        if (string.IsNullOrWhiteSpace(password)
            || password.Length is < LocalIdentityOptions.MinimumPasswordLength or > LocalIdentityOptions.MaximumPasswordLength)
            return false;
        LocalCredentialCreateResult? existing = await ResolveExistingAsync(request, null, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            return existing.Outcome == LocalCredentialCreateOutcome.Replayed;

        var user = new LocalIdentityUser
        {
            UserName = request.Username, Email = request.Email,
            FirstName = request.FirstName, LastName = request.LastName, EmailConfirmed = true,
            LockoutEnabled = true, CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        foreach (IUserValidator<LocalIdentityUser> validator in userManager.UserValidators)
        {
            IdentityResult validation = await validator.ValidateAsync(userManager, user).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!validation.Succeeded) return false;
        }
        foreach (IPasswordValidator<LocalIdentityUser> validator in userManager.PasswordValidators)
        {
            IdentityResult validation = await validator.ValidateAsync(userManager, user, password).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!validation.Succeeded) return false;
        }
        return true;
    }

    public Task<LocalCredentialCreateResult> CreateBootstrapPendingAsync(
        LocalCredentialCreateRequest request, Guid? localSubjectId, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(password)
            || password.Length is < LocalIdentityOptions.MinimumPasswordLength or > LocalIdentityOptions.MaximumPasswordLength)
            return Task.FromResult(LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Invalid));
        if (localSubjectId is { } subject && (subject == Guid.Empty || subject.Version != 7 || subject.Variant is < 8 or > 11))
            throw new ArgumentException("A UUIDv7 Local subject is required.", nameof(localSubjectId));
        return CreatePendingCoreAsync(request, localSubjectId, password, cancellationToken);
    }

    public Task<LocalCredentialCreateResult> CreatePendingAsync(
        LocalCredentialCreateRequest request, CancellationToken cancellationToken) =>
        CreatePendingCoreAsync(request, null, null, cancellationToken);

    private Task<LocalCredentialCreateResult> CreatePendingCoreAsync(
        LocalCredentialCreateRequest request, Guid? localSubjectId, string? password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!identityDbContext.Database.IsRelational())
            throw new InvalidOperationException("Credential creation requires a relational Identity store.");
        if (identityDbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Credential creation cannot join a caller-owned transaction.");
        if (identityDbContext.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Credential creation requires a context without pending changes.");

        return identityDbContext.Database.CreateExecutionStrategy().ExecuteAsync(
            () => EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(
                () => CreateAttemptAsync(request, localSubjectId, password, cancellationToken), cancellationToken));
    }

    private async Task<LocalCredentialCreateResult> CreateAttemptAsync(
        LocalCredentialCreateRequest request, Guid? localSubjectId, string? password,
        CancellationToken cancellationToken)
    {
        List<object> ownedEntities = [];
        DbUpdateException? uniqueFailure = null;
        try
        {
            await using var transaction = await identityDbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            try
            {
                LocalCredentialCreateResult? existing = await ResolveExistingAsync(request, localSubjectId, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                    return existing;

                DateTime createdAt = timeProvider.GetUtcNow().UtcDateTime;
                var user = new LocalIdentityUser
                {
                    Id = localSubjectId ?? Guid.CreateVersion7(),
                    UserName = request.Username,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    EmailConfirmed = true,
                    LockoutEnabled = true,
                    CreatedAt = createdAt
                };
                ownedEntities.Add(user);
                string temporaryPassword = password ?? $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
                IdentityResult creation = await userManager.CreateAsync(user, temporaryPassword).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!creation.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var receipt = new LocalCredentialOperationReceipt(
                        operationId: request.OperationId,
                        kind: LocalCredentialOperationKind.Create,
                        stage: LocalCredentialOperationStage.ProvisioningPending,
                        initiatingApplicationUserId: request.InitiatingApplicationUserId,
                        localSubjectId: user.Id,
                        personalActorId: Guid.CreateVersion7(),
                        externalLoginId: Guid.CreateVersion7(),
                        createdAt: createdAt);
                    var operation = new LocalIdentityCredentialOperation(
                        receipt: receipt,
                        concurrencyStamp: Guid.CreateVersion7());
                    var state = new IdentityUserToken<Guid>
                    {
                        UserId = user.Id,
                        LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
                        Name = LocalCredentialStateMetadata.TokenName,
                        Value = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                            version: LocalCredentialStateMetadata.CurrentVersion,
                            state: LocalCredentialState.ProvisioningPending,
                            operationId: receipt.OperationId,
                            applicationUserId: user.Id))
                    };
                    ownedEntities.Add(operation);
                    ownedEntities.Add(state);
                    identityDbContext.AddRange(operation, state);
                    await identityDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return LocalCredentialCreateResult.Created(receipt: receipt, temporaryPassword: temporaryPassword);
                }
            }
            catch
            {
                await RollbackBestEffortAsync(transaction).ConfigureAwait(false);
                throw;
            }
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            uniqueFailure = exception;
        }
        finally
        {
            foreach (object entity in ownedEntities)
                identityDbContext.Entry(entity).State = EntityState.Detached;
        }

        LocalCredentialCreateResult? converged = await ResolveExistingAsync(request, localSubjectId, cancellationToken)
            .ConfigureAwait(false);
        if (converged is not null)
            return converged;
        if (uniqueFailure is not null)
            ExceptionDispatchInfo.Capture(uniqueFailure).Throw();
        return LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Invalid);
    }

    private async Task<LocalCredentialCreateResult?> ResolveExistingAsync(
        LocalCredentialCreateRequest request, Guid? localSubjectId,
        CancellationToken cancellationToken)
    {
        LocalIdentityCredentialOperation? operation = await identityDbContext.Set<LocalIdentityCredentialOperation>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == request.OperationId, cancellationToken)
            .ConfigureAwait(false);
        string? normalizedName = userManager.NormalizeName(request.Username);
        string? normalizedEmail = userManager.NormalizeEmail(request.Email);
        if (operation is null)
        {
            bool existingName = await identityDbContext.Set<LocalIdentityUser>()
                .AsNoTracking()
                .AnyAsync(user => user.NormalizedUserName == normalizedName || user.Id == localSubjectId
                    || (normalizedEmail != null && user.NormalizedEmail == normalizedEmail), cancellationToken)
                .ConfigureAwait(false);
            return existingName ? LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Conflict) : null;
        }

        LocalIdentityUser? existingUser = await identityDbContext.Set<LocalIdentityUser>()
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == operation.LocalSubjectId, cancellationToken)
            .ConfigureAwait(false);
        if (operation.Kind != LocalCredentialOperationKind.Create
            || (localSubjectId.HasValue && operation.LocalSubjectId != localSubjectId.Value)
            || operation.InitiatingApplicationUserId != request.InitiatingApplicationUserId
            || existingUser is null
            || !string.Equals(existingUser.NormalizedUserName, normalizedName, StringComparison.Ordinal)
            || !string.Equals(existingUser.NormalizedEmail, userManager.NormalizeEmail(request.Email), StringComparison.Ordinal)
            || !string.Equals(existingUser.FirstName, request.FirstName, StringComparison.Ordinal)
            || !string.Equals(existingUser.LastName, request.LastName, StringComparison.Ordinal))
        {
            return LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Conflict);
        }

        return LocalCredentialCreateResult.Replayed(new LocalCredentialOperationReceipt(
            operationId: operation.Id,
            kind: operation.Kind,
            stage: operation.Stage,
            initiatingApplicationUserId: operation.InitiatingApplicationUserId,
            localSubjectId: operation.LocalSubjectId,
            personalActorId: operation.PersonalActorId,
            externalLoginId: operation.ExternalLoginId,
            createdAt: operation.CreatedAt));
    }

    private static async Task RollbackBestEffortAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Preserve the original failure when a commit completed or the transaction was disposed.
        }
        catch (DbException)
        {
            // Preserve the original failure if the provider cannot acknowledge rollback.
        }
    }

    public async Task<LocalIdentityUser?> ReadReadySessionAsync(
        LocalSessionAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        var current = await (
            from token in identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
            join user in identityDbContext.Set<LocalIdentityUser>().AsNoTracking() on token.UserId equals user.Id
            where token.UserId == authority.LocalSubjectId
                && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && token.Name == LocalCredentialStateMetadata.TokenName
            select new { token.Value, User = user })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (current is null || string.IsNullOrWhiteSpace(current.User.PasswordHash)
            || !string.Equals(current.User.SecurityStamp, authority.SecurityStamp, StringComparison.Ordinal)
            || current.User.EmailConfirmed != authority.EmailVerified)
        {
            return null;
        }
        LocalCredentialStateMetadata? state = DeserializeMetadata(current.Value);
        if (state?.State != LocalCredentialState.Ready || state.ApplicationUserId != authority.LocalSubjectId)
        {
            return null;
        }
        LocalIdentityCredentialOperation? operation = await identityDbContext.Set<LocalIdentityCredentialOperation>()
            .AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == state.OperationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null || operation.LocalSubjectId != authority.LocalSubjectId
            || operation.Kind is not (LocalCredentialOperationKind.Create or LocalCredentialOperationKind.Reset)
            || operation.Stage != LocalCredentialOperationStage.Replaced
            || !await HasExactApplicationBindingAsync(operation, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return current.User;
    }

    public async Task<LocalCredentialStateMetadata?> ReadAsync(
        Guid localSubjectId,
        string expectedSecurityStamp,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(expectedSecurityStamp))
        {
            return null;
        }
        var authority = await (
            from token in identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
            join user in identityDbContext.Set<LocalIdentityUser>().AsNoTracking() on token.UserId equals user.Id
            where token.UserId == localSubjectId
                && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && token.Name == LocalCredentialStateMetadata.TokenName
            select new { token.Value, user.SecurityStamp })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return authority is not null
            && string.Equals(authority.SecurityStamp, expectedSecurityStamp, StringComparison.Ordinal)
                ? DeserializeMetadata(authority.Value)
                : null;
    }

    private static LocalCredentialStateMetadata? DeserializeMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LocalCredentialStateMetadata>(value, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
