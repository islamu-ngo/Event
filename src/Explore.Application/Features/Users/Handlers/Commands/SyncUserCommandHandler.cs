using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Authentication;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Identity;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;


namespace Explore.Application.Features.Users.Handlers.Commands;

public class SyncUserCommandHandler : ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>
{
    private readonly IUserRepository _userRepository;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IIdentityAccountResolver _identityAccountResolver;
    private readonly IUserIdentityEmailRepository _identityEmails;
    private readonly IdentityEmailSynchronizationOperation _identityEmailSynchronization;
    private readonly IActorRepository _actorRepository;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly ITenantLifecycleAccessService _lifecycle;
    private readonly IInstanceBootstrapStateRepository _bootstrapRepository;
    private readonly InstanceOnboardingCompletionOperation _onboardingCompletion;
    private readonly HybridCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SyncUserCommandHandler> _logger;
    private readonly PrivacyIdentityFenceOperation _identityFence;

    public SyncUserCommandHandler(
        IUserRepository userRepository,
        IUserExternalLoginRepository userExternalLoginRepository,
        IIdentityAccountResolver identityAccountResolver,
        IUserIdentityEmailRepository identityEmails,
        IdentityEmailSynchronizationOperation identityEmailSynchronization,
        IActorRepository actorRepository,
        ITenantContextAccessor tenantContext,
        ITenantLifecycleAccessService lifecycle,
        IInstanceBootstrapStateRepository bootstrapRepository,
        InstanceOnboardingCompletionOperation onboardingCompletion,
        HybridCache cache,
        IUnitOfWork unitOfWork,
        ILogger<SyncUserCommandHandler> logger,
        PrivacyIdentityFenceOperation identityFence)
    {
        _userRepository = userRepository;
        _userExternalLoginRepository = userExternalLoginRepository;
        _identityAccountResolver = identityAccountResolver;
        _identityEmails = identityEmails;
        _identityEmailSynchronization = identityEmailSynchronization;
        _actorRepository = actorRepository;
        _tenantContext = tenantContext;
        _lifecycle = lifecycle;
        _bootstrapRepository = bootstrapRepository;
        _onboardingCompletion = onboardingCompletion;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _identityFence = identityFence;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(SyncUserCommand request, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _identityFence.ExecuteEnrollmentAsync(
                request.AccountKey, token => ExecuteUnderFenceAsync(request, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return BaseCommandResponse.Failure<Guid>(
                "identity_enrollment_unavailable", "Identity enrollment is unavailable.");
        }
    }

    private async Task<BaseCommandResponse<Guid>> ExecuteUnderFenceAsync(SyncUserCommand request, CancellationToken cancellationToken)
    {
        var userDto = request.UserDto;

        try
        {
            ProviderAccountKey accountKey = request.AccountKey;
            AuthenticationProviderKind providerKind = accountKey.ProviderKind;
            if ((request.LocalLifecycleSynchronization is not null && providerKind != AuthenticationProviderKind.Local)
                || (request.AuthorityEvidence is not null && request.AuthorityEvidence.AccountKey != accountKey)
                || (!string.IsNullOrWhiteSpace(userDto.AuthProvider)
                    && userDto.AuthProvider.ParseAuthenticationProviderKind() != providerKind))
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Provider account authority is invalid."],
                    "Provider account authority is invalid.");
            }

            var email = NormalizeEmail(request.AuthorityEvidence?.Email ?? userDto.Email);
            bool? emailVerified = request.AuthorityEvidence?.EmailVerified ?? userDto.EmailVerified;

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
            // Local synchronization has already proved a current explicit binding above; its login
            // and lifecycle callbacks remain available privately. External signup has no such authority.
            if (providerKind != AuthenticationProviderKind.Local
                && !await _lifecycle.IsPublicAsync(_tenantContext.TenantId, cancellationToken)
                && (bootstrap is not
                {
                    Mode: InstanceBootstrapMode.ConfiguredAdministrator,
                    Status: InstanceBootstrapStatus.Pending or InstanceBootstrapStatus.Completed
                }
                || !await _lifecycle.CanAttemptConfiguredAdministratorSyncAsync(_tenantContext.TenantId, cancellationToken)))
            {
                return BaseCommandResponse.Failure<Guid>(
                    "tenant_lifecycle_unavailable", "Tenant is not available for user synchronization.");
            }

            bool completedOrdinaryLocalBinding = providerKind == AuthenticationProviderKind.Local
                && bootstrap is
                {
                    Mode: InstanceBootstrapMode.ConfiguredAdministrator,
                    Status: InstanceBootstrapStatus.Completed,
                    CompletedByUserId: Guid administratorId
                }
                && existingLogin is not null
                && existingLogin.UserId != administratorId;

            if (request.LocalLifecycleSynchronization is null && !completedOrdinaryLocalBinding && bootstrap is
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
                        AuthorityEvidence = request.AuthorityEvidence,
                        UserId = claimUserId,
                        Email = email,
                        FirstName = userDto.FirstName,
                        LastName = userDto.LastName,
                        EmailVerified = emailVerified
                    },
                    cancellationToken);
                if (bootstrap.Status == InstanceBootstrapStatus.Pending)
                {
                    // The onboarding operation returns its receipt ID; SyncUser returns the account ID.
                    return claim.IsSuccess ? BaseCommandResponse.Success(claimUserId, claim.Message) : claim;
                }

                if (!claim.IsSuccess
                    && (existingLogin is null
                        || bootstrap.CompletedByUserId != existingLogin.UserId))
                {
                    return claim;
                }
            }

            if (providerKind != AuthenticationProviderKind.Local && request.AuthorityEvidence is null)
                return ExplicitBindingRequired();

