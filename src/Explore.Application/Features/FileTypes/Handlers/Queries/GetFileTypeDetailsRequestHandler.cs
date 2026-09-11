using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.FileType;
using Explore.Application.Features.FileTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.FileTypes.Handlers.Queries;

public class GetFileTypeDetailsRequestHandler : IRequestHandler<GetFileTypeDetailsRequest, FileTypeDto>
{
    private readonly IFileTypeRepository _fileTypeRepository;

    public GetFileTypeDetailsRequestHandler(IFileTypeRepository fileTypeRepository)
    {
        _fileTypeRepository = fileTypeRepository;
    }

    public async Task<FileTypeDto> Handle(GetFileTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var fileType = await _fileTypeRepository.GetById(request.Id);
        return FileTypeMapper.ToDetail(fileType)!;
    }
}
