using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StatusType;
using Explore.Application.Features.StatusTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.StatusTypes.Handlers.Queries;

public class GetStatusTypeListRequestHandler : IRequestHandler<GetStatusTypeListRequest, List<StatusTypeListDto>>
{
    private readonly IApprovalStatusRepository _statusTypeRepository;
    private readonly IMapper _mapper;

    public GetStatusTypeListRequestHandler(IApprovalStatusRepository statusTypeRepository, IMapper mapper)
    {
        _statusTypeRepository = statusTypeRepository;
        _mapper = mapper;
    }

    public async Task<List<StatusTypeListDto>> Handle(GetStatusTypeListRequest request, CancellationToken cancellationToken)
    {
        var statusTypes = await _statusTypeRepository.GetAll();
        return _mapper.Map<List<StatusTypeListDto>>(statusTypes);
    }
}
