using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;

public sealed class GetEventSessionTemplateDiffQueryHandler
    : IQueryHandler<GetEventSessionTemplateDiffQuery, BaseCommandResponse<TemplateDiffDto>>
{
    private readonly IEventSessionTemplateDiffService _diffService;

    public GetEventSessionTemplateDiffQueryHandler(IEventSessionTemplateDiffService diffService)
    {
        _diffService = diffService;
    }

    public async Task<BaseCommandResponse<TemplateDiffDto>> QueryAsync(
        GetEventSessionTemplateDiffQuery request,
        CancellationToken cancellationToken)
    {
        var diff = await _diffService.ComputeDiffAsync(request.EventSessionId, request.TargetTemplateVersion, cancellationToken);
        return BaseCommandResponse.Success(diff, "Event session template diff retrieved successfully.");
    }
}
