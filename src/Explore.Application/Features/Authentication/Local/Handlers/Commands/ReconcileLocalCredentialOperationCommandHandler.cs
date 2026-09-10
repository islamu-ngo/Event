
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class ReconcileLocalCredentialOperationCommandHandler(
    IAdminContext adminContext,
    IPlatformUserRoleRepository platformUserRoles,
    ILocalCredentialAdministration credentialAdministration,
    IUnitOfWork unitOfWork,
    IUserRepository userRepository,
    IActorRepository actorRepository,
    IUserExternalLoginRepository externalLoginRepository)
    : IRequestHandler<ReconcileLocalCredentialOperationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReconcileLocalCredentialOperationCommand request,
        CancellationToken cancellationToken)
    {
        Guid? actorUserId = await LocalCredentialAdministrator.ResolveAsync(
            adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (actorUserId is null)
            return BaseCommandResponse.Authorization<Guid>();

        if (request.OperationId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<Guid>(errors: ["An operation identifier is required."]);
        }

        LocalCredentialProvisioningSnapshot? snapshot = await credentialAdministration
            .ReadProvisioningAsync(request.OperationId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return BaseCommandResponse.NotFound<Guid>(message: "Credential operation was not found.");

        LocalCredentialOperationReceipt receipt = snapshot.Receipt;
        if (receipt.Kind != LocalCredentialOperationKind.Create
            || receipt.Stage is not (LocalCredentialOperationStage.ProvisioningPending
                or LocalCredentialOperationStage.ChangeRequired))
        {
            return BaseCommandResponse.Conflict(id: request.OperationId);
        }

        BaseCommandResponse<Guid> binding = await unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            Guid? currentActor = await LocalCredentialAdministrator.ResolveAsync(
                adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: ct)
                .ConfigureAwait(false);
            if (currentActor != actorUserId)
                return BaseCommandResponse.Authorization<Guid>();

            return await BindAsync(snapshot, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        if (!binding.IsSuccess)
            return binding;

        if (await LocalCredentialAdministrator.ResolveAsync(
                adminContext: adminContext, platformUserRoles: platformUserRoles, cancellationToken: cancellationToken)
                .ConfigureAwait(false) != actorUserId)
        {
            return BaseCommandResponse.Authorization<Guid>();
        }
        LocalCredentialActivationOutcome activation = await credentialAdministration.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(
                operationId: request.OperationId,
                expectedOperationConcurrencyStamp: snapshot.OperationConcurrencyStamp),
            cancellationToken).ConfigureAwait(false);
        return activation switch
        {
            LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated =>
                BaseCommandResponse.Success(id: request.OperationId),
            LocalCredentialActivationOutcome.NotFound => BaseCommandResponse.NotFound<Guid>(),
            LocalCredentialActivationOutcome.Conflict or LocalCredentialActivationOutcome.BindingIncomplete =>
                BaseCommandResponse.Conflict(id: request.OperationId),
            _ => throw new InvalidOperationException("Unknown credential activation outcome.")
        };
    }

    private async Task<BaseCommandResponse<Guid>> BindAsync(
        LocalCredentialProvisioningSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        LocalCredentialOperationReceipt receipt = snapshot.Receipt;
        var accountKey = new ProviderAccountKey(
            providerKind: AuthenticationProviderKind.Local,
            value: receipt.LocalSubjectId.ToString("D"));
        User? user = await userRepository.GetUserWithDetails(receipt.ApplicationUserId, cancellationToken)
            .ConfigureAwait(false);
        Actor? actor = await actorRepository.GetActorWithDetails(receipt.PersonalActorId, cancellationToken)
            .ConfigureAwait(false);
        UserExternalLogin? login = await externalLoginRepository.GetByProviderAndKey(accountKey).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (user is { IsDeleted: false }
            && actor is { IsDeleted: false, IsSuspended: false }
            && actor.ActorTypeId == (int)ActorTypeEnum.User
            && actor.UserId == receipt.ApplicationUserId
            && login is not null && login.Id == receipt.ExternalLoginId
            && login.UserId == receipt.ApplicationUserId)
        {
            return BaseCommandResponse.Success(id: receipt.OperationId);
        }

        if (user is not null || actor is not null || login is not null
            || await actorRepository.GetActorByUserId(receipt.ApplicationUserId).ConfigureAwait(false) is not null
            || await externalLoginRepository.Exists(receipt.ExternalLoginId).ConfigureAwait(false)
            || (await externalLoginRepository.GetByUser(receipt.ApplicationUserId).ConfigureAwait(false)).Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BaseCommandResponse.Conflict(id: receipt.OperationId);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var createdUser = new User
        {
            Id = receipt.ApplicationUserId,
            Pii = new UserPii
            {
                Email = snapshot.Email,
                FirstName = snapshot.FirstName,
                LastName = snapshot.LastName
            },
            EmailVerified = snapshot.EmailVerified,
            CreatedAt = receipt.CreatedAt,
            CreatedBy = receipt.InitiatingApplicationUserId
        };
        await userRepository.Create(createdUser).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await actorRepository.Create(new Actor
        {
            Id = receipt.PersonalActorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = receipt.ApplicationUserId,
            User = createdUser,
            Pii = new ActorPii { DisplayName = $"{snapshot.FirstName} {snapshot.LastName}".Trim() },
            CreatedAt = receipt.CreatedAt,
            CreatedBy = receipt.InitiatingApplicationUserId
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await externalLoginRepository.Create(new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = receipt.ApplicationUserId,
            User = createdUser,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = accountKey.Value,
            ProviderDisplayName = "Local",
            CreatedAt = receipt.CreatedAt,
            CreatedBy = receipt.InitiatingApplicationUserId
        }).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return BaseCommandResponse.Success(id: receipt.OperationId);
    }
}
