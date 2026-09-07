using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models.Responses;

namespace Explore.Blazor.Client.Contracts.Services.SupportAccess;

public interface ISupportAccessClientService
{
    event Action? Changed;

    SupportAccessSessionDto? CurrentSession { get; }

    bool IsActive { get; }

    bool IsLoading { get; }

    bool IsStopping { get; }

    string? LastError { get; }

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<SupportAccessCommandResult> StartAsync(
        StartSupportAccessSessionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<SupportAccessCommandResult> StopCurrentAsync(
        string? endReasonText = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>> GetSessionsAsync(
        Guid targetTenantId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>> GetAuditEventsAsync(
        Guid targetTenantId,
        Guid sessionId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<SupportAccessCommandResult> ForceStopAsync(
        Guid sessionId,
        string? endReasonText = null,
        CancellationToken cancellationToken = default);
}
