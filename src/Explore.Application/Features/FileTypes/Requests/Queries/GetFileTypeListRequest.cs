using System.Collections.Generic;
using Explore.Application.DTOs.FileType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.FileTypes.Requests.Queries;

public sealed record GetFileTypeListRequest : IQuery<List<FileTypeListDto>>
{
}
