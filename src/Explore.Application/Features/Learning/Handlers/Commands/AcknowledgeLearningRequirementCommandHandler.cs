using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Validators;
using Explore.Application.Responses;
using Explore.Domain.Learning;

namespace Explore.Application.Features.Learning.Handlers.Commands;

public sealed class AcknowledgeLearningRequirementCommandHandler(
    IHumanLearningMutationPreparation preparation)
    : ICommandHandler<AcknowledgeLearningRequirementCommand, LearningMutationResult>
{
    public async Task<LearningMutationResult> ExecuteAsync(
        AcknowledgeLearningRequirementCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await new AcknowledgeLearningRequirementValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return LearningCommandFailures.Validation(validation);

        LearningOperationState operation;
        try
        {
            operation = await preparation.ExecuteAsync(command, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return new(BaseCommandResponse.Validation<Guid>(exception.Errors), null);
        }
        catch (ConcurrencyConflictException exception)
        {
            return LearningCommandFailures.Conflict(exception, Guid.Empty);
        }
        catch (InvalidOperationException exception) when (LearningCommandFailures.IsDomainConflict(exception))
        {
            return LearningCommandFailures.Conflict(Guid.Empty, exception.Message);
        }

        LearningRequirementDefinition definition = LearningRequirementDefinition.Current;
        LearningRequirementAcknowledgement acknowledgement = operation.State?.RequirementAcknowledgements
            .SingleOrDefault(value => value.RequirementKey == definition.RequirementKey
                && value.MaterialVersion == definition.MaterialVersion)
            ?? throw new InvalidOperationException("Settled acknowledgement requires its private evidence.");
        return LearningRepresentation.Mutation(operation, acknowledgement.Id);
    }
}
