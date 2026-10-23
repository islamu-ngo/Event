using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Storage;
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
        if (change != "missing-administrator")
        {
            await using var seed = fixture.Native.CreateDatabase();
            int role = await seed.Roles.Where(row => row.MasterCode == "platform.admin"
                && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = role,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddStorageProbeOriginalAction();
            services.AddStorageProbeSourceOwner();
        }));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        if (change == "principal-replaced")
            request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
        var query = new TestInstanceStorageProviderQuery
        {
            UserId = change == "empty-actor" ? Guid.Empty
                : change == "wrong-actor" ? Guid.CreateVersion7() : fixture.Binding.LocalSubjectId
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selected.Services
            .GetRequiredService<IStorageProbeOriginalAction>().ExecuteAsync(query, CancellationToken.None));
    }

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
                throw new InvalidOperationException("The real storage probe request did not authenticate.");
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
