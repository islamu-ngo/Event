// ABOUTME: Synchronizes provider profiles while requiring pre-established canonical Local account bindings.
// ABOUTME: Rechecks ownership inside the write transaction and prevents implicit Local/external account adoption.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Application.DTOs.User;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;


namespace Explore.Application.Features.Users.Handlers.Commands;

public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, BaseCommandResponse<Guid>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IActorRepository _actorRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IInstanceBootstrapStateRepository _bootstrapRepository;
    private readonly InstanceOnboardingCompletionOperation _onboardingCompletion;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncUserCommandHandler> _logger;

    public SyncUserCommandHandler(
        IUserRepository userRepository,
        IUserExternalLoginRepository userExternalLoginRepository,
        IActorRepository actorRepository,
        ITenantRepository tenantRepository,
        IInstanceBootstrapStateRepository bootstrapRepository,
        InstanceOnboardingCompletionOperation onboardingCompletion,
        HybridCache cache,
        IUnitOfWork unitOfWork,
        ILogger<SyncUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _userExternalLoginRepository = userExternalLoginRepository;
        _actorRepository = actorRepository;
        _tenantRepository = tenantRepository;
        _bootstrapRepository = bootstrapRepository;
        _onboardingCompletion = onboardingCompletion;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(SyncUserCommand request, CancellationToken cancellationToken)
    {
        var userDto = request.UserDto;

        try
        {
            ProviderAccountKey accountKey = request.AccountKey;
            AuthenticationProviderKind providerKind = accountKey.ProviderKind;
            if ((request.LocalLifecycleSynchronization is not null && providerKind != AuthenticationProviderKind.Local)
                || (!string.IsNullOrWhiteSpace(userDto.AuthProvider)
                    && userDto.AuthProvider.ParseAuthenticationProviderKind() != providerKind))
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Provider account authority is invalid."],
                    "Provider account authority is invalid.");
            }

            var supportsEmailAutoMatch = SupportsEmailAutoMatch(providerKind);
            var email = NormalizeEmail(userDto.Email);

            var existingLogin = await _userExternalLoginRepository.GetByProviderAndKey(accountKey);
            if (providerKind == AuthenticationProviderKind.Local)
            {
                if (!await HasValidLocalBindingAsync(accountKey, userDto.Id, existingLogin, cancellationToken,
                        request.LocalLifecycleSynchronization))
                    return ExplicitBindingRequired();
            }
            else if (existingLogin is null && userDto.Id != Guid.Empty
                && await IsLocalOwnedAsync(userDto.Id, cancellationToken))
            {
                return ExplicitBindingRequired();
            }

            InstanceBootstrapState? bootstrap = await _bootstrapRepository.GetCurrent(cancellationToken);
            if (request.LocalLifecycleSynchronization is null && bootstrap is
                {
                    Mode: InstanceBootstrapMode.ConfiguredAdministrator
                })
            {
                if (bootstrap.ProviderKind != providerKind)
                {
                    return BaseCommandResponse.Failure<Guid>(
                        "configured_administrator_claim_mismatch",
                        "Configured administrator claim did not match.");
                }

                Guid claimUserId = existingLogin?.UserId
                    ?? Guid.CreateVersion7();
                BaseCommandResponse<Guid> claim = await _onboardingCompletion.ClaimConfiguredAsync(
                    new ClaimConfiguredInstanceAdministratorCommand
                    {
                        AuthenticatedAccount = accountKey,
                        UserId = claimUserId,
                        Email = email,
                        FirstName = userDto.FirstName,
                        LastName = userDto.LastName,
                        EmailVerified = userDto.EmailVerified
                    },
                    cancellationToken);
                if (bootstrap.Status == InstanceBootstrapStatus.Pending)
                {
                    return claim;
                }

                if (!claim.IsSuccess
                    && (existingLogin is null
                        || bootstrap.CompletedByUserId != existingLogin.UserId))
                {
                    return claim;
                }
            }

            if (!supportsEmailAutoMatch && string.IsNullOrWhiteSpace(email))
            {
                if (existingLogin == null)
                {
                    const string message =
                        "AT Protocol identity must be explicitly linked to an existing account before sign-in sync without email.";
                    return BaseCommandResponse.Validation<Guid>([message], message);
                }
            }

            // Pre-reads for user resolution — outside transaction for fast rejection
            User? user = null;

            if (existingLogin != null)
            {
                user = await _userRepository.GetById(existingLogin.UserId);
            }

            if (user == null && userDto.Id != Guid.Empty)
            {
                user = await _userRepository.GetById(userDto.Id);
            }

            if (user == null
                && supportsEmailAutoMatch
                && userDto.EmailVerified == true
                && !string.IsNullOrWhiteSpace(email))
            {
                IReadOnlyList<User> emailMatches =
                    await _userRepository.GetUsersByNormalizedEmailAsync(email, cancellationToken);
                if (emailMatches.Count > 1)
                {
                    const string message =
                        "Verified email resolves to multiple user accounts; explicit linking is required.";
                    return BaseCommandResponse.Validation<Guid>([message], message);
                }

                user = emailMatches.SingleOrDefault();
                if (user is not null && await IsLocalOwnedAsync(user.Id, cancellationToken))
                    user = null;
            }


            // Fast-rejection for missing email on new account creation — before any writes
            string? safeEmail = null;
            if (user == null)
            {
                safeEmail = ResolveEmailForCreation(providerKind, email);
                if (string.IsNullOrWhiteSpace(safeEmail))
                {
                    return BaseCommandResponse.Validation<Guid>(
                        ["Email is required to create a new account for this provider."],
                        "Email is required to create a new account for this provider.");
                }
            }

            // IDs generated before lambda — captured via closure for retry safety
            var newUserId = userDto.Id != Guid.Empty ? userDto.Id : Guid.CreateVersion7();
            var loginId = Guid.CreateVersion7();

            async Task<BaseCommandResponse<Guid>> SynchronizeAsync(CancellationToken ct)
            {
                UserExternalLogin? currentLogin = await _userExternalLoginRepository.GetByProviderAndKey(accountKey);
                ct.ThrowIfCancellationRequested();
                if (providerKind == AuthenticationProviderKind.Local)
                {
                    if (!await HasValidLocalBindingAsync(accountKey, userDto.Id, currentLogin, ct,
                            request.LocalLifecycleSynchronization)
                        || user is null || currentLogin!.UserId != user.Id)
                    {
                        return ExplicitBindingRequired();
                    }
                }
                else if (currentLogin is not null && (user is null || currentLogin.UserId != user.Id))
                {
                    return ExplicitBindingRequired();
                }
                else if (user is not null && currentLogin is null && await IsLocalOwnedAsync(user.Id, ct))
                {
                    return ExplicitBindingRequired();
                }

                if (user == null)
                {
                    var newUser = new User
                    {
                        Id = newUserId,
                        Pii = new UserPii
                        {
                            Email = safeEmail!,
                            FirstName = ResolveFirstName(userDto.FirstName),
                            LastName = ResolveLastName(userDto.LastName)
                        },
                        EmailVerified = userDto.EmailVerified ?? supportsEmailAutoMatch
                    };

                    var createdUser = await _userRepository.Create(newUser);

                    var actor = new Actor
                    {
                        ActorTypeId = (int)ActorTypeEnum.User,
                        ActorType = null!,
                        Pii = new ActorPii
                        {
                            DisplayName = BuildDisplayName(userDto.FirstName, userDto.LastName)
                        },
                        Description = null,
                        UserId = createdUser.Id
                    };

                    await _actorRepository.Create(actor);

                    await EnsureExternalLoginLinkInTransactionAsync(createdUser, accountKey, loginId, ct);
                    return BaseCommandResponse.Success(id: createdUser.Id, message: "User synchronized successfully.");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        user.Email = email;
                    }

                    user.FirstName = ResolveFirstName(userDto.FirstName);
                    user.LastName = ResolveLastName(userDto.LastName);
                    if (userDto.EmailVerified.HasValue)
                    {
                        user.EmailVerified = userDto.EmailVerified;
                    }

                    var actor = await _actorRepository.GetActorByUserId(user.Id);
                    if (actor != null)
                    {
                        actor.DisplayName = BuildDisplayName(userDto.FirstName, userDto.LastName);
                        await _actorRepository.Update(actor);
                    }

                    await _userRepository.Update(user);

                    await EnsureExternalLoginLinkInTransactionAsync(user, accountKey, loginId, ct);
                    return BaseCommandResponse.Success(id: user.Id, message: "User synchronized successfully.");
                }
            }

            // Only the internal lifecycle receipt opts into the native store's existing transaction.
            // Both paths execute the same binding recheck and mirror writer; no second persistence boundary.
            var synchronization = request.LocalLifecycleSynchronization is null
                ? await _unitOfWork.ExecuteSerializableAsync(SynchronizeAsync, cancellationToken)
                : await SynchronizeAsync(cancellationToken);

            if (synchronization.IsSuccess && request.LocalLifecycleSynchronization is null)
                await _cache.RemoveAsync($"user:detail:{synchronization.Id}", cancellationToken);
            return synchronization;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "User synchronization failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            const string message = "User synchronization failed.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }
    }

    private async Task<bool> HasValidLocalBindingAsync(
        ProviderAccountKey accountKey,
        Guid requestedUserId,
        UserExternalLogin? login,
        CancellationToken cancellationToken,
        LocalIdentityLifecycleSynchronization? lifecycle = null)
    {
        if (!Guid.TryParseExact(accountKey.Value, "D", out Guid subjectId) || subjectId == Guid.Empty
            || !string.Equals(accountKey.Value, subjectId.ToString("D"), StringComparison.Ordinal)
            || (requestedUserId != Guid.Empty && requestedUserId != subjectId)
            || login is null || login.UserId != subjectId
            || login.AuthenticationProviderId != (int)AuthenticationProviderKind.Local
            || !string.Equals(login.ProviderKey, accountKey.Value, StringComparison.Ordinal))
        {
            return false;
        }

        User? user = await _userRepository.GetUserWithDetails(subjectId, cancellationToken);
        return user is { IsDeleted: false, Actor: { IsDeleted: false, IsSuspended: false } }
            && user.Actor.UserId == subjectId
            && user.Actor.ActorTypeId == (int)ActorTypeEnum.User
            && (lifecycle is null || (lifecycle.ApplicationUserId == subjectId
                && requestedUserId == subjectId
                && lifecycle.Operation.ExternalLoginId == login.Id
                && lifecycle.Operation.PersonalActorId == user.Actor.Id));
    }

    private async Task<bool> IsLocalOwnedAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<UserExternalLogin> bindings = await _userExternalLoginRepository.GetByUser(userId);
        cancellationToken.ThrowIfCancellationRequested();
        return bindings.Any(binding => binding.AuthenticationProviderId == (int)AuthenticationProviderKind.Local);
    }

    private static BaseCommandResponse<Guid> ExplicitBindingRequired()
    {
        const string message = "Identity ownership requires an explicit account binding.";
        return BaseCommandResponse.Validation<Guid>(errors: [message], message: message);
    }

    private async Task EnsureExternalLoginLinkInTransactionAsync(
        User user,
        ProviderAccountKey accountKey,
        Guid loginId,
        CancellationToken ct)
    {
        var existingByProviderAndKey = await _userExternalLoginRepository.GetByProviderAndKey(accountKey);
        if (existingByProviderAndKey != null)
        {
            if (existingByProviderAndKey.UserId != user.Id)
                throw new InvalidOperationException("This provider identity is already linked to another account.");

            return;
        }

        var login = new UserExternalLogin
        {
            Id = loginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)accountKey.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = accountKey.Value,
            ProviderDisplayName = GetProviderDisplayName(accountKey.ProviderKind)
        };

        await _userExternalLoginRepository.Create(login);
    }

    private static bool SupportsEmailAutoMatch(AuthenticationProviderKind provider)
    {
        return provider is AuthenticationProviderKind.Keycloak
            or AuthenticationProviderKind.Google;
    }

    private static string NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : email.Trim().ToLowerInvariant();
    }

    private static string ResolveEmailForCreation(AuthenticationProviderKind provider, string email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        return provider == AuthenticationProviderKind.Atproto ? string.Empty : email;
    }

    private static string ResolveFirstName(string? firstName)
    {
        return string.IsNullOrWhiteSpace(firstName) ? "User" : firstName.Trim();
    }

    private static string ResolveLastName(string? lastName)
    {
        return string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName.Trim();
    }

    private static string BuildDisplayName(string? firstName, string? lastName)
    {
        return $"{ResolveFirstName(firstName)} {ResolveLastName(lastName)}".Trim();
    }

    private static string GetProviderDisplayName(AuthenticationProviderKind provider)
    {
        return provider switch
        {
            AuthenticationProviderKind.Keycloak => "Keycloak",
            AuthenticationProviderKind.Google => "Google",
            AuthenticationProviderKind.Atproto => "AT Protocol",
            AuthenticationProviderKind.Local => "Local Identity",
            AuthenticationProviderKind.Development => "Development",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

}
