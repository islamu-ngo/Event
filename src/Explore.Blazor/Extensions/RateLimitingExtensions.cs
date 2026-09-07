using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Event.Web.BffHosting.Security;
using System.Threading.RateLimiting;

using Explore.Blazor.Services;


namespace Explore.Blazor.Extensions;

public static class RateLimitingExtensions
{
    public const string SetupSecretPolicy = "BffSetupSecret";
    public const string AtprotoAuthenticationPolicy = "BffAtprotoAuthentication";
    public const string LocalAuthenticationPolicy = "BffLocalAuthentication";
    public const string RegistrationPaymentCheckoutIssuePolicy = "BffRegistrationPaymentCheckoutIssue";
    public const string TicketPurchaseAuthorityPolicy =
        "BffTicketPurchaseAuthority";
    public const string ParticipantReadinessWritePolicy =
        "BffParticipantReadinessWrite";
    public const string TicketTransferWritePolicy =
        "BffTicketTransferWrite";

    public static IServiceCollection AddBffRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var disableInTesting = configuration.GetValue("RateLimiting:DisableInTesting", true);
        if (environment.EnvironmentName == "Testing" && disableInTesting)
        {
            services.AddRateLimiter(options =>
            {
                options.AddPolicy(SetupSecretPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("test"));
                options.AddPolicy(AtprotoAuthenticationPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("test"));
                options.AddPolicy(LocalAuthenticationPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("test"));
                options.AddPolicy(RegistrationPaymentCheckoutIssuePolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("test"));
                options.AddPolicy(TicketPurchaseAuthorityPolicy, _ =>
                    RateLimitPartition.GetNoLimiter<string>("test"));
                options.AddPolicy(
                    ParticipantReadinessWritePolicy,
                    _ => RateLimitPartition
                        .GetNoLimiter<string>("test"));
                options.AddPolicy(
                    TicketTransferWritePolicy,
                    _ => RateLimitPartition
                        .GetNoLimiter<string>("test"));
            });

            return services;
        }

        var section = configuration.GetSection("RateLimiting:SetupSecret");
        var permitLimit = section.GetValue("PermitLimit", 5);
        var windowSeconds = section.GetValue("WindowSeconds", 60);
        var atprotoSection = configuration.GetSection("RateLimiting:AtprotoAuthentication");
        var atprotoPermitLimit = Math.Clamp(atprotoSection.GetValue("PermitLimit", 10), 1, 1000);
        var atprotoWindowSeconds = Math.Clamp(atprotoSection.GetValue("WindowSeconds", 60), 1, 3600);
        var localSection = configuration.GetSection("RateLimiting:LocalAuthentication");
        var localPermitLimit = Math.Clamp(localSection.GetValue("PermitLimit", 10), 1, 100);
        var localWindowSeconds = Math.Clamp(localSection.GetValue("WindowSeconds", 60), 1, 3600);
        var checkoutSection = configuration.GetSection("RateLimiting:RegistrationPaymentCheckoutIssue");
        var checkoutPermitLimit = Math.Clamp(checkoutSection.GetValue("PermitLimit", 10), 1, 100);
        var checkoutWindowSeconds = Math.Clamp(checkoutSection.GetValue("WindowSeconds", 60), 1, 3600);
        var purchaseSection =
            configuration.GetSection(
                "RateLimiting:TicketPurchaseAuthority");
        var purchasePermitLimit = Math.Clamp(
            purchaseSection.GetValue("PermitLimit", 10),
            1,
            100);
        var purchaseWindowSeconds = Math.Clamp(
            purchaseSection.GetValue("WindowSeconds", 60),
            1,
            3600);
        var readinessSection = configuration.GetSection(
            "RateLimiting:ParticipantReadinessWrite");
        var readinessPermitLimit = Math.Clamp(
            readinessSection.GetValue("PermitLimit", 20),
            1,
            100);
        var readinessWindowSeconds = Math.Clamp(
            readinessSection.GetValue("WindowSeconds", 60),
            1,
            3600);
        var transferSection = configuration.GetSection(
            "RateLimiting:TicketTransferWrite");
        var transferPermitLimit = Math.Clamp(
            transferSection.GetValue("PermitLimit", 10),
            1,
            100);
        var transferWindowSeconds = Math.Clamp(
            transferSection.GetValue("WindowSeconds", 60),
            1,
            3600);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
                }

                var isSetupSecret = context.HttpContext.Request.Path.StartsWithSegments(
                    "/bff/setup-secret",
                    StringComparison.Ordinal);
                await context.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc6585#section-4",
                    Title = "Too Many Requests",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = isSetupSecret
                        ? "Too many setup-secret attempts. Please retry after the period indicated in the Retry-After header."
                        : context.HttpContext.Request.Path.Value?.Contains("/registration-payments/", StringComparison.Ordinal) == true
                            ? "Too many checkout attempts. Please retry after the period indicated in the Retry-After header."
                            : "Too many authentication attempts. Please retry after the period indicated in the Retry-After header."
                }, cancellationToken);
            };

            options.AddPolicy(SetupSecretPolicy, httpContext =>
            {
                var partitionKey = ResolveSetupSecretPartitionKey(httpContext);

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.AddPolicy(AtprotoAuthenticationPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"atproto:ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = atprotoPermitLimit,
                        Window = TimeSpan.FromSeconds(atprotoWindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(LocalAuthenticationPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"local-auth:ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = localPermitLimit,
                        Window = TimeSpan.FromSeconds(localWindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(RegistrationPaymentCheckoutIssuePolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolveCheckoutPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = checkoutPermitLimit,
                        Window = TimeSpan.FromSeconds(checkoutWindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(
                TicketPurchaseAuthorityPolicy,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ResolveCheckoutPartitionKey(
                            httpContext),
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    purchasePermitLimit,
                                Window = TimeSpan.FromSeconds(
                                    purchaseWindowSeconds),
                                QueueProcessingOrder =
                                    QueueProcessingOrder
                                        .OldestFirst,
                                QueueLimit = 0,
                                AutoReplenishment = true,
                            }));
            options.AddPolicy(
                ParticipantReadinessWritePolicy,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ResolveCheckoutPartitionKey(
                            httpContext),
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    readinessPermitLimit,
                                Window = TimeSpan.FromSeconds(
                                    readinessWindowSeconds),
                                QueueProcessingOrder =
                                    QueueProcessingOrder
                                        .OldestFirst,
                                QueueLimit = 0,
                                AutoReplenishment = true,
                            }));
            options.AddPolicy(
                TicketTransferWritePolicy,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        ResolveCheckoutPartitionKey(
                            httpContext),
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit =
                                    transferPermitLimit,
                                Window = TimeSpan.FromSeconds(
                                    transferWindowSeconds),
                                QueueProcessingOrder =
                                    QueueProcessingOrder
                                        .OldestFirst,
                                QueueLimit = 0,
                                AutoReplenishment = true,
                            }));
        });

        return services;
    }

    private static string ResolveSetupSecretPartitionKey(HttpContext context)
    {
        if (context.User.TryGetRatePartitionIdentity(out var userId))
        {
            return $"setup:user:{userId.PartitionKey}";
        }

        return $"setup:ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }

    private static string ResolveCheckoutPartitionKey(HttpContext context)
    {
        if (context.User.TryGetRatePartitionIdentity(out var userId))
        {
            return $"checkout:user:{Digest(userId.PartitionKey)}";
        }

        string tenant = context.Items[TenantRouteContextAccessor.TenantSlugItemKey]?.ToString() ?? string.Empty;
        string remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"checkout:anonymous:{remoteIp}:{Digest(tenant)}";
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
