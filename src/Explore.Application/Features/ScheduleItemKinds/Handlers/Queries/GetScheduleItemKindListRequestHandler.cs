using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Application.Features.ScheduleItemKinds.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ScheduleItemKinds.Handlers.Queries;

public class GetScheduleItemKindListRequestHandler : IRequestHandler<GetScheduleItemKindListRequest, List<ScheduleItemKindListDto>>
{
    private readonly IScheduleItemKindRepository _scheduleItemKindRepository;
    private readonly IMapper _mapper;

    public GetScheduleItemKindListRequestHandler(IScheduleItemKindRepository scheduleItemKindRepository, IMapper mapper)
    {
        _scheduleItemKindRepository = scheduleItemKindRepository;
        _mapper = mapper;
    }

    public async Task<List<ScheduleItemKindListDto>> Handle(GetScheduleItemKindListRequest request, CancellationToken cancellationToken)
    {
        var kinds = await _scheduleItemKindRepository.GetAll();
        return _mapper.Map<List<ScheduleItemKindListDto>>(kinds);
    }
}
