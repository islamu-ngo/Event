using System.Net.Http.Headers;
using System.Net.Http.Json;
using Explore.Application.DTOs.Tenant;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    private List<SanitizedRequestFailure> tenantCreationFailures = [];

    internal static async Task<AuthenticatedSettingsFixture> CreateForTenantCreationApiAsync(
        Func<ExploreDbContext> createDatabase, PostgreSqlContainerFixture postgres, IInterceptor? interceptor = null,
        Action<IServiceCollection>? configureServices = null,
        DeploymentMode deploymentMode = DeploymentMode.SingleTenant)
    {
        await using (var migrator = createDatabase())
            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, postgres.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var failures = new List<SanitizedRequestFailure>();
        var fixture = await CreateAsync(createDatabase, postgres, interceptor, requestFailureObserver: failures.Add,
            configureServices: configureServices, deploymentMode: deploymentMode);
        fixture.tenantCreationFailures = failures;
        return fixture;
    }

    internal string TenantCreationFailureDescription => tenantCreationFailures.LastOrDefault() is { } failure
        ? $"Native HTTP failure types: {string.Join(" -> ", failure.ExceptionTypes)}; "
            + $"SQLSTATE: {string.Join(", ", failure.SqlStates)}; methods: "
            + string.Join(" -> ", failure.Methods.OrderBy(method =>
                method.StartsWith("Explore.", StringComparison.Ordinal) ? 0 : 1))
        : "No server exception was captured for this native tenant-creation request.";

    internal bool ObservedTenantCreationFailure<TException>() where TException : Exception =>
        tenantCreationFailures.Any(failure => failure.ExceptionTypes.Contains(typeof(TException).FullName!));

    internal void ObserveCurrentTenantCreationRequest(Action<HttpContext> observation)
    {
        var context = host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException("A live tenant-creation request is required for the test observation.");
        observation(context);
    }

    /// <summary>Posts through the real API using only this shared fixture's completed Local login.</summary>
    internal async Task<HttpResponseMessage> CreateTenantViaApiAsync(
        CreateTenantDto tenant, CancellationToken cancellationToken, bool authenticate = true)
    {
        tenantCreationFailures.Clear();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Tenant")
        {
            Content = JsonContent.Create(tenant)
        };
        if (authenticate)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, cancellationToken);
    }

    internal async Task<HttpResponseMessage> ProbeTenantCreationRoutingAsync(
        HttpMethod method, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, cancellationToken);
    }
}
