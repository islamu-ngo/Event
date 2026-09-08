using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Explore.Application.Features.Users.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.Users.Handlers.Commands;

public sealed class DeleteUserCommandHandler(
    IPrivacyErasureService erasureService)
    : IRequestHandler<DeleteUserCommand, PrivacyErasureStartDto>
{
    public Task<PrivacyErasureStartDto> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken) =>
        erasureService.EraseUserAsync(request.UserId, request.IntentId, cancellationToken);
}
