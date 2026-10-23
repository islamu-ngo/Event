using Explore.API.Configuration;
using Explore.API.Middleware;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class TenantRoutingScopeTests
{
    [Test]
    public async Task PreliminaryLookupDisposesItsLeaseBeforeBindingTheRequestTenant()
    {
        Guid tenantId = Guid.CreateVersion7();
        RoutingLease? preliminary = null;
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
        services.AddScoped(provider => new RoutingLease(provider.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<ITenantSlugCache>(provider =>
        {
            preliminary = provider.GetRequiredService<RoutingLease>();
            preliminary.Context.BindTenantLease();
            var cache = Substitute.For<ITenantSlugCache>();
            cache.GetTenantIdBySlugAsync("selected", Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromResult<Guid?>(tenantId));
            return cache;
        });
        await using var provider = services.BuildServiceProvider();
        await using var request = provider.CreateAsyncScope();
        var tenant = request.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var http = new DefaultHttpContext { RequestServices = request.ServiceProvider };
        http.Request.Path = "/api/events";
        http.Request.Headers[TenantHeaderNames.TenantSlug] = "selected";
        bool reached = false;
        var middleware = new ApiTenantResolutionMiddleware(async _ =>
        {
            await Assert.That(preliminary).IsNotNull();
            await Assert.That(preliminary!.Disposed).IsTrue();
            await Assert.That(tenant.TenantId).IsEqualTo(tenantId);
            var ordinary = request.ServiceProvider.GetRequiredService<RoutingLease>();
            await Assert.That(ReferenceEquals(ordinary, preliminary)).IsFalse();
            await Assert.That(ordinary.Context.BindTenantLease()).IsEqualTo(tenantId);
            reached = true;
        }, Options.Create(new DeploymentSettings { Mode = DeploymentMode.MultiTenant }));
        var mode = Substitute.For<IDeploymentModeProvider>();
        mode.IsSingleTenantAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        await middleware.InvokeAsync(http, tenant, Substitute.For<IProblemDetailsService>(),
            mode, Options.Create(new McpAdapterSettings()), provider.GetRequiredService<IServiceScopeFactory>());

        await Assert.That(reached).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExemptOrResolvedRequestsDoNotConstructRoutingDependencies(bool resolved)
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
        var tenant = new TenantContextAccessor(new HttpContextAccessor());
        if (resolved) tenant.SetTenant(Guid.CreateVersion7());
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Path = resolved ? "/api/events" : "/api/System/probe";
        bool reached = false;
        var middleware = new ApiTenantResolutionMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        }, Options.Create(new DeploymentSettings { Mode = DeploymentMode.MultiTenant }));

        await middleware.InvokeAsync(http, tenant, Substitute.For<IProblemDetailsService>(),
            Substitute.For<IDeploymentModeProvider>(), Options.Create(new McpAdapterSettings()),
            provider.GetRequiredService<IServiceScopeFactory>());

        await Assert.That(reached).IsTrue();
    }

    private sealed class RoutingLease : IDisposable
    {
        internal ExploreDbContext Context { get; }
        internal bool Disposed { get; private set; }

        internal RoutingLease(ITenantContextAccessor tenant)
        {
            var context = Substitute.For<ITenantContext>();
            context.TenantId.Returns(_ => tenant.TenantId ?? Guid.Empty);
            Context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
            {
                TenantContext = context
            };
        }

        public void Dispose()
        {
            Disposed = true;
            Context.Dispose();
        }
    }
}
