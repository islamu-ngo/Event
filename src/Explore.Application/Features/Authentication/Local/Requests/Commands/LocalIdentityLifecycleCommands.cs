
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record RequestLocalEmailVerificationCommand(
    LocalEmailVerificationRequestDto Request,
    LocalSessionAuthority? Authority) : ICommand<BaseCommandResponse<Guid>>;

public sealed record ConfirmLocalEmailCommand(
    LocalEmailConfirmationRequestDto Request) : ICommand<BaseCommandResponse<Guid>>;

public sealed record RequestLocalPasswordRecoveryCommand(
    LocalPasswordRecoveryRequestDto Request) : ICommand<BaseCommandResponse<Guid>>;

public sealed record CompleteLocalPasswordRecoveryCommand(
    LocalPasswordRecoveryCompletionRequestDto Request) : ICommand<BaseCommandResponse<Guid>>;

public sealed record ChangeLocalPasswordCommand(
    LocalPasswordChangeRequestDto Request,
    LocalSessionAuthority? Authority) : ICommand<BaseCommandResponse<Guid>>;
