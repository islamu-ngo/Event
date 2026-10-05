using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventDiscoveryTraversalTests
{
    [Test]
    public async Task SourceMutationAfterHalAssemblyCannotReleaseOldCursorPage()
    {
        var boundary = new HalAssemblyBoundary();
        await using var factory = new NativeEventTagsFactory(relational: true);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IResourceAssembler<EventDiscoveryItemDto>>();
            services.AddScoped<IResourceAssembler<EventDiscoveryItemDto>>(provider =>
                new PausedDiscoveryAssembler(
                    ActivatorUtilities.CreateInstance<EventDiscoveryResourceAssembler>(provider), boundary));
        }));
        using var client = host.CreateClient();
        var seed = await SeedAsync(host);
        string next = await NextAsync(client, Route(seed.Title));

        // Subscribe before triggering continuation; the signal follows real HAL assembly.
        Task entered = boundary.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30),
            TestContext.Current!.Execution.CancellationToken);
        boundary.Arm();
        Task<HttpResponseMessage> pending = client.GetAsync(next);
        try
        {
            await entered;
            await using var scope = host.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var session = await context.EventSessions.SingleAsync(value => value.Id == seed.LastSessionId);
            session.IsDeleted = true;
            await context.SaveChangesAsync();
        }
        finally
        {
            boundary.Release.TrySetResult();
        }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30),
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertFailureAsync(response, "discovery_restart_required");
    }

    private sealed class HalAssemblyBoundary
    {
        private int _armed;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public async Task PauseAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _armed, 0) == 0)
                return;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
    }

    private sealed class PausedDiscoveryAssembler(
        IResourceAssembler<EventDiscoveryItemDto> inner, HalAssemblyBoundary boundary)
        : IResourceAssembler<EventDiscoveryItemDto>
    {
        public Task<HalResource<EventDiscoveryItemDto>> ToResource(
            EventDiscoveryItemDto dto, HttpContext httpContext) => inner.ToResource(dto, httpContext);

        public Task<HalResource<EventDiscoveryItemDto>> ToListResource(
            EventDiscoveryItemDto dto, HttpContext httpContext) => inner.ToListResource(dto, httpContext);

        public Task<HalCollectionResource<EventDiscoveryItemDto>> ToCollectionResource(
            PaginatedResult<EventDiscoveryItemDto> result, string routeName,
            object? additionalRouteValues, HttpContext httpContext) =>
            inner.ToCollectionResource(result, routeName, additionalRouteValues, httpContext);

        public Task<HalCollectionResource<EventDiscoveryItemDto>> ToCollectionResource(
            IEnumerable<EventDiscoveryItemDto> items, string routeName,
            object? additionalRouteValues, HttpContext httpContext) =>
            inner.ToCollectionResource(items, routeName, additionalRouteValues, httpContext);

        public async Task<HalCollectionResource<EventDiscoveryItemDto>> ToCollectionResource(
            IEnumerable<EventDiscoveryItemDto> items, string routeName, HttpContext httpContext)
        {
            var resource = await inner.ToCollectionResource(items, routeName, httpContext);
            await boundary.PauseAsync(httpContext.RequestAborted);
            return resource;
        }
    }
}