            if (providerKind == AuthenticationProviderKind.Atproto && string.IsNullOrWhiteSpace(email))
            {
                if (existingLogin == null)
                {
                    const string message =
                        "AT Protocol identity must be explicitly linked to an existing account before sign-in sync without email.";
                    return BaseCommandResponse.Validation<Guid>([message], message);
                }
            }

            IdentityAccountResolution resolution = await _identityAccountResolver.ResolveAsync(
                accountKey, request.AuthorityEvidence, cancellationToken);
            if (resolution.Decision == IdentityCorrelationDecision.RecoveryRequired)
                return ExplicitBindingRequired();
            User? user = resolution.User;
            if (user is not null && providerKind != AuthenticationProviderKind.Local)
                await _identityFence.EnsureSubjectMayEnrollAsync(user.Id, cancellationToken);

            // IDs generated before lambda — captured via closure for retry safety
            var newUserId = Guid.CreateVersion7();
            var newActorId = Guid.CreateVersion7();
            var loginId = Guid.CreateVersion7();
            var claimId = Guid.CreateVersion7();
            var evidenceId = Guid.CreateVersion7();
            DateTime observedAtUtc = DateTime.UtcNow;

            async Task<BaseCommandResponse<Guid>> SynchronizeAsync(CancellationToken ct)
            {
                IdentityAccountResolution current = await _identityAccountResolver.ResolveAsync(
                    accountKey, request.AuthorityEvidence, ct);
                if (current.Decision == IdentityCorrelationDecision.RecoveryRequired
                    || current.User?.Id != user?.Id
                    || (resolution.Decision == IdentityCorrelationDecision.ExactBinding
                        && current.Decision != IdentityCorrelationDecision.ExactBinding))
                    return ExplicitBindingRequired();

                UserExternalLogin? currentLogin = await _userExternalLoginRepository.GetByProviderAndKey(accountKey);
                var observation = new IdentityEmailObservation(
                    email, emailVerified == true,
                    providerKind == AuthenticationProviderKind.Local || current.CanClaimVerifiedEmail,
                    claimId, evidenceId, observedAtUtc);
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
                    if (observation.CanClaimIdentityEmail && observation.EmailVerified
                        && !string.IsNullOrWhiteSpace(email)
                        && await _identityEmails.GetByNormalizedEmailAsync(email, ct) is not null)
                        return ExplicitBindingRequired();
                    var newUser = new User
                    {
                        Id = newUserId,
                        Pii = new UserPii
                        {
                            Email = email,
                            FirstName = ResolveFirstName(userDto.FirstName),
                            LastName = ResolveLastName(userDto.LastName)
                        },
                        EmailVerified = emailVerified ?? false
                    };

                    var createdUser = await _userRepository.Create(newUser);

                    var actor = new Actor
                    {
                        Id = newActorId,
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

                    UserExternalLogin createdLogin = await EnsureExternalLoginLinkInTransactionAsync(
                        createdUser, accountKey, loginId, ct);
                    await _identityEmailSynchronization.ExecuteAsync(createdUser, createdLogin, observation, ct);
                    return BaseCommandResponse.Success(id: createdUser.Id, message: "User synchronized successfully.");
                }
                else
                {
                    UserExternalLogin login = await EnsureExternalLoginLinkInTransactionAsync(
                        user, accountKey, loginId, ct);
                    IdentityEmailSynchronizationOutcome emailOutcome =
                        await _identityEmailSynchronization.ExecuteAsync(user, login, observation, ct);
                    if (!string.IsNullOrWhiteSpace(email)
                        && emailOutcome != IdentityEmailSynchronizationOutcome.ConflictingOwner)
                    {
                        user.Email = email;
                    }

                    user.FirstName = ResolveFirstName(userDto.FirstName);
                    user.LastName = ResolveLastName(userDto.LastName);
                    if (emailVerified.HasValue)
                    {
                        user.EmailVerified = emailVerified;
                    }

                    var actor = await _actorRepository.GetActorByUserId(user.Id);
                    if (actor != null)
                    {
                        actor.DisplayName = BuildDisplayName(userDto.FirstName, userDto.LastName);
                        await _actorRepository.Update(actor);
                    }

                    await _userRepository.Update(user);

                    return BaseCommandResponse.Success(id: user.Id,
                        message: emailOutcome == IdentityEmailSynchronizationOutcome.ConflictingOwner
                            ? "Account synchronized; the identity address needs recovery or operator support."
                            : "User synchronized successfully.");
                }
            }

            // Only the internal lifecycle receipt opts into the native store's existing transaction.
            // Both paths execute the same binding recheck and mirror writer; no second persistence boundary.
            var synchronization = request.LocalLifecycleSynchronization is null
                ? await _unitOfWork.ExecuteBootstrapConvergenceAsync(SynchronizeAsync, cancellationToken)
                : await SynchronizeAsync(cancellationToken);

            if (synchronization.IsSuccess && request.LocalLifecycleSynchronization is null)
                await _identityFence.AfterEnrollmentCommitAsync(
                    () => _cache.RemoveAsync($"user:detail:{synchronization.Id}", cancellationToken).AsTask());
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
        const string message = "Account synchronization requires recovery through the original sign-in provider or operator support.";
        return BaseCommandResponse.Validation<Guid>(errors: [message], message: message);
    }

    private async Task<UserExternalLogin> EnsureExternalLoginLinkInTransactionAsync(
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

            return existingByProviderAndKey;
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

        return await _userExternalLoginRepository.Create(login);
    }

    private static string NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : email.Trim().ToLowerInvariant();
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
