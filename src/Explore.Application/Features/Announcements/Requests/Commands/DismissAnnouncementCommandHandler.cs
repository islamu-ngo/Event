using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Announcements.Requests.Commands;

public sealed class DismissAnnouncementCommandHandler(
    IHumanSettingsMutationPreparation preparation, IOrdinarySettingsMutationStore store)
    : ICommandHandler<DismissAnnouncementCommand, AnnouncementDismissalResult>
{
    public async Task<AnnouncementDismissalResult> ExecuteAsync(
        DismissAnnouncementCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ActorUserId == Guid.Empty)
            throw new UnauthorizedAccessException("An authenticated dismissal subject is required.");
        var prepared = await preparation.PrepareAsync(request, cancellationToken);
        return await preparation.ExecutePreparedAsync(prepared,
            (_, token) => store.ApplyAnnouncementDismissalAsync(
                preparation.CurrentOrdinarySource
                    ?? throw new UnauthorizedAccessException("The original dismissal source is required."),
                request, token), cancellationToken);
    }
}
