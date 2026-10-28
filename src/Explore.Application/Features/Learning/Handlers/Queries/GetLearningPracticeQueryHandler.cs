using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning.Requests.Queries;
using Explore.Application.Features.Learning.Validators;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Handlers.Queries;

public sealed class GetLearningPracticeQueryHandler(
    IHumanCommitScopePreparation humanScope,
    IHumanLearningContextReader reader) : IQueryHandler<GetLearningPracticeQuery, LearningPracticeDto>
{
    public async Task<LearningPracticeDto> QueryAsync(
        GetLearningPracticeQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await new GetLearningPracticeValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            throw new ValidationException(validation);

        await humanScope.PrepareAsync(cancellationToken);
        var operation = await reader.ReadPracticeAsync(query.PracticeId, cancellationToken);
        return LearningRepresentation.Practice(operation);
    }
}
