using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeCustomPropertyGovernanceHttpTests
{
    [Test]
    public async Task NativeQuery_NormalizesPagingAndRejectsForeignAndNonadminBeforeRepositoryExecution()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            SetPrincipal(scope, data.AdminId);
            var report = await Query(scope).QueryAsync(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                Filter = new() { PageNumber = -2, PageSize = 500 }
            }, default);
            await Assert.That(report.PageNumber).IsEqualTo(1);
            await Assert.That(report.PageSize).IsEqualTo(100);
            await Assert.That(report.TotalCount).IsEqualTo(4);
            await Assert.That(report.Items.Count).IsEqualTo(4);
        }
        factory.Reads.Fail = true;
        foreach (var userId in new[] { data.MemberId, data.ForeignAdminId })
        {
            using var scope = factory.Services.CreateScope();
            SetPrincipal(scope, userId);
            await Assert.That(async () => await Query(scope).QueryAsync(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId
            }, default)).Throws<AuthorizationException>();
        }
        using (var scope = factory.Services.CreateScope())
        {
            SetPrincipal(scope, data.AdminId);
            await Assert.That(async () => await Query(scope).QueryAsync(new()
            {
                TenantId = data.ForeignTenantId
            }, default)).Throws<AuthorizationException>();
        }
        await Assert.That(factory.Reads.Entered.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task PersistedInstanceAdminCanReadAmbientReportButCannotSelectAForeignTenant()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        Guid instanceId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var instance = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            instanceId = instance.UserId;
            var role = await db.Set<Role>().SingleAsync(row => row.MasterCode == "platform.admin");
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = instanceId,
                User = null!,
                RoleId = role.Id,
                Role = role
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(factory, instanceId);
        await Assert.That((await ReportAsync(client)).TotalCount).IsEqualTo(4);
        using var foreign = await client.GetAsync(Root + $"?tenantId={data.ForeignTenantId}");
        await ProblemAsync(foreign, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task RevokedPersistedGrantDeniesTheNextHttpRequestDespiteUnchangedAdminClaims()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        await Assert.That((await ReportAsync(client)).TotalCount).IsEqualTo(4);
        Guid grantId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            grantId = await db.TenantUserRoleGrants.Where(row => row.TenantUser.UserId == data.AdminId)
                .Select(row => row.Id).SingleAsync();
        }
        using (var revoked = await client.DeleteAsync($"/api/tenant-user-role-grants/{grantId}"))
            await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var response = await client.GetAsync(Url());
        await ProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task RepositoryFailureRemainsAProblemNotASuccessfulEmptyReport()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        factory.Reads.Fail = true;
        using var response = await client.GetAsync(Url());
        await ProblemAsync(response, HttpStatusCode.InternalServerError);
        using var scope = factory.Services.CreateScope();
        SetPrincipal(scope, data.AdminId);
        await Assert.That(async () => await Query(scope).QueryAsync(new()
        {
            TenantId = PlatformDefaults.DefaultTenantId
        }, default)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NativeCancellationReachesTheActualRepositoryRead()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        SetPrincipal(scope, data.AdminId);
        factory.Reads.Block = true;
        using var cancellation = new CancellationTokenSource();
        var entered = factory.Reads.Entered.Task;
        var pending = Query(scope).QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId }, cancellation.Token);
        try
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await cancellation.CancelAsync();
        }
        await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10))).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task ControllerUsesOnlyTheClosedQueryPortAndPreservesItsPublishedContract()
    {
        await Assert.That(typeof(CustomPropertyGovernanceController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType)).IsEquivalentTo(new[]
        {
            typeof(IQueryHandler<GetCustomPropertyGovernanceReportQuery, PaginatedResult<CustomPropertyGovernanceRowDto>>)
        });
        await Assert.That(typeof(CustomPropertyGovernanceController).GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var operation = document.RootElement.GetProperty("paths").GetProperty(Root).GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo("GetCustomPropertyGovernanceReport");
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Authenticated");
        await Assert.That(operation.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()))
            .Contains("TenantId");
    }

    private static IQueryHandler<GetCustomPropertyGovernanceReportQuery, PaginatedResult<CustomPropertyGovernanceRowDto>> Query(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyGovernanceReportQuery, PaginatedResult<CustomPropertyGovernanceRowDto>>>();

    private static void SetPrincipal(IServiceScope scope, Guid userId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("internal_user_id", userId.ToString())], "Test"))
        };
    }
}
