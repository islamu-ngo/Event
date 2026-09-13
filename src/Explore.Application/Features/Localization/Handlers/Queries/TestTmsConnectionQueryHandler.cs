using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Handlers.Queries;

public class TestTmsConnectionQueryHandler : IQueryHandler<TestTmsConnectionQuery, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly ITranslationManagementProvider _translationProvider;

    public TestTmsConnectionQueryHandler(
        IAdminContext adminContext,
        ITranslationManagementProvider translationProvider)
    {
        _adminContext = adminContext;
        _translationProvider = translationProvider;
    }

    public async Task<BaseCommandResponse<Guid>> QueryAsync(TestTmsConnectionQuery request, CancellationToken cancellationToken)
    {
        var actor = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actor.HasValue || !await _adminContext.IsInstanceAdminAsync(actor.Value, cancellationToken))
        {
            const string message = "Instance administrator authority is required to test the localization TMS connection.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var isConnected = await _translationProvider.TestConnectionAsync(cancellationToken);
        if (isConnected)
        {
            return BaseCommandResponse.Success(Guid.Empty, "TMS connection successful.");
        }

        const string failureMessage = "TMS connection failed. Check provider settings and API credentials.";
        return BaseCommandResponse.Validation<Guid>([failureMessage], failureMessage);
    }
}
