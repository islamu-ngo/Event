using Explore.Application.Models.Storage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Requests.Queries;

public sealed record GetPublicImageRequest(Guid StorageObjectId) : IQuery<StorageObjectContentResult?>;
