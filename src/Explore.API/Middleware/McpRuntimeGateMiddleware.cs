using Explore.API.Configuration;
using Explore.API.Mcp;
using Microsoft.Extensions.Options;

namespace Explore.API.Middleware;

public sealed class McpRuntimeGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpRuntimeGateMiddleware> _logger;

    public McpRuntimeGateMiddleware(RequestDelegate next, ILogger<McpRuntimeGateMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IOptions<McpAdapterSettings> options,
        IMcpRuntimeStateService runtimeStateService)
    {
        var settings = options.Value;
        if (!IsEnabledMcpPath(context, settings))
        {
            await _next(context);
            return;
        }

        var state = await runtimeStateService.GetAsync(cancellationToken: context.RequestAborted);
        if (state.EffectiveEnabled)
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "MCP request rejected because runtime governance disabled the adapter. StartupEnabled={StartupEnabled}, RuntimeEnabled={RuntimeEnabled}, TenantOverrideAllowed={TenantOverrideAllowed}.",
            state.StartupEnabled,
            state.RuntimeEnabled,
            state.TenantOverrideAllowed);

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static bool IsEnabledMcpPath(HttpContext context, McpAdapterSettings settings)
    {
        return settings.Enabled &&
               !string.IsNullOrWhiteSpace(settings.EndpointPath) &&
               context.Request.Path.StartsWithSegments(
                   settings.EndpointPath,
                   StringComparison.OrdinalIgnoreCase);
    }
}
