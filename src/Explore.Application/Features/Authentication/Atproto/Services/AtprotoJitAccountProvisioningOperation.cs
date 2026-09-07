using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Authentication.Atproto.Services;

public sealed record AtprotoJitAccountIds(
    Guid UserId,
    Guid ActorId,
    Guid ExternalLoginId);

public sealed record AtprotoPlatformAccount(
    User User,
    Actor PersonalActor,
    UserExternalLogin ExternalLogin);

public sealed class AtprotoJitAccountProvisioningOperation(
    IUserRepository users,
    IActorRepository actors,
    IUserExternalLoginRepository externalLogins)
{
    public async Task<AtprotoPlatformAccount?> EnsureAsync(
        ProviderAccountKey accountKey,
        AtprotoJitAccountIds ids,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        if (accountKey.ProviderKind != AuthenticationProviderKind.Atproto)
        {
            throw new ArgumentException(
                "JIT account provisioning requires AT Protocol authority.",
                nameof(accountKey));
        }

        UserExternalLogin? existingLogin =
            await externalLogins.GetByProviderAndKey(accountKey)
                .ConfigureAwait(false);
        if (existingLogin is not null)
        {
            User? existingUser =
                await users.GetById(existingLogin.UserId).ConfigureAwait(false);
            Actor? existingActor = await actors
                .GetTrackedActorByUserId(
                    existingLogin.UserId,
                    cancellationToken)
                .ConfigureAwait(false);
            return IsComplete(existingLogin, existingUser, existingActor, accountKey)
                ? new AtprotoPlatformAccount(
                    existingUser!,
                    existingActor!,
                    existingLogin)
                : null;
        }

        var user = new User
        {
            Id = ids.UserId,
            Pii = new UserPii
            {
                Email = string.Empty,
                FirstName = string.Empty,
                LastName = string.Empty,
            },
            EmailVerified = false,
            CreatedAt = createdAt,
        };
        user = await users.Create(user).ConfigureAwait(false);

        var actor = new Actor
        {
            Id = ids.ActorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id,
            User = user,
            Pii = new ActorPii
            {
                DisplayName = "AT Protocol user",
            },
            CreatedAt = createdAt,
            CreatedBy = user.Id,
        };
        actor = await actors.Create(actor).ConfigureAwait(false);

        var login = new UserExternalLogin
        {
            Id = ids.ExternalLoginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId =
                (int)AuthenticationProviderKind.Atproto,
            AuthenticationProvider = null!,
            ProviderKey = accountKey.Value,
            ProviderDisplayName = "AT Protocol",
            CreatedAt = createdAt,
            CreatedBy = user.Id,
        };
        login = await externalLogins.Create(login).ConfigureAwait(false);

        return new AtprotoPlatformAccount(user, actor, login);
    }

    private static bool IsComplete(
        UserExternalLogin login,
        User? user,
        Actor? actor,
        ProviderAccountKey accountKey) =>
        user is { IsDeleted: false }
        && actor is { IsDeleted: false, IsSuspended: false }
        && actor.UserId == user.Id
        && login.UserId == user.Id
        && login.AuthenticationProviderId
            == (int)AuthenticationProviderKind.Atproto
        && string.Equals(
            login.ProviderKey,
            accountKey.Value,
            StringComparison.Ordinal);
}
