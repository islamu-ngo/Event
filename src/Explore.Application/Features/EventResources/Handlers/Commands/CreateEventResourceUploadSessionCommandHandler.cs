using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;

namespace Explore.Application.Features.EventResources.Handlers.Commands;

public sealed class CreateEventResourceUploadSessionCommandHandler(EventResourceFileUploadWorkflow workflow)
    : ICommandHandler<CreateEventResourceUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>>
{
    public Task<BaseCommandResponse<StorageUploadSessionDto>> ExecuteAsync(
        CreateEventResourceUploadSessionCommand request, CancellationToken cancellationToken) =>
        workflow.ReserveAsync(request.ResourceId, request.UploadSessionDto, cancellationToken);
}
