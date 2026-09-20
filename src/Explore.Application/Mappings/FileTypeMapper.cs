using Explore.Application.DTOs.FileType;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class FileTypeMapper
{
    public static partial FileTypeDto? ToDetail(FileType? source);

    public static partial FileTypeListDto ToListItem(FileType source);
}
