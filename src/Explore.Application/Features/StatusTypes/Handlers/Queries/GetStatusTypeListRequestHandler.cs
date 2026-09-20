using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StatusType;
using Explore.Application.Features.StatusTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StatusTypes.Handlers.Queries;

public class GetStatusTypeListRequestHandler : IQueryHandler<GetStatusTypeListRequest, List<StatusTypeListDto>>
{
    private readonly IApprovalStatusRepository _statusTypeRepository;

    public GetStatusTypeListRequestHandler(IApprovalStatusRepository statusTypeRepository)
    {
        _statusTypeRepository = statusTypeRepository;
    }

    public async Task<List<StatusTypeListDto>> QueryAsync(GetStatusTypeListRequest request, CancellationToken cancellationToken)
    {
        var statusTypes = await _statusTypeRepository.GetAll();
        return statusTypes.Select(OrganizationMapper.ToApprovalStatus).ToList();
    }
}
