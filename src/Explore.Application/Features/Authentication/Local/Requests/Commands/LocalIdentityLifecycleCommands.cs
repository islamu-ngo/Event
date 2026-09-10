
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record RequestLocalEmailVerificationCommand(
    LocalEmailVerificationRequestDto Request,
    LocalSessionAuthority? Authority) : IRequest<BaseCommandResponse<Guid>>;

public sealed record ConfirmLocalEmailCommand(
    LocalEmailConfirmationRequestDto Request) : IRequest<BaseCommandResponse<Guid>>;

public sealed record RequestLocalPasswordRecoveryCommand(
    LocalPasswordRecoveryRequestDto Request) : IRequest<BaseCommandResponse<Guid>>;

public sealed record CompleteLocalPasswordRecoveryCommand(
    LocalPasswordRecoveryCompletionRequestDto Request) : IRequest<BaseCommandResponse<Guid>>;

public sealed record ChangeLocalPasswordCommand(
    LocalPasswordChangeRequestDto Request,
    LocalSessionAuthority? Authority) : IRequest<BaseCommandResponse<Guid>>;
