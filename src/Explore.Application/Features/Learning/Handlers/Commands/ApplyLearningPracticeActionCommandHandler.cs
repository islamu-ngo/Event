using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Validators;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Handlers.Commands;

public sealed class ApplyLearningPracticeActionCommandHandler(
    IHumanLearningMutationPreparation preparation) : ICommandHandler<ApplyLearningPracticeActionCommand, LearningMutationResult>
{
    public async Task<LearningMutationResult> ExecuteAsync(
        ApplyLearningPracticeActionCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new ApplyLearningPracticeActionValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return LearningCommandFailures.Validation(validation);

        LearningOperationState operation;
        try
        {
            operation = await preparation.ExecuteAsync(command, cancellationToken);
        }
        catch (ConcurrencyConflictException exception)
        {
            return LearningCommandFailures.Conflict(exception, command.Input.PracticeId ?? Guid.Empty);
        }
        catch (InvalidOperationException exception) when (LearningCommandFailures.IsDomainConflict(exception))
        {
            return LearningCommandFailures.Conflict(command.Input.PracticeId ?? Guid.Empty, exception.Message);
        }

        bool cleared = command.Input.Action == LearningPracticeAction.Clear;
        Guid id = cleared ? command.Input.PracticeId!.Value : operation.Practice?.Id
            ?? throw new InvalidOperationException("A settled practice action requires its practice.");
        return LearningRepresentation.Mutation(operation, id, cleared: cleared);
    }
}
