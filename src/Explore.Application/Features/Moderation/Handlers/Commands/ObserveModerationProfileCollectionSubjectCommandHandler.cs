using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class ObserveModerationProfileCollectionSubjectCommandHandler(
    IModerationProfileCollectionSubjectObservationOwner observations)
    : ICommandHandler<ObserveModerationProfileCollectionSubjectCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ObserveModerationProfileCollectionSubjectCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new ObserveModerationProfileCollectionSubjectCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var observation = await observations.ObserveAsync(command.ToNomination(), cancellationToken);
        return observation is null
            ? BaseCommandResponse.NotFound<Guid>()
            : BaseCommandResponse.Success(observation.Id);
    }
}

public sealed class ObserveModerationProfileCollectionSubjectCommandValidator
    : AbstractValidator<ObserveModerationProfileCollectionSubjectCommand>
{
    public ObserveModerationProfileCollectionSubjectCommandValidator()
    {
        RuleFor(command => command.ProfileId).Must(id =>
            id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80);
        RuleFor(command => command.Mode).IsInEnum();
    }
}
