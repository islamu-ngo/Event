using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Routing;

namespace Explore.API.Middleware;

public sealed class SupportAccessAuditMiddleware(
    RequestDelegate next,
    ILogger<SupportAccessAuditMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        ISupportAccessSessionService supportAccessSessionService,
        ISupportAccessAuditEventRepository auditEventRepository,
        BusinessMetrics metrics)
    {
        await next(context);

        if (!ShouldAudit(context))
        {
            return;
        }

        var metricEventType = "unknown";
        var metricOutcome = "unknown";
        try
        {
            var supportContext = await supportAccessSessionService.GetCurrentAsync(CancellationToken.None);
            if (!supportContext.IsActive ||
                !supportContext.SessionId.HasValue ||
                !supportContext.ActorUserId.HasValue ||
                !supportContext.TargetTenantId.HasValue)
            {
                return;
            }

            var eventType = IsUnsafeMethod(context.Request.Method)
                ? SupportAccessAuditEventTypeEnum.CommandCommitted
                : SupportAccessAuditEventTypeEnum.RequestObserved;
            metricEventType = eventType.ToString();
            metricOutcome = ResolveOutcome(context.Response.StatusCode);
            AddActiveSupportAccessTraceTags(context, supportContext, metricEventType, metricOutcome);

            var auditEvent = SupportAccessAuditEvent.Create(
                supportContext.SessionId.Value,
                eventType,
                supportContext.ActorUserId.Value,
                supportContext.TargetTenantId.Value,
                metricOutcome,
                DateTimeOffset.UtcNow,
                supportContext.TargetTenantUserId,
                routeName: ResolveRouteName(context),
                requestName: ResolveRequestName(context),
                action: context.Request.Method,
                httpStatusCode: context.Response.StatusCode,
                correlationId: context.Items["CorrelationId"] as string,
                traceId: System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                sanitizedMetadataJson: BuildMetadataJson(context));

            await auditEventRepository.CreateAsync(auditEvent, CancellationToken.None);
            metrics.RecordSupportAccessRequestAudit(metricEventType, metricOutcome, "persisted");
        }
        catch (Exception ex)
        {
            metrics.RecordSupportAccessRequestAudit(
                metricEventType,
                metricOutcome,
                "failed",
                "support_access_audit_persistence_failed");
            System.Diagnostics.Activity.Current?.SetTag("support_access.audit.persistence_outcome", "failed");
            logger.LogWarning(
                ex,
                "Could not persist support-access request audit event for request={RequestName} statusCode={StatusCode}.",
                ResolveRequestName(context),
                context.Response.StatusCode);
        }
    }

    private static bool ShouldAudit(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && context.GetEndpoint() is not null;
    }

    private static bool IsUnsafeMethod(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }

    private static string ResolveOutcome(int statusCode)
    {
        return statusCode switch
        {
            >= 200 and < 400 => "success",
            >= 400 and < 500 => "client_error",
            >= 500 => "server_error",
            _ => "observed"
        };
    }

    private static string? ResolveRouteName(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? (endpoint as RouteEndpoint)?.RoutePattern.RawText
            ?? endpoint?.DisplayName;
    }

    private static string ResolveRequestName(HttpContext context)
    {
        var pattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        return string.IsNullOrWhiteSpace(pattern)
            ? context.Request.Method
            : $"{context.Request.Method} {pattern}";
    }

    private static string BuildMetadataJson(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var payload = new
        {
            method = context.Request.Method,
            routePattern = (endpoint as RouteEndpoint)?.RoutePattern.RawText,
            endpoint = endpoint?.DisplayName,
            statusCode = context.Response.StatusCode
        };

        return JsonSerializer.Serialize(payload);
    }

    private static void AddActiveSupportAccessTraceTags(
        HttpContext context,
        ISupportAccessContext supportContext,
        string eventType,
        string outcome)
    {
        var activity = System.Diagnostics.Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.SetTag("support_access.active", true);
        activity.SetTag("support_access.mode", supportContext.Mode?.ToString() ?? "unknown");
        activity.SetTag("support_access.allows_writes", supportContext.AllowsWrites);
        activity.SetTag("support_access.request.event_type", eventType);
        activity.SetTag("support_access.request.outcome", outcome);
        activity.SetTag("support_access.request.method", context.Request.Method);
    }
}
