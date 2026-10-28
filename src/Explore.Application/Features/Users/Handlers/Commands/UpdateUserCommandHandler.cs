using System.Linq;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Users.Handlers.Commands;

/// <summary>Consumes names only within the native owner's already-open exact-self collection transaction.</summary>
public sealed class UpdateUserCommandHandler(IUserRepository users,
    IPrivacyErasureStateRepository erasure, HybridCache cache,
    IModerationProfileCollectionAdmissionOwner admission) : ICommandHandler<UpdateUserCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateUserCommand request, CancellationToken cancellationToken = default)
    {
        await admission.RequireCurrentAsync(request.UserId, cancellationToken);
        var validation = await new UpdateUserDtoValidator().ValidateAsync(request.UpdateUserDto, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage),
                "User update failed due to validation errors.");
        if (await erasure.GetBySubjectAsync(request.UserId, cancellationToken) is not null)
            return BaseCommandResponse.NotFound<Guid>("User not found");
        var user = await users.GetById(request.UserId);
        if (user is null || user.IsDeleted) return BaseCommandResponse.NotFound<Guid>("User not found");
        if (user.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "The user was modified by another request. Reload and retry.", nameof(Domain.User), user.Id.ToString());
        user.FirstName = request.UpdateUserDto.Names!.FirstName;
        user.LastName = request.UpdateUserDto.Names.LastName;
        await users.Update(user);
        await admission.RequireCurrentAsync(user.Id, cancellationToken);
        await cache.RemoveAsync($"user:detail:{user.Id}", cancellationToken);
        return BaseCommandResponse.Success(user.Id, "User updated successfully");
    }
}
