using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.FileType;
using Explore.Application.Features.FileTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.FileTypes.Handlers.Queries;

public class GetFileTypeDetailsRequestHandler : IQueryHandler<GetFileTypeDetailsRequest, FileTypeDto?>
{
    private readonly IFileTypeRepository _fileTypeRepository;

    public GetFileTypeDetailsRequestHandler(IFileTypeRepository fileTypeRepository)
    {
        _fileTypeRepository = fileTypeRepository;
    }

    public async Task<FileTypeDto?> QueryAsync(GetFileTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var fileType = await _fileTypeRepository.GetById(request.Id);
        return FileTypeMapper.ToDetail(fileType);
    }
}
