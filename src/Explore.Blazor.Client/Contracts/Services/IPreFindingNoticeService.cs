using Explore.Blazor.Client.Services.Moderation;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IPreFindingNoticeService
{
    Task<PreFindingNoticeReadResult> GetAsync(
        Guid noticeId, bool reviewResponse, CancellationToken cancellationToken);

    Task<PreFindingNoticeFailure> AcknowledgeAsync(
        PreFindingNoticeView view, CancellationToken cancellationToken);

    Task<PreFindingNoticeFailure> RespondAsync(
        PreFindingNoticeView view, string statement, CancellationToken cancellationToken);
}
