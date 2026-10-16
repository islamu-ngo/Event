using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Reproduces the current resolved-account and Local ready-session predicates from held
/// facts. It is credential continuity, not an administrator or selected-provider grant.
/// </summary>
public static class HumanPrincipalSourceValidation
{
    public static void RequireCurrent(HumanCommitSubject subject, LocalSessionAuthority? session,
        HumanPrincipalSourceFacts facts)
    {
        if (subject.AccountKey is not { } account || facts.PrincipalId != subject.PrincipalId
            || facts.Users is not [var user] || user.Id != subject.PrincipalId || user.IsDeleted
            || facts.Bindings is not [var binding] || binding.UserId != subject.PrincipalId
            || binding.AuthenticationProviderId != (int)account.ProviderKind)
            throw new UnauthorizedAccessException("The original human account binding is no longer current.");
        // Bindings were filtered by the original provider/account in the native database.
        // Preserve its comparison semantics instead of replacing them with ordinal matching.
        if (account.ProviderKind != AuthenticationProviderKind.Local)
        {
            if (session is not null || facts.LocalSession is not null)
                throw new UnauthorizedAccessException("The original human authentication scheme has changed.");
            return;
        }
        if (session is null || session.LocalSubjectId != subject.PrincipalId
            || facts.LocalSession is not { } local || local.Accounts is not [var localAccount]
            || localAccount.Id != subject.PrincipalId || !localAccount.HasPassword || !localAccount.StampMatches
            || localAccount.EmailConfirmed != session.EmailVerified
            || !localAccount.EmailConfirmed && local.EmailDeliveryEnabled is true
            || local.CredentialState is not { State: LocalCredentialState.Ready } state
            || state.ApplicationUserId != subject.PrincipalId)
            throw new UnauthorizedAccessException("The original Local credential is no longer current.");
        var operation = local.Operations.SingleOrDefault(candidate => candidate.Id == state.OperationId);
        if (operation is null || operation.LocalSubjectId != subject.PrincipalId
            || operation.Kind is not ((int)LocalCredentialOperationKind.Create or (int)LocalCredentialOperationKind.Reset)
            || operation.Stage != (int)LocalCredentialOperationStage.Replaced
            || operation.ExternalLoginId != binding.Id
            || !local.PersonalActors.Any(actor => actor.Id == operation.PersonalActorId
                && actor.UserId == subject.PrincipalId && actor.ActorTypeId == (int)ActorTypeEnum.User
                && !actor.IsDeleted && !actor.IsSuspended))
            throw new UnauthorizedAccessException("The original Local credential binding is no longer current.");
    }
}
