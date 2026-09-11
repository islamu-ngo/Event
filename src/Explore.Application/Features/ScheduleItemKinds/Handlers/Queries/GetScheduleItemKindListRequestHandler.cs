using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Application.Features.ScheduleItemKinds.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ScheduleItemKinds.Handlers.Queries;

public class GetScheduleItemKindListRequestHandler : IRequestHandler<GetScheduleItemKindListRequest, List<ScheduleItemKindListDto>>
{
    private readonly IScheduleItemKindRepository _scheduleItemKindRepository;

    public GetScheduleItemKindListRequestHandler(IScheduleItemKindRepository scheduleItemKindRepository)
    {
        _scheduleItemKindRepository = scheduleItemKindRepository;
    }

    public async Task<List<ScheduleItemKindListDto>> Handle(GetScheduleItemKindListRequest request, CancellationToken cancellationToken)
    {
        var kinds = await _scheduleItemKindRepository.GetAll();
        return kinds.Select(RegistrationMapper.ToListItem).ToList();
    }
}
