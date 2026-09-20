using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.FileType;
using Explore.Application.Features.FileTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.FileTypes.Handlers.Queries;

public class GetFileTypeListRequestHandler : IQueryHandler<GetFileTypeListRequest, List<FileTypeListDto>>
{
    private readonly IFileTypeRepository _fileTypeRepository;

    public GetFileTypeListRequestHandler(IFileTypeRepository fileTypeRepository)
    {
        _fileTypeRepository = fileTypeRepository;
    }

    public async Task<List<FileTypeListDto>> QueryAsync(GetFileTypeListRequest request, CancellationToken cancellationToken)
    {
        var fileTypes = await _fileTypeRepository.GetAll();
        return fileTypes.Select(FileTypeMapper.ToListItem).ToList();
    }
}
