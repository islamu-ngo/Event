using Explore.Application.DTOs.EventResource;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Application.Services;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Consumes the actual terminal projection before native upload tests inspect stored fields.</summary>
internal sealed class EventResourceUploadTestConsumer(EventResourceFileUploadWorkflow workflow)
{
    internal EventResourceFileUploadWorkflow Workflow => workflow;

    internal Task<BaseCommandResponse<StorageUploadSessionDto>> ReserveAsync(Guid resourceId,
        CreateEventResourceUploadSessionDto dto, CancellationToken cancellationToken) =>
        ConsumeAsync(workflow.ReserveAsync(resourceId, dto, cancellationToken));

    internal Task<BaseCommandResponse<StorageUploadSessionDto>> FinalizeAsync(
        FinalizeStorageUploadSessionCommand request, CancellationToken cancellationToken) =>
        ConsumeAsync(workflow.FinalizeAsync(request, cancellationToken));

    internal Task<BaseCommandResponse<StorageUploadSessionDto>> CancelAsync(
        Guid sessionId, CancellationToken cancellationToken) =>
        ConsumeAsync(workflow.CancelAsync(sessionId, cancellationToken));

    internal static async Task<BaseCommandResponse<StorageUploadSessionDto>> ConsumeAsync(
        Task<StorageUploadSessionResult> pending)
    {
        await using var result = await pending;
        if (result.Response is { } ordinary) return ordinary;
        BaseCommandResponse<StorageUploadSessionDto>? delivered = null;
        var outcome = await result.Projection!.DiscloseAsync(
            (response, _) => Task.FromResult(response),
            (response, _) => { delivered = response; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        return delivered ?? throw new InvalidOperationException("A successful terminal read must deliver its response.");
    }
}
