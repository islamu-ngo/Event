using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Models.Storage;
using Explore.Domain;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Federation.Atproto.Handlers.Commands;

public sealed class ImportAtprotoFederatedEventCommandHandler(
    IAtprotoJetstreamRepository repository,
    IAtprotoThumbnailBlobGateway thumbnailGateway)
    : IRequestHandler<ImportAtprotoFederatedEventCommand, bool>
{
    public async Task<bool> Handle(
        ImportAtprotoFederatedEventCommand request,
        CancellationToken cancellationToken)
    {
        AtprotoJetstreamApplyRequest applyRequest = request.ApplyRequest;
        IReadOnlyList<AtprotoFederatedEventImportPlan> importPlans =
            await BuildImportPlansAsync(applyRequest, cancellationToken);
        IReadOnlyList<AtprotoFederatedEventImportPlan> stagedPlans =
            await StageThumbnailsAsync(importPlans, cancellationToken);

        AtprotoPersistenceApplyResult result;
        try
        {
            result = await repository.TryApplyAndAdvanceWithResultAsync(
                applyRequest with { EventImports = stagedPlans },
                cancellationToken);
        }
        catch
        {
            await CleanupUnconsumedAsync(stagedPlans, []);
            throw;
        }

        await CleanupUnconsumedAsync(stagedPlans, result.ConsumedStagedThumbnails);
        return result.Applied;
    }

    private static async Task<IReadOnlyList<AtprotoFederatedEventImportPlan>> BuildImportPlansAsync(
        AtprotoJetstreamApplyRequest applyRequest,
        CancellationToken cancellationToken)
    {
        if (applyRequest.EventProjection is null)
        {
            return [];
        }

        AtprotoRecord record = applyRequest.Record
            ?? throw new ValidationException("An event projection requires a canonical ATProto record.");
        IEnumerable<Guid> tenantIds = applyRequest.Presentations
            .Where(presentation => presentation.IsVisible)
            .Select(presentation => presentation.TenantId);
        return await AtprotoFederatedEventImportPlanFactory.CreateAsync(
            record,
            applyRequest.EventProjection,
            tenantIds,
            cancellationToken);
    }

    private async Task<IReadOnlyList<AtprotoFederatedEventImportPlan>> StageThumbnailsAsync(
        IReadOnlyList<AtprotoFederatedEventImportPlan> plans,
        CancellationToken cancellationToken)
    {
        var stagedPlans = new List<AtprotoFederatedEventImportPlan>(plans.Count);
        try
        {
            foreach (AtprotoFederatedEventImportPlan plan in plans)
            {
                FileStorageWriteResult? staged = await thumbnailGateway.FetchAndStageAsync(
                    plan.Thumbnail,
                    plan.TenantId,
                    cancellationToken);
                stagedPlans.Add(plan with { StagedThumbnail = staged });
            }

            return stagedPlans;
        }
        catch
        {
            await CleanupUnconsumedAsync(stagedPlans, []);
            throw;
        }
    }

    private async Task CleanupUnconsumedAsync(
        IEnumerable<AtprotoFederatedEventImportPlan> plans,
        IReadOnlyList<FileStorageWriteResult> consumed)
    {
        foreach (FileStorageWriteResult staged in plans
                     .Select(plan => plan.StagedThumbnail)
                     .OfType<FileStorageWriteResult>()
                     .Where(staged => !consumed.Contains(staged)))
        {
            await thumbnailGateway.CleanupAsync(staged, CancellationToken.None);
        }
    }
}
