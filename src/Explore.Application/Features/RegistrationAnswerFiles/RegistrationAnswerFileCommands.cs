using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.RegistrationAnswerFiles.Commands;

public sealed record ReleaseRegistrationAnswerFileCommand(Guid TenantId, Guid Id, string Reason)
    : ICommand<BaseCommandResponse<Guid>>;

public sealed class ReleaseRegistrationAnswerFileCommandValidator
    : AbstractValidator<ReleaseRegistrationAnswerFileCommand>
{
    public ReleaseRegistrationAnswerFileCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReleaseRegistrationAnswerFileCommandHandler(
    IRegistrationAnswerFileRepository repository,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
    : ICommandHandler<ReleaseRegistrationAnswerFileCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ReleaseRegistrationAnswerFileCommand command,
        CancellationToken cancellationToken = default)
    {
        await new ReleaseRegistrationAnswerFileCommandValidator().ValidateAndThrowAsync(command, cancellationToken);
        Guid? actorId = currentUserService.UserId;
        if (!currentUserService.IsAuthenticated || actorId is null || actorId == Guid.Empty)
        {
            return Failure(command.Id, "Authenticated release operator could not be resolved.",
                "registration_answer_file_release_operator_required");
        }

        RegistrationAnswerFileReleaseResult? result = await repository.ReleaseAsync(
            command.TenantId,
            command.Id,
            actorId.Value,
            command.Reason,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        if (result is null)
        {
            return Failure(command.Id, "Registration answer file was not found.",
                "registration_answer_file_not_found");
        }

        return BaseCommandResponse.Success(
            result.File.Id,
            result.WasAlreadyReleased
                ? "Registration answer file was already released; the original audit was preserved."
                : "Registration answer file released.");
    }

    private static BaseCommandResponse<Guid> Failure(Guid id, string message, string code) =>
        BaseCommandResponse.Failure<Guid>(code, message, [message], id);
}
