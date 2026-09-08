using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.SupportAccess;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Application.SupportAccess;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.SupportAccess;

public sealed class SupportAccessSessionService(
    IHttpContextAccessor httpContextAccessor,
    IAdminContext adminContext,
    ITenantContextAccessor tenantContextAccessor,
    IHierarchicalSettingsResolver settingsResolver,
    ISupportAccessSessionRepository sessionRepository,
    BusinessMetrics? metrics = null,
    ILogger<SupportAccessSessionService>? logger = null)
    : ISupportAccessSessionService
{
    private const string CachedContextItemKey = "__support_access_context";

    public async Task<ISupportAccessContext> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue(CachedContextItemKey, out var cached) == true &&
            cached is ISupportAccessContext cachedContext)
        {
            return cachedContext;
        }

        var wasForwarded = HasForwardedSessionHeader(httpContext);
        var forwardedSessionId = TryReadForwardedSessionId(httpContext);
        var actorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actorUserId.HasValue)
        {
            return Cache(
                httpContext,
                wasForwarded
                    ? SupportAccessContext.InactiveForwarded
                    : SupportAccessContext.Inactive);
        }

        var resolvedTenantId = tenantContextAccessor.TenantId;
        ISupportAccessContext context;
        if (forwardedSessionId.HasValue)
        {
            context = await ValidateForwardedSessionAsync(
                forwardedSessionId.Value,
                actorUserId.Value,
                resolvedTenantId,
                cancellationToken);
        }
        else
        {
            context = wasForwarded
                ? SupportAccessContext.InactiveForwarded
                : SupportAccessContext.Inactive;
        }

        return Cache(httpContext, context);
    }

    public async Task<ISupportAccessContext> ValidateForwardedSessionAsync(
        Guid sessionId,
        Guid actorUserId,
        Guid? resolvedTenantId,
        CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty || actorUserId == Guid.Empty)
        {
            return SupportAccessContext.InactiveForwarded;
        }

        var settings = await settingsResolver.ResolveGroupAsync<SupportAccessSettingGroup>(
            new SettingContext(),
            cancellationToken);
        if (!settings.Enabled)
        {
            metrics?.RecordSupportAccessSessionValidationDenial(SupportAccessFailureCodes.Disabled, null);
            logger?.LogWarning(
                "Forwarded support-access session denied because support access is disabled sessionId={SupportAccessSessionId} actorUserId={ActorUserId} resolvedTenantId={ResolvedTenantId}",
                sessionId,
                actorUserId,
                resolvedTenantId);
            return SupportAccessContext.InactiveForwarded;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var session = await sessionRepository.GetActiveOwnedSessionAsync(
            sessionId,
            actorUserId,
            resolvedTenantId,
            nowUtc,
            cancellationToken);

        return BuildContext(session, settings, nowUtc);
    }

    private ISupportAccessContext BuildContext(
        SupportAccessSession? session,
        SupportAccessSettingGroup settings,
        DateTimeOffset nowUtc)
    {
        if (session is null || !session.IsActiveAt(nowUtc))
        {
            metrics?.RecordSupportAccessSessionValidationDenial("support_access_inactive", null);
            logger?.LogWarning("Forwarded support-access session denied because no active matching session was found.");
            return SupportAccessContext.InactiveForwarded;
        }

        var mode = (SupportAccessModeEnum)session.ModeId;
        if (mode == SupportAccessModeEnum.Write && !settings.AllowWriteMode)
        {
            metrics?.RecordSupportAccessSessionValidationDenial(
                SupportAccessFailureCodes.WriteModeDisabled,
                mode.ToString());
            logger?.LogWarning(
                "Forwarded write-capable support-access session denied because write mode is disabled sessionId={SupportAccessSessionId} actorUserId={ActorUserId} targetTenantId={TargetTenantId}",
                session.Id,
                session.ActorUserId,
                session.TargetTenantId);
            return SupportAccessContext.InactiveForwarded;
        }

        return new SupportAccessContext(
            true,
            session.Id,
            session.ActorUserId,
            session.TargetTenantId,
            session.TargetTenantUserId,
            mode,
            session.StartedAtUtc,
            session.ExpiresAtUtc,
            session.ReasonCode,
            session.TicketReference,
            WasForwarded: true);
    }

    private static Guid? TryReadForwardedSessionId(HttpContext? httpContext)
    {
        if (httpContext is null ||
            !httpContext.Request.Headers.TryGetValue(SupportAccessHeaderNames.SessionId, out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return Guid.TryParse(value, out var sessionId) && sessionId != Guid.Empty ? sessionId : null;
    }

    private static bool HasForwardedSessionHeader(HttpContext? httpContext) =>
        httpContext is not null &&
        httpContext.Request.Headers.ContainsKey(SupportAccessHeaderNames.SessionId);

    private static ISupportAccessContext Cache(HttpContext? httpContext, ISupportAccessContext context)
    {
        if (httpContext is not null)
        {
            httpContext.Items[CachedContextItemKey] = context;
        }

        return context;
    }
}
