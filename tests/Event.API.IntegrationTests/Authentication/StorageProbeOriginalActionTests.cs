using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageProbeOriginalActionTests
{
    [Test]
    [Arguments("wrong-actor")]
    [Arguments("empty-actor")]
    [Arguments("missing-administrator")]
    [Arguments("principal-replaced")]
    public async Task ActualProbeRejectsSubstitutedOriginalHuman(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = CreateProbeDatabase(fixture.Native))
        {
            if (change != "missing-administrator")
                await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        remote.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        remote.AfterVersioning = () =>
        {
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            return Task.CompletedTask;
        };
        var query = new TestInstanceStorageProviderQuery
        {
            UserId = change == "empty-actor" ? Guid.Empty
                : change == "wrong-actor" ? Guid.CreateVersion7() : fixture.Binding.LocalSubjectId
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selected.Services
            .GetRequiredService<IStorageProbeOriginalAction>().ExecuteAsync(query, CancellationToken.None));
        await Assert.That(remote.Puts).IsEqualTo(0);
        await Assert.That(remote.Deletes).IsEqualTo(0);
    }

    private static ExploreDbContext CreateProbeDatabase(LocalAdmissionWebApplicationFactory native, Guid? tenantId = null)
    {
        var database = native.CreateDatabase();
        if (tenantId is { } selectedTenant)
            database.TenantContext = new ProbeTenant(selectedTenant);
        database.BindTenantLease();
        return database;
    }

    private sealed record ProbeTenant(Guid TenantId) : ITenantContext;

    private sealed class ProbeRequest(AsyncServiceScope scope, IHttpContextAccessor accessor, DefaultHttpContext context)
        : IAsyncDisposable
    {
        internal IServiceProvider Services { get { accessor.HttpContext = context; return scope.ServiceProvider; } }
        internal DefaultHttpContext Context => context;

        internal static async Task<ProbeRequest> CreateAsync(IServiceProvider services, string bearer, string? secret = null)
        {
            var scope = services.CreateAsyncScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/instance/settings/storage/test";
            context.Request.Headers.Authorization = "Bearer " + bearer;
            if (secret is not null) context.Request.Headers["X-Setup-Secret"] = secret;
            accessor.HttpContext = context;
            var authentication = await context.AuthenticateAsync();
            if (!authentication.Succeeded)
            {
                accessor.HttpContext = null;
                await scope.DisposeAsync();
                throw new InvalidOperationException(
                    $"The real storage probe request did not authenticate: {authentication.Failure?.GetType().Name ?? "NoResult"}.");
            }
            context.User = authentication.Principal!;
            return new(scope, accessor, context);
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
