using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Models.Responses;
using Explore.Blazor.Client.Services.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class SupportAccessClientService(
    IBffClient bffClient,
    ILogger<SupportAccessClientService> logger) : ISupportAccessClientService
{
    private const string CurrentPath = "/bff/support-access/current";
    private const string StartPath = "/bff/support-access/sessions";
    private const string StopCurrentPath = "/bff/support-access/sessions/current/stop";
    private const int DefaultListLimit = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Action? Changed;

    public SupportAccessSessionDto? CurrentSession { get; private set; }

    public bool IsActive => CurrentSession?.IsActive == true;

    public bool IsLoading { get; private set; }

    public bool IsStopping { get; private set; }

    public string? LastError { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        LastError = null;
        Notify();

        try
        {
            var current = await bffClient.GetAsync<CurrentSupportAccessSessionDto>(
                CurrentPath,
                cancellationToken);
            SetCurrentSession(current?.IsActive == true ? current.Session : null);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not refresh support-access status.");
            CurrentSession = null;
            LastError = "Support access status is unavailable.";
            Notify();
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    public async Task<SupportAccessCommandResult> StartAsync(
        StartSupportAccessSessionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        LastError = null;
        Notify();

        try
        {
            var resource = await bffClient.SendAsync<StartSupportAccessSessionRequestDto, HalResourceOfSupportAccessSessionDto>(
                HttpMethod.Post,
                StartPath,
                request,
                cancellationToken);
            if (resource is null)
            {
                return Fail("Support access could not be started.");
            }

            var session = MapSession(resource);
            if (session is null)
            {
                return Fail("Support access started, but the returned session was invalid.");
            }

            SetCurrentSession(session);
            return SupportAccessCommandResult.Succeeded();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start support access.");
            return Fail("Support access could not be started.");
        }
    }

    public async Task<SupportAccessCommandResult> StopCurrentAsync(
        string? endReasonText = null,
        CancellationToken cancellationToken = default)
    {
        IsStopping = true;
        LastError = null;
        Notify();

        try
        {
            var request = new StopSupportAccessSessionRequestDto
            {
                EndReasonText = endReasonText
            };
            var resource = await bffClient.SendAsync<StopSupportAccessSessionRequestDto, HalResourceOfSupportAccessSessionDto>(
                HttpMethod.Post,
                StopCurrentPath,
                request,
                cancellationToken);
            if (resource is null)
            {
                return Fail("Support access could not be stopped.");
            }

            CurrentSession = null;
            Notify();
            return SupportAccessCommandResult.Succeeded();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not stop support access.");
            return Fail("Support access could not be stopped.");
        }
        finally
        {
            IsStopping = false;
            Notify();
        }
    }

    public async Task<ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>> GetSessionsAsync(
        Guid targetTenantId,
        int limit = DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (targetTenantId == Guid.Empty)
        {
            return ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Failure(
                "Select a tenant before loading support access sessions.");
        }

        LastError = null;
        Notify();

        try
        {
            var collection = await bffClient.GetAsync<HalCollectionResourceOfSupportAccessSessionDto>(
                $"/bff/support-access/tenants/{targetTenantId:D}/sessions?limit={ClampLimit(limit)}",
                cancellationToken);

            return ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Success(
                collection ?? new HalCollectionResourceOfSupportAccessSessionDto());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load support-access sessions.");
            LastError = "Support access sessions are unavailable.";
            Notify();
            return ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Failure(LastError);
        }
    }

    public async Task<ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>> GetAuditEventsAsync(
        Guid targetTenantId,
        Guid sessionId,
        int limit = DefaultListLimit,
        CancellationToken cancellationToken = default)
    {
        if (targetTenantId == Guid.Empty || sessionId == Guid.Empty)
        {
            return ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>.Failure(
                "Select a support access session before loading audit events.");
        }

        LastError = null;
        Notify();

        try
        {
            var collection = await bffClient.GetAsync<HalCollectionResourceOfSupportAccessAuditEventDto>(
                $"/bff/support-access/tenants/{targetTenantId:D}/sessions/{sessionId:D}/audit-events?limit={ClampLimit(limit)}",
                cancellationToken);

            return ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>.Success(
                collection ?? new HalCollectionResourceOfSupportAccessAuditEventDto());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load support-access audit events.");
            LastError = "Support access audit events are unavailable.";
            Notify();
            return ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>.Failure(LastError);
        }
    }

    public async Task<SupportAccessCommandResult> ForceStopAsync(
        Guid sessionId,
        string? endReasonText = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return Fail("Select a support access session before force-stopping.");
        }

        LastError = null;
        Notify();

        try
        {
            var request = new ForceStopSupportAccessSessionRequestDto
            {
                EndReasonText = endReasonText
            };

            var resource = await bffClient.SendAsync<ForceStopSupportAccessSessionRequestDto, HalResourceOfSupportAccessSessionDto>(
                HttpMethod.Post,
                $"/bff/support-access/sessions/{sessionId:D}/force-stop",
                request,
                cancellationToken);

            if (resource is null)
            {
                return Fail("Support access could not be force-stopped.");
            }

            if (CurrentSession?.Id == sessionId)
            {
                CurrentSession = null;
                Notify();
            }

            return SupportAccessCommandResult.Succeeded();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not force-stop support access.");
            return Fail("Support access could not be force-stopped.");
        }
    }

    private SupportAccessCommandResult Fail(string errorMessage)
    {
        LastError = errorMessage;
        Notify();
        return SupportAccessCommandResult.Failed(errorMessage);
    }

    private void SetCurrentSession(SupportAccessSessionDto? session)
    {
        CurrentSession = session?.IsActive == true ? session : null;
        Notify();
    }

    private void Notify() => Changed?.Invoke();

    private static SupportAccessSessionDto? MapSession(HalResourceOfSupportAccessSessionDto? resource)
    {
        if (resource is null)
        {
            return null;
        }

        if (resource.Id is null || resource.TargetTenantId is null)
        {
            return MapSessionFromEnvelope(resource);
        }

        var session = new SupportAccessSessionDto
        {
            Id = resource.Id,
            ActorUserId = resource.ActorUserId,
            TargetTenantId = resource.TargetTenantId,
            TargetTenantUserId = resource.TargetTenantUserId,
            StatusId = resource.StatusId,
            StatusName = resource.StatusName,
            ModeId = resource.ModeId,
            ModeName = resource.ModeName,
            AllowsWrites = resource.AllowsWrites,
            ReasonCode = resource.ReasonCode,
            TicketReference = resource.TicketReference,
            ApprovedByUserId = resource.ApprovedByUserId,
            StartedAtUtc = resource.StartedAtUtc,
            ExpiresAtUtc = resource.ExpiresAtUtc,
            EndedAtUtc = resource.EndedAtUtc,
            EndReasonId = resource.EndReasonId,
            EndReasonName = resource.EndReasonName,
            IsActive = resource.IsActive
        };
        return IsIdentifiedSession(session) ? session : null;
    }

    private static SupportAccessSessionDto? MapSessionFromEnvelope(HalResourceOfSupportAccessSessionDto resource)
    {
        if (!resource.AdditionalProperties.TryGetValue("data", out var data) || data is null)
        {
            return null;
        }

        var session = data switch
        {
            JsonElement element => element.Deserialize<SupportAccessSessionDto>(JsonOptions),
            SupportAccessSessionDto typed => typed,
            _ => JsonSerializer.Deserialize<SupportAccessSessionDto>(
                JsonSerializer.Serialize(data, JsonOptions),
                JsonOptions)
        };

        return session is not null && IsIdentifiedSession(session)
            ? session
            : null;
    }

    private static bool IsIdentifiedSession(SupportAccessSessionDto session) =>
        session.Id.HasValue &&
        session.Id.Value != Guid.Empty &&
        session.TargetTenantId.HasValue &&
        session.TargetTenantId.Value != Guid.Empty;

    private static int ClampLimit(int limit) => Math.Clamp(limit <= 0 ? DefaultListLimit : limit, 1, 250);
}
