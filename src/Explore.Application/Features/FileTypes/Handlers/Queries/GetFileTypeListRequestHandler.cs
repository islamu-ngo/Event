using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.FileType;
using Explore.Application.Features.FileTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.FileTypes.Handlers.Queries;

public class GetFileTypeListRequestHandler : IRequestHandler<GetFileTypeListRequest, List<FileTypeListDto>>
{
    private readonly IFileTypeRepository _fileTypeRepository;
    private readonly IMapper _mapper;

    public GetFileTypeListRequestHandler(IFileTypeRepository fileTypeRepository, IMapper mapper)
    {
        _fileTypeRepository = fileTypeRepository;
        _mapper = mapper;
    }

    public async Task<List<FileTypeListDto>> Handle(GetFileTypeListRequest request, CancellationToken cancellationToken)
    {
        var fileTypes = await _fileTypeRepository.GetAll();
        return _mapper.Map<List<FileTypeListDto>>(fileTypes);
    }
}
