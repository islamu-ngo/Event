using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Learning.Requests.Queries;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Handlers.Queries;

public sealed class GetLearningGuidancePolicyQueryHandler(
    IHumanCommitScopePreparation humanScope,
    IHumanLearningContextReader contexts,
    ILearningGuidancePolicyReader policies)
    : IQueryHandler<GetLearningGuidancePolicyQuery, LearningGuidancePolicyDto>
{
    public async Task<LearningGuidancePolicyDto> QueryAsync(
        GetLearningGuidancePolicyQuery query, CancellationToken cancellationToken = default)
    {
        await humanScope.PrepareAsync(cancellationToken);
        var operation = await contexts.ReadContextAsync("self", null, cancellationToken);
        var resolved = policies.Resolve(operation.Sources
            ?? throw new UnauthorizedAccessException("Original guidance sources are required."));
        return LearningGuidancePolicyCodec.ToDto(resolved, operation.CanManageSpace);
    }
}
