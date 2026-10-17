using Explore.Application.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private sealed class SelfResetPostimage(ILocalAdministrationInvocation invocation,
        IDbContextTransaction transaction, LocalCredentialResetReceipt receipt,
        PropertyValues user, PropertyValues previous, PropertyValues operation, PropertyValues token)
    {
        internal ILocalAdministrationInvocation Invocation { get; } = invocation;
        internal IDbContextTransaction Transaction { get; } = transaction;
        internal LocalCredentialResetReceipt Receipt { get; } = receipt;
        internal PropertyValues User { get; } = user;
        internal PropertyValues Previous { get; } = previous;
        internal PropertyValues Operation { get; } = operation;
        internal PropertyValues Token { get; } = token;
        internal bool Committed { get; set; }
    }

    private void CaptureSelfReset(HumanAdministrationAttempt human, LocalIdentityUser user,
        LocalIdentityCredentialOperation previous, IdentityUserToken<Guid> token,
        LocalIdentityCredentialOperation operation, LocalCredentialResetReceipt receipt,
        string passwordHash, string userStamp, string securityStamp, Guid previousStamp,
        string metadata, DateTime changedAt)
    {
        if (!ReferenceEquals(_administrationAttempt, human) || human.Stage != LocalAdministrationStage.Reset
            || receipt.Operation.LocalSubjectId != human.Invocation.PrincipalId
            || receipt.Operation.InitiatingApplicationUserId != human.Invocation.PrincipalId || _selfReset is not null)
            throw new UnauthorizedAccessException("Only the original Local reset writer can define its postimage.");
        var expectedUser = identityDbContext.Entry(user).CurrentValues.Clone();
        expectedUser[nameof(LocalIdentityUser.PasswordHash)] = passwordHash;
        expectedUser[nameof(LocalIdentityUser.SecurityStamp)] = securityStamp;
        expectedUser[nameof(LocalIdentityUser.ConcurrencyStamp)] = userStamp;
        expectedUser[nameof(LocalIdentityUser.UpdatedAt)] = changedAt;
        var expectedPrevious = identityDbContext.Entry(previous).CurrentValues.Clone();
        expectedPrevious[nameof(LocalIdentityCredentialOperation.Stage)] = LocalCredentialOperationStage.Superseded;
        expectedPrevious[nameof(LocalIdentityCredentialOperation.ConcurrencyStamp)] = previousStamp;
        expectedPrevious[nameof(LocalIdentityCredentialOperation.UpdatedAt)] = changedAt;
        var expectedToken = identityDbContext.Entry(token).CurrentValues.Clone();
        expectedToken[nameof(IdentityUserToken<Guid>.Value)] = metadata;
        _selfReset = new(human.Invocation, human.Transaction, receipt, expectedUser, expectedPrevious,
            identityDbContext.Entry(operation).CurrentValues.Clone(), expectedToken);
    }

    private async Task<LocalCredentialResetReceipt?> ValidateSelfResetAsync(
        ILocalAdministrationInvocation invocation, CancellationToken cancellationToken)
    {
        if (_selfReset is not { } expected) return null;
        if (!ReferenceEquals(expected.Invocation, invocation) || _administrationAttempt is not { } attempt
            || !ReferenceEquals(attempt.Invocation, invocation)
            || !ReferenceEquals(identityDbContext.Database.CurrentTransaction, attempt.Transaction)
            || !applicationDbContext.HumanCommitAttemptOwnsTransaction
            || (!expected.Committed && (attempt.Stage != LocalAdministrationStage.Reset
                    || !ReferenceEquals(attempt.Transaction, expected.Transaction)))
            || expected.Committed && attempt.Stage != LocalAdministrationStage.Disclose)
            throw new UnauthorizedAccessException("Self-reset custody cannot leave its original physical owners.");
        Guid subject = expected.Receipt.Operation.LocalSubjectId;
        var user = await identityDbContext.Set<LocalIdentityUser>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == subject, cancellationToken);
        var previous = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expected.Receipt.PreviousOperationId, cancellationToken);
        var operation = await identityDbContext.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expected.Receipt.Operation.OperationId, cancellationToken);
        var token = await identityDbContext.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == subject
                && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && row.Name == LocalCredentialStateMetadata.TokenName, cancellationToken);
        Require(expected.User, user);
        Require(expected.Previous, previous);
        Require(expected.Operation, operation);
        Require(expected.Token, token);
        return expected.Receipt;

        void Require(PropertyValues image, object? entity)
        {
            if (entity is null)
                throw new UnauthorizedAccessException("An original self-reset row is missing.");
            var current = identityDbContext.Entry(entity).CurrentValues;
            if (image.Properties.Any(property => !Equals(image[property], current[property])))
                throw new UnauthorizedAccessException("The persisted self-reset differs from its exact writer postimage.");
        }
    }
}
