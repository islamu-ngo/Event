using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;

public sealed class GetEventTemplateDiffQueryHandler
    : IQueryHandler<GetEventTemplateDiffQuery, BaseCommandResponse<TemplateDiffDto>>
{
    private readonly IEventTemplateDiffService _diffService;

    public GetEventTemplateDiffQueryHandler(IEventTemplateDiffService diffService)
    {
        _diffService = diffService;
    }

    public async Task<BaseCommandResponse<TemplateDiffDto>> QueryAsync(
        GetEventTemplateDiffQuery request,
        CancellationToken cancellationToken)
    {
        var diff = await _diffService.ComputeDiffAsync(request.EventId, request.TargetTemplateVersion, cancellationToken);
        return BaseCommandResponse.Success(diff, "Event template diff retrieved successfully.");
    }
}
