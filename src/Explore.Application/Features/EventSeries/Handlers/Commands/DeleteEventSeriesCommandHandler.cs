using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSeries.Handlers.Commands;

public class DeleteEventSeriesCommandHandler : IRequestHandler<DeleteEventSeriesCommand, BaseCommandResponse<bool>>
{
    private readonly IEventSeriesRepository _eventSeriesRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;

    public DeleteEventSeriesCommandHandler(
        IEventSeriesRepository eventSeriesRepository,
        ITenantContext tenantContext,
        IAdminContext adminContext)
    {
        _eventSeriesRepository = eventSeriesRepository;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
    }

    public async Task<BaseCommandResponse<bool>> Handle(DeleteEventSeriesCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        Guid? userId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        IReadOnlyList<Guid> adminTenantIds = userId.HasValue
            ? await _adminContext.GetAdminTenantIdsAsync(userId.Value, cancellationToken)
            : [];

        if (!adminTenantIds.Contains(tenantId))
        {
            throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.Delete);
        }

        var series = await _eventSeriesRepository.GetById(request.Id);
        if (series == null)
        {
            return BaseCommandResponse.Validation<bool>(
                ["Event series not found."],
                "Event series not found.");
        }

        await _eventSeriesRepository.Delete(series);

        return BaseCommandResponse.Success(true, "Event series deleted successfully.");
    }
}
