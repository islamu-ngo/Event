using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Features.Users.Requests.Commands;

namespace Explore.Application.Features.Users.Handlers.Commands;

public sealed class DeleteUserCommandHandler(
    IPrivacyErasureService erasureService)
    : ICommandHandler<DeleteUserCommand, PrivacyErasureStartDto>
{
    public Task<PrivacyErasureStartDto> ExecuteAsync(
        DeleteUserCommand request,
        CancellationToken cancellationToken = default) =>
        erasureService.EraseUserAsync(request.UserId, request.IntentId, cancellationToken);
}
