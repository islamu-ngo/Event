// ABOUTME: Verifies a PDS session, enforces provider admission, and atomically stores local ATProto identity.
// ABOUTME: Issues a platform JWT after commit and persists refresh state once the target tenant exists.

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Authentication;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Services;
using Explore.Application.Features.Authentication.Atproto.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class BootstrapAtprotoSessionCommandHandler(
    IAtprotoOAuthSecurityGateway securityGateway,
    IAtprotoSessionTokenIssuer tokenIssuer,
    ISender sender,
    IUserExternalLoginRepository externalLoginRepository,
    IInstanceBootstrapStateRepository bootstrapRepository,
    IAuthenticationProviderDispatcher authenticationProviderDispatcher,
    IAuthProviderConfigurationService authProviderConfiguration,
    AtprotoJitAccountProvisioningOperation jitAccountProvisioning,
    AtprotoSubjectOnboardingOperation onboardingOperation,
    IUnitOfWork unitOfWork,
    ISettingMutationLock settingMutationLock,
    IVisitorAccessCapabilityResolver visitorAccessCapabilityResolver,
    IAdminCacheInvalidator adminCacheInvalidator,
    ITenantContext tenantContext,
    IConfiguration configuration,
    TimeProvider timeProvider)
    : IRequestHandler<BootstrapAtprotoSessionCommand, AtprotoSessionBootstrapResult>
{
    public async Task<AtprotoSessionBootstrapResult> Handle(
        BootstrapAtprotoSessionCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new BootstrapAtprotoSessionCommandValidator()
            .ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return AtprotoSessionBootstrapResult.Failed("invalid_request");
        }

        var tenantId = tenantContext.TenantId;
        var verification = await securityGateway.VerifyAsync(
            new AtprotoOAuthVerificationInput(
                request.ExpectedDid,
                new Uri(request.ExpectedPdsUri, UriKind.Absolute),
                request.OAuthClientKeyId,
                request.OAuthSessionPayload),
            cancellationToken).ConfigureAwait(false);
        if (verification.Session is not { } verified)
        {
            return AtprotoSessionBootstrapResult.Failed(verification.FailureCode ?? "pds_verification_failed");
        }

        if (verified.Did != request.ExpectedDid)
        {
            return AtprotoSessionBootstrapResult.Failed("pds_identity_mismatch");
        }

        ProviderAccountKey accountKey =
            PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
                verified.Did);
        AuthenticationProviderKind primaryProvider =
            await authenticationProviderDispatcher
                .GetActivePrimaryProviderAsync(cancellationToken)
                .ConfigureAwait(false);
        AuthProviderConfigurationDto providerConfiguration =
            await authProviderConfiguration
                .ReadConfigurationAsync()
                .ConfigureAwait(false);
        if (primaryProvider != AuthenticationProviderKind.Atproto
            && !providerConfiguration.AtprotoLoginEnabled)
        {
            return AtprotoSessionBootstrapResult.Failed(
                "provider_inactive");
        }

        var login = await externalLoginRepository
            .GetByProviderAndKey(accountKey).ConfigureAwait(false);
        InstanceBootstrapState? bootstrap = await bootstrapRepository
            .GetCurrent(cancellationToken).ConfigureAwait(false);
        // Pending configured setup admits only its verified claimant. After completion,
        // replay that claim only for its exact linked owner, not unrelated visitors.
        bool configuredAccount = bootstrap?.Mode == InstanceBootstrapMode.ConfiguredAdministrator
            && (bootstrap.Status != InstanceBootstrapStatus.Completed
                || IsExactLinkedLogin(login, accountKey)
                    && bootstrap.CompletedByUserId == login!.UserId);
        if (configuredAccount)
        {
            var claim = await sender.Send(
                new ClaimConfiguredInstanceAdministratorCommand
                {
                    AuthenticatedAccount = accountKey,
                    UserId = IsExactLinkedLogin(login, accountKey)
                        ? login!.UserId
                        : Guid.CreateVersion7(),
                    Email = configuration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"],
                    FirstName = configuration["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"],
                    LastName = configuration["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"],
                    EmailVerified = false
                },
                cancellationToken).ConfigureAwait(false);

            if (!claim.IsSuccess)
            {
                if (bootstrap?.Status != InstanceBootstrapStatus.Completed
                    || !IsExactLinkedLogin(login, accountKey)
                    || bootstrap.CompletedByUserId != login!.UserId)
                {
                    return AtprotoSessionBootstrapResult.Failed("account_not_linked");
                }
            }

            login = await externalLoginRepository
                .GetByProviderAndKey(accountKey).ConfigureAwait(false);
            if (!IsExactLinkedLogin(login, accountKey))
            {
                return AtprotoSessionBootstrapResult.Failed("configured_claim_incomplete");
            }
        }

        var indexedAt = timeProvider.GetUtcNow().UtcDateTime;
        var jitIds = new AtprotoJitAccountIds(
            login?.UserId ?? Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        BootstrapPersistenceOutcome persistence =
            await settingMutationLock.ExecuteOrderedGroupsAsync(
                [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
                leaseToken => unitOfWork.ExecuteBootstrapConvergenceAsync(
                async transactionToken =>
        {
            // Recheck after acquiring authority and starting the convergence snapshot.
            // An exact existing DID binding has login authority independently of signup.
            UserExternalLogin? currentLogin = await externalLoginRepository
                .GetByProviderAndKey(accountKey).ConfigureAwait(false);
            if (!IsExactLinkedLogin(currentLogin, accountKey))
            {
                if (configuredAccount)
                {
                    return BootstrapPersistenceOutcome.Failed("configured_claim_incomplete");
                }

                var capability = await visitorAccessCapabilityResolver
                    .ResolveAsync(tenantId, transactionToken).ConfigureAwait(false);
                if (capability.Mode != VisitorAccessMode.FullRegistrationAndAuth
                    || !capability.SignupDestinations.Any(destination =>
                        destination.Provider == AuthenticationProviderKind.Atproto))
                {
                    return BootstrapPersistenceOutcome.Failed("account_not_linked");
                }
            }

            AtprotoPlatformAccount? account =
                await jitAccountProvisioning.EnsureAsync(
                    accountKey,
                    jitIds,
                    indexedAt,
                    transactionToken).ConfigureAwait(false);
            if (account is null)
            {
                return BootstrapPersistenceOutcome.Failed(
                    "linked_identity_incomplete");
            }

            AtprotoSubjectOnboardingResult onboarding =
                await onboardingOperation.ExecuteAsync(
                request,
                verified,
                account.User,
                account.PersonalActor,
                tenantId,
                indexedAt,
                transactionToken).ConfigureAwait(false);
            if (!onboarding.Success)
            {
                return BootstrapPersistenceOutcome.Failed(
                    onboarding.FailureCode);
            }

            if (onboarding.ParticipationId is not null)
            {
                AtprotoPreparedOAuthSession preparedSession =
                    await securityGateway.PreparePersistenceAsync(
                        verified,
                        tenantId,
                        account.User.Id,
                        transactionToken).ConfigureAwait(false);
                await securityGateway.PersistPreparedAsync(
                    preparedSession,
                    transactionToken).ConfigureAwait(false);
            }

            return BootstrapPersistenceOutcome.Succeeded(
                account.User.Id,
                onboarding.ActorId!.Value,
                onboarding.ParticipationId);
        },
                leaseToken),
                cancellationToken).ConfigureAwait(false);
        if (!persistence.Success)
        {
            return AtprotoSessionBootstrapResult.Failed(
                persistence.FailureCode);
        }

        adminCacheInvalidator.InvalidateUser(persistence.UserId!.Value);
        var issued = await tokenIssuer
            .IssueAsync(
                persistence.UserId.Value,
                tenantId,
                verified.Did,
                cancellationToken).ConfigureAwait(false);
        return AtprotoSessionBootstrapResult.Succeeded(
            persistence.UserId.Value,
            persistence.ActorId!.Value,
            persistence.ParticipationId,
            request.Classification,
            issued,
            request.CanonicalActorId,
            request.ExpectedCanonicalActorConcurrencyStamp);
    }

    private static bool IsExactLinkedLogin(UserExternalLogin? login, ProviderAccountKey accountKey) =>
        login is not null
        && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Atproto
        && string.Equals(login.ProviderKey, accountKey.Value, StringComparison.Ordinal);

    private sealed record BootstrapPersistenceOutcome(
        bool Success,
        string FailureCode,
        Guid? UserId = null,
        Guid? ActorId = null,
        Guid? ParticipationId = null)
    {
        public static BootstrapPersistenceOutcome Failed(string failureCode) =>
            new(false, failureCode);

        public static BootstrapPersistenceOutcome Succeeded(
            Guid userId,
            Guid actorId,
            Guid? participationId) =>
            new(true, string.Empty, userId, actorId, participationId);
    }
}
