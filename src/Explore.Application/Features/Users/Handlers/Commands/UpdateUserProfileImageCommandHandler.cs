using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.User.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Users.Handlers.Commands;

/// <summary>Changes an owned safe image without granting profile-field collection authority.</summary>
public sealed class UpdateUserProfileImageCommandHandler(IUserRepository users, IActorRepository actors,
    IStorageObjectRepository objects, IPrivacyErasureStateRepository erasure, ITenantContext tenant,
    IUnitOfWork unit, HybridCache cache, IUserContext caller, IAdminContext identity,
    IMachinePrincipalAccessor machine, IPrivacyIdentityFenceAuthority privacy,
    IAuthorizationProvider authorization) : ICommandHandler<UpdateUserProfileImageCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateUserProfileImageCommand request, CancellationToken cancellationToken = default)
    {
        if (!caller.IsAuthenticated || machine.IsMachineCaller
            || request.UserId == Guid.Empty || await identity.ResolveUserIdAsync(cancellationToken) != request.UserId)
            throw new AuthorizationException("Only the current Human may change this profile image.");
        var decision = await authorization.AuthorizeAsync(new(
            AuthorizationCapabilityCatalog.Require(
                Explore.Application.Authorization.ResourceKinds.User, Explore.Application.Authorization.AuthorizationActions.Update),
            request.UserId.ToString()), cancellationToken);
        if (!decision.IsAllowed)
        {
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(
                    Explore.Application.Authorization.ResourceKinds.User, Explore.Application.Authorization.AuthorizationActions.Update);
            throw new AuthorizationException("Profile image update is not authorized.");
        }
        var validation = await new UpdateUserProfileImageDtoValidator().ValidateAsync(request.ProfileImage, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage), "Profile image is invalid.");
        async Task EnsureAsync(CancellationToken token)
        {
            if (!caller.IsAuthenticated || machine.IsMachineCaller
                || await identity.ResolveUserIdAsync(token) != request.UserId
                || await privacy.IsSubjectFencedAsync(request.UserId, token))
                throw new AuthorizationException("Current profile image authority is required.");
        }
        var result = await privacy.ExecuteEnrollmentSerializedAsync(token => unit.ExecuteRecognitionEnrollmentAsync(async inner =>
        {
            await EnsureAsync(inner);
            if (await erasure.GetBySubjectAsync(request.UserId, inner) is not null)
                return BaseCommandResponse.NotFound<Guid>("User not found");
            var user = await users.GetById(request.UserId);
            if (user is null || user.IsDeleted) return BaseCommandResponse.NotFound<Guid>("User not found");
            if (user.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
                throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                    "The user was modified by another request. Reload and retry.", nameof(Domain.User), user.Id.ToString());
            var actor = await actors.GetTrackedActorByUserId(user.Id, inner);
            if (actor is null) return BaseCommandResponse.NotFound<Guid>("User profile not found");
            var image = request.ProfileImage;
            var stored = image.ProfilePictureId is { } id ? await objects.GetById(id) : null;
            if (image.ProfilePictureId is not null
                && (!SafeRasterContentPolicy.IsEligibleImageReference(stored, tenant.TenantId)
                    || !StoragePresentationUrlResolver.IsManagedProfileImage(stored)
                    || stored!.ActorId is { } ownerId && ownerId != actor.Id))
                return BaseCommandResponse.Validation<Guid>(["Profile image must be an active public safe-raster object in the current tenant."],
                    "Profile image is invalid.");
            if (stored is not null) stored.ActorId = actor.Id;
            actor.Pii.SetProfilePicture(image.ProfilePictureId, image.ExternalProfilePictureUri);
            actor.Pii.ProfilePicture = stored;
            await users.Update(user);
            return BaseCommandResponse.Success(user.Id, "Profile image updated successfully");
        }, EnsureAsync, token), EnsureAsync, cancellationToken);
        if (result.IsSuccess) await cache.RemoveAsync($"user:detail:{result.Id}", cancellationToken);
        return result;
    }
}
