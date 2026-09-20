using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Application.Features.ScheduleItemKinds.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ScheduleItemKinds.Handlers.Queries;

public class GetScheduleItemKindListRequestHandler : IQueryHandler<GetScheduleItemKindListRequest, List<ScheduleItemKindListDto>>
{
    private readonly IScheduleItemKindRepository _scheduleItemKindRepository;

    public GetScheduleItemKindListRequestHandler(IScheduleItemKindRepository scheduleItemKindRepository)
    {
        _scheduleItemKindRepository = scheduleItemKindRepository;
    }

    public async Task<List<ScheduleItemKindListDto>> QueryAsync(GetScheduleItemKindListRequest request, CancellationToken cancellationToken)
    {
        var kinds = await _scheduleItemKindRepository.GetAll();
        return kinds.Select(RegistrationMapper.ToListItem).ToList();
    }
}
