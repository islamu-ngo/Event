using Explore.API.Middleware;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class AdmissionRequestLoggingTests
{
    [Test]
    public async Task AdmissionRequestLogUsesNormalizedRouteIdentityWithoutSensitiveIdentifiers()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid checkInId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        const string tenantSlug = "private-masjid";
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        tenant.TenantId.Returns(tenantId);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/events/{eventId:D}/admission/check-ins/{checkInId:D}/undo";
        context.Request.Headers["X-Tenant-Slug"] = tenantSlug;
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim("sub", actorId.ToString("D"))],
                "test"));
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(
                "api/events/{eventId:guid}/admission/check-ins/{checkInId:guid}/undo"),
            0,
            EndpointMetadataCollection.Empty,
            "admission undo"));

        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(context, tenant);

        await Assert.That(logger.Messages).HasSingleItem();
        string log = logger.Messages.Single();
        await Assert.That(log).Contains(
            "/api/events/{eventid:guid}/admission/check-ins/{checkinid:guid}/undo");
        await Assert.That(log).DoesNotContain(eventId.ToString("D"));
        await Assert.That(log).DoesNotContain(checkInId.ToString("D"));
        await Assert.That(log).DoesNotContain(actorId.ToString("D"));
        await Assert.That(log).DoesNotContain(tenantId.ToString("D"));
        await Assert.That(log).DoesNotContain(tenantSlug);
    }

    [Test]
    public async Task OrdinaryRequestLogRetainsItsConcreteNonSensitivePath()
    {
        const string path = "/api/category";
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/category"),
            0,
            EndpointMetadataCollection.Empty,
            "category list"));

        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(context, tenant);

        await Assert.That(logger.Fields).Contains(new KeyValuePair<string, string?>("Path", path));
    }

    [Test]
    public async Task OrdinaryRequestLogContainsOnlyBoundedIdentityAndTenantPresence()
    {
        Guid subject = Guid.CreateVersion7();
        Guid tenantId = Guid.CreateVersion7();
        const string tenantSlug = "private-tenant-value";
        const string opaqueSubject = "opaque-provider-subject";
        var logger = new RecordingLogger<RequestLoggingMiddleware>();
        var tenant = Substitute.For<ITenantContextAccessor>();
        tenant.TenantId.Returns(tenantId);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/category";
        context.Request.Headers[Explore.Application.Constants.TenantHeaderNames.TenantSlug] = tenantSlug;
        context.Request.Headers.Authorization = "Bearer redacted-token";
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity([
                new System.Security.Claims.Claim("sub", subject.ToString("D")),
                new System.Security.Claims.Claim("provider_subject", opaqueSubject),
                new System.Security.Claims.Claim(PlatformIdentityClaimTypes.InternalUserId, Guid.CreateVersion7().ToString("D"))
            ], "interactive"));

        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(context, tenant);

        string log = logger.Messages.Single();
        await Assert.That(log).DoesNotContain(subject.ToString("D"));
        await Assert.That(log).DoesNotContain(tenantId.ToString("D"));
        await Assert.That(log).DoesNotContain(tenantSlug);
        await Assert.That(log).DoesNotContain(opaqueSubject);
        await Assert.That(log).DoesNotContain("redacted-token");
        await Assert.That(logger.Fields).Contains(new KeyValuePair<string, string?>("PlatformIdentityPresent", "True"));
        await Assert.That(logger.Fields).Contains(new KeyValuePair<string, string?>("TenantPresent", "True"));
        await Assert.That(logger.Fields).Contains(new KeyValuePair<string, string?>("TenantSlugPresent", "True"));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        internal List<string> Messages { get; } = [];
        internal List<KeyValuePair<string, string?>> Fields { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> fields)
                Fields.AddRange(fields.Select(field => new KeyValuePair<string, string?>(
                    field.Key,
                    Convert.ToString(field.Value, System.Globalization.CultureInfo.InvariantCulture))));
        }
    }
}
