using Explore.Application.Models.Storage;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Requests.Queries;

public sealed record GetPublicImageRequest(Guid StorageObjectId) : IRequest<StorageObjectContentResult?>;
