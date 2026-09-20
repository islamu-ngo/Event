using Explore.Application.DTOs.FileType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.FileTypes.Requests.Queries;

public sealed record GetFileTypeDetailsRequest(int Id = default) : IQuery<FileTypeDto?>;
