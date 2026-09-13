using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyDefinitions.Authorization;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class CustomPropertyDefinitionPrerequisiteTests
{
    [Test]
    public async Task ControllerAndHost_ExposeExactlySixScopedProtectedPortsAndRequiredUpdateEnricher()
    {
        Type[] ports =
        [
            typeof(ICommandHandler<CreateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<DeleteCustomPropertyDefinitionCommand, bool>),
            typeof(ICommandHandler<PurgeCustomPropertyDefinitionCommand, BaseCommandResponse<CustomPropertyPurgeResultDto>>),
            typeof(IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>),
            typeof(IQueryHandler<GetCustomPropertyDefinitionListQuery, PaginatedResult<CustomPropertyDefinitionListDto>>)
        ];
        await Assert.That(typeof(CustomPropertyDefinitionController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray()).IsEquivalentTo(
                ports.Append(typeof(IResourceAssembler<CustomPropertyDefinitionDto, CustomPropertyDefinitionListDto>)).ToArray());
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        using var first = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        using var second = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        foreach (var port in ports)
        {
            var handler = first.ServiceProvider.GetRequiredService(port);
            var expected = port.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                ? typeof(AuthorizationCommandHandlerDecorator<,>) : typeof(AuthorizationQueryHandlerDecorator<,>);
            await Assert.That(handler.GetType().GetGenericTypeDefinition()).IsEqualTo(expected);
            await Assert.That(ReferenceEquals(handler, first.ServiceProvider.GetRequiredService(port))).IsTrue();
            await Assert.That(ReferenceEquals(handler, second.ServiceProvider.GetRequiredService(port))).IsFalse();
        }
        await Assert.That(first.ServiceProvider.GetRequiredService<IAuthorizationContextEnricher<UpdateCustomPropertyDefinitionCommand>>())
            .IsTypeOf<UpdateCustomPropertyDefinitionAuthorizationContextEnricher>();
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    public async Task NativeUpdate_UsesPersistedTenantEnrichmentRatherThanForgedRequestTenant()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        SetNativePrincipal(scope, data.UserId);
        var detail = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>>();
        var before = await detail.QueryAsync(new(data.OwnDefinitionId), default);
        var update = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>();
        var result = await update.ExecuteAsync(new UpdateCustomPropertyDefinitionCommand
        {
            DefinitionId = data.OwnDefinitionId, TenantId = data.ForeignTenantId,
            ExpectedConcurrencyStamp = before.ConcurrencyStamp,
            DefinitionDto = new() { Metadata = new() { DisplayName = "Trusted tenant update" } }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var after = await detail.QueryAsync(new(data.OwnDefinitionId), default);
        await Assert.That(after.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(after.DisplayName).IsEqualTo("Trusted tenant update");
        await Assert.That(async () => await update.ExecuteAsync(new UpdateCustomPropertyDefinitionCommand
        {
            DefinitionId = data.ForeignDefinitionId, TenantId = PlatformDefaults.DefaultTenantId,
            ExpectedConcurrencyStamp = Guid.CreateVersion7(),
            DefinitionDto = new() { Metadata = new() { DisplayName = "Cross-tenant attempt" } }
        }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await detail.QueryAsync(new(data.ForeignDefinitionId), default)).Throws<NotFoundException>();
        using var foreign = TenantScope(factory, data.ForeignTenantId);
        var other = await foreign.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>>()
            .QueryAsync(new(data.ForeignDefinitionId), default);
        await Assert.That(other.DisplayName).IsEqualTo("foreign_definition");
    }

    [Test]
    public async Task NativeMutations_DenyUnprivilegedCallerBeforeAnyWrite()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(scope.ServiceProvider.GetRequiredService<ExploreDbContext>());
        SetNativePrincipal(scope, member.UserId);
        var detail = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>>()
            .QueryAsync(new(data.OwnDefinitionId), default);
        await Assert.That(async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { DefinitionDto = CreateDto() }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new()
            {
                DefinitionId = data.OwnDefinitionId, ExpectedConcurrencyStamp = detail.ConcurrencyStamp,
                DefinitionDto = new() { Metadata = new() { DisplayName = "Denied update" } }
            }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteCustomPropertyDefinitionCommand, bool>>()
            .ExecuteAsync(new() { Id = data.OwnDefinitionId }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await scope.ServiceProvider.GetRequiredService<ICommandHandler<PurgeCustomPropertyDefinitionCommand, BaseCommandResponse<CustomPropertyPurgeResultDto>>>()
            .ExecuteAsync(new() { Id = data.OwnDefinitionId, Reason = "Denied purge" }, default)).Throws<AuthorizationException>();
        var list = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 1);
        await Assert.That(list.TotalCount).IsEqualTo(1);
        await Assert.That(list.Items.Single().Id).IsEqualTo(data.OwnDefinitionId);
        await Assert.That(list.Items.Single().DisplayName).IsEqualTo("own_definition");
    }

    // Replaces the shared-definition route's old mediator stub in AdminRoleEndpointParityTests.
    [Test]
    [Arguments("anonymous", HttpStatusCode.Unauthorized)]
    [Arguments("member", HttpStatusCode.Forbidden)]
    [Arguments("admin", HttpStatusCode.OK)]
    public async Task SharedPurgeHttp_PreservesRoleCohortsThroughRealRepositoryAndAudit(string caller, HttpStatusCode expected)
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        if (caller != "admin")
        {
            client.DefaultRequestHeaders.Remove(TestAuthHandler.AuthHeaderName);
            if (caller == "member")
            {
                using var memberScope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
                var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(memberScope.ServiceProvider.GetRequiredService<ExploreDbContext>());
                client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(member.UserId));
            }
        }
        using var response = await MutateAsync(client, data, "purge");
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        var dependencies = await scope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
            .GetPurgeDependencies(data.Seed.OwnDefinitionId, default);
        if (caller == "admin")
        {
            var result = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<CustomPropertyPurgeResultDto>>())!.Id!;
            await Assert.That(result.Purged).IsTrue();
            await Assert.That(dependencies).IsNull();
            await Assert.That(await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetById(result.AuditLogId!.Value)).IsNotNull();
        }
        else
        {
            await Assert.That(dependencies).IsNotNull();
            await Assert.That((await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetAll()).Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeCreateWithOptions_PersistsOwnedOptionAndDefaultIdentities(bool failCommit)
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(data.UserId, PlatformDefaults.DefaultTenantId));
        await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 1);
        factory.Commits.Fail = failCommit;
        using var created = await client.PostAsJsonAsync(Root, CreateDto() with
        {
            PropertyType = PropertyType.Option,
            Options =
            [
                new() { Namespace = "tenant.community", Key = "kept", DisplayName = "Kept", Value = "kept", IsDefault = true, IsActive = true, SortOrder = 10 },
                new() { Namespace = "tenant.community", Key = "new", DisplayName = "New", Value = "new", IsActive = true, SortOrder = 5 }
            ]
        });
        factory.Commits.Fail = false;
        if (failCommit)
        {
            await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            var reads = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
            await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 1)).TotalCount).IsEqualTo(1);
            await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(reads);
            using var failedScope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            await Assert.That((await failedScope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
                .GetDefinitionsWithDetailsPaged(EntityTypeName.Organization, 1, 1)).TotalCount).IsEqualTo(1);
            return;
        }
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.Id;
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        var detail = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>>()
            .QueryAsync(new(id), default);
        await Assert.That(detail.Options.Count).IsEqualTo(2);
        await Assert.That(detail.Options.Select(option => option.Id).Distinct().Count()).IsEqualTo(2);
        await Assert.That(detail.Options.All(option => option.Id != Guid.Empty)).IsTrue();
        await Assert.That(detail.DefaultOptionId).IsEqualTo(detail.Options.Single(option => option.Key == "kept").Id);
        var graph = await scope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>().GetDefinitionWithDetails(id);
        await Assert.That(graph!.Options.All(option => option.CustomPropertyDefinitionId == id)).IsTrue();
    }

    [Test]
    public async Task NativeHttp_PreservesHalPagingPatchAffordanceAndMissingConcurrencyFailure()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        using var response = await client.GetAsync($"{Root}/{data.Seed.OwnDefinitionId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(detail.RootElement.GetProperty("_links").GetProperty("edit").GetProperty("method").GetString()).IsEqualTo("PATCH");
        using var listResponse = await client.GetAsync($"{Root}?entityTypeName=Organization&pageNumber=2&pageSize=1");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        await Assert.That(list.RootElement.GetProperty("pageNumber").GetInt32()).IsEqualTo(2);
        await Assert.That(list.RootElement.GetProperty("totalCount").GetInt32()).IsEqualTo(3);
        await Assert.That(list.RootElement.GetProperty("_links").TryGetProperty("next", out _)).IsTrue();
        await Assert.That(list.RootElement.GetProperty("_links").TryGetProperty("prev", out _)).IsTrue();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.Seed.OwnDefinitionId}")
        {
            Content = JsonContent.Create(new UpdateCustomPropertyDefinitionDto { Metadata = new() { DisplayName = "Missing stamp" } })
        };
        using var failed = await client.SendAsync(request);
        await Assert.That(failed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var validation = JsonDocument.Parse(await failed.Content.ReadAsStringAsync());
        await Assert.That(validation.RootElement.GetProperty("status").GetInt32()).IsEqualTo(400);
        await Assert.That(validation.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray())
            .IsEquivalentTo(new[] { "if-Match" });
        using var missing = await client.GetAsync($"{Root}/{Guid.CreateVersion7()}");
        await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(missing.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
    }

    private static void SetNativePrincipal(IServiceScope scope, Guid userId) =>
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test"))
        };
}
