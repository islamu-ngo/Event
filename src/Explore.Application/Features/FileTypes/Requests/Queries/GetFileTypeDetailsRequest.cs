using Explore.Application.DTOs.FileType;
using MediatR;

namespace Explore.Application.Features.FileTypes.Requests.Queries;

public sealed record GetFileTypeDetailsRequest(int Id = default) : IRequest<FileTypeDto>;
