using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.FileType;
using Explore.Application.Features.FileTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.FileTypes.Handlers.Queries;

public class GetFileTypeDetailsRequestHandler : IRequestHandler<GetFileTypeDetailsRequest, FileTypeDto>
{
    private readonly IFileTypeRepository _fileTypeRepository;
    private readonly IMapper _mapper;

    public GetFileTypeDetailsRequestHandler(IFileTypeRepository fileTypeRepository, IMapper mapper)
    {
        _fileTypeRepository = fileTypeRepository;
        _mapper = mapper;
    }

    public async Task<FileTypeDto> Handle(GetFileTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var fileType = await _fileTypeRepository.GetById(request.Id);
        return _mapper.Map<FileTypeDto>(fileType);
    }
}
