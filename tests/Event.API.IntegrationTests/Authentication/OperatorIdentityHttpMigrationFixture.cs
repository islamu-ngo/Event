using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// The three migrated operator HTTP suites retain real Local tickets, persisted
/// subject bindings and administrator grants. Observation clients start anonymous.
/// Identity preparation traverses the closed PUT command and its selected owner.
/// </summary>
internal sealed class OperatorIdentityHttpMigrationFixture : IAsyncDisposable
{
    internal LocalIdentityLifecycleHttpFixture Local { get; }
    internal WebApplicationFactory<Program> Host { get; }
    internal IServiceProvider Services => Host.Services;
    internal Guid Actor => Local.Binding.LocalSubjectId;

    private OperatorIdentityHttpMigrationFixture(
        LocalIdentityLifecycleHttpFixture local, WebApplicationFactory<Program> host)
    {
        Local = local;
        Host = host;
    }

    internal static async Task<OperatorIdentityHttpMigrationFixture> CreateAsync(
        bool administrator = true, IInterceptor? interceptor = null)
    {
        var local = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
        var host = local.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // These fixture-owned registrations do not substitute any source issuer,
            // human lifetime, selected execution owner or persistence implementation.
            services.AddScoped<IOperatorIdentityMutationAuthority, OperatorIdentityMutationAuthority>();
            services.AddScoped<IOperatorIdentitySettingsWriter, OperatorIdentitySettingsWriter>();
        }));
        var fixture = new OperatorIdentityHttpMigrationFixture(local, host);
        try
        {
            if (administrator) await fixture.GrantAdministratorAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal HttpClient CreateClient() => Host.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    internal async Task AuthorizeAsync(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", await Local.SignInAsync());
    }

    internal async Task<Guid> GrantAdministratorAsync()
    {
        await using var seed = Local.Native.CreateDatabase();
        int roleId = await seed.Roles.Where(role => role.MasterCode == "platform.admin"
            && role.RoleScopeId == (int)RoleScopeEnum.Platform).Select(role => role.Id).SingleAsync();
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = Actor,
            User = null!,
            RoleId = roleId,
            Role = null!,
            GrantedAt = Local.Clock.GetUtcNow().UtcDateTime
        };
        seed.PlatformUserRoles.Add(grant);
        await seed.SaveChangesAsync();
        return grant.Id;
    }

    internal async Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>> SaveAsync(
        SaveInstanceOperatorIdentityRequestDto request)
    {
        using var client = CreateClient();
        await AuthorizeAsync(client);
        using var response = await client.PutAsJsonAsync("/api/instance-operator-identity", request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content
            .ReadFromJsonAsync<BaseCommandResponse<InstanceOperatorIdentitySavedDocumentDto>>())!;
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Local.DisposeAsync();
    }
}
