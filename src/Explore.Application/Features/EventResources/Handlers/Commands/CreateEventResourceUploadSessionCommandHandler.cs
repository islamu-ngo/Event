using Explore.Application.Contracts.Operations;
using Explore.Application.Models.Storage;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Commands;

public sealed class CreateEventResourceUploadSessionCommandHandler(EventResourceFileUploadWorkflow workflow)
    : ICommandHandler<CreateEventResourceUploadSessionCommand, StorageUploadSessionResult>
{
    public Task<StorageUploadSessionResult> ExecuteAsync(
        CreateEventResourceUploadSessionCommand request, CancellationToken cancellationToken) =>
        workflow.ReserveAsync(request.ResourceId, request.UploadSessionDto, cancellationToken);
}
