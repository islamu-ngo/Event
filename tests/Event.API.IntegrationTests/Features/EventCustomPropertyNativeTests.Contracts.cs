using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Authorization;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventCustomPropertyNativeTests
{
    [Test]
    public async Task ControllerAndHost_ExposeExactlyNineProtectedScopedPortsAndUpdateEnricher()
    {
        Type[] ports =
        [
            typeof(ICommandHandler<CreateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<DeleteEventCustomPropertyDefinitionCommand, bool>),
            typeof(ICommandHandler<PurgeEventCustomPropertyDefinitionCommand, BaseCommandResponse<CustomPropertyPurgeResultDto>>),
            typeof(ICommandHandler<SetEventCustomPropertyValueCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<SetEventCustomPropertyMultiValuesCommand, BaseCommandResponse<Guid>>),
            typeof(IQueryHandler<GetEventCustomPropertyDefinitionDetailsRequest, EventCustomPropertyDefinitionDto>),
            typeof(IQueryHandler<GetEventCustomPropertyDefinitionListRequest, PaginatedResult<EventCustomPropertyDefinitionListDto>>),
            typeof(IQueryHandler<GetEventCustomPropertyValuesRequest, List<EventCustomPropertyValueDto>>)
        ];
        await Assert.That(typeof(EventCustomPropertyController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray()).IsEquivalentTo(
                ports.Append(typeof(IResourceAssembler<EventCustomPropertyDefinitionDto, EventCustomPropertyDefinitionListDto>)).ToArray());
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var first = Scope(factory, PlatformDefaults.DefaultTenantId);
        using var second = Scope(factory, PlatformDefaults.DefaultTenantId);
        foreach (var port in ports)
        {
            var handler = first.ServiceProvider.GetRequiredService(port);
            var expected = port.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                ? typeof(AuthorizationCommandHandlerDecorator<,>) : typeof(AuthorizationQueryHandlerDecorator<,>);
            await Assert.That(handler.GetType().GetGenericTypeDefinition()).IsEqualTo(expected);
            await Assert.That(ReferenceEquals(handler, first.ServiceProvider.GetRequiredService(port))).IsTrue();
            await Assert.That(ReferenceEquals(handler, second.ServiceProvider.GetRequiredService(port))).IsFalse();
        }
        await Assert.That(first.ServiceProvider.GetRequiredService<IAuthorizationContextEnricher<UpdateEventCustomPropertyDefinitionCommand>>())
            .IsTypeOf<UpdateEventCustomPropertyDefinitionAuthorizationContextEnricher>();
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    public async Task NativeUpdate_ReplacesForgedTenantWithPersistedAuthority_AndDeniesForeignTarget()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId, data.AdminId);
        var update = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>();
        var result = await update.ExecuteAsync(new()
        {
            DefinitionId = data.DefinitionId, TenantId = data.ForeignTenantId, ExpectedConcurrencyStamp = data.Stamp,
            DefinitionDto = new() { Metadata = new() { DisplayName = "Trusted update" } }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var own = await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId);
        await Assert.That(own.DisplayName).IsEqualTo("Trusted update");
        await Assert.That(own.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(async () => await update.ExecuteAsync(new()
        {
            DefinitionId = data.ForeignDefinitionId, TenantId = PlatformDefaults.DefaultTenantId,
            ExpectedConcurrencyStamp = data.Stamp, DefinitionDto = new() { Metadata = new() { DisplayName = "Foreign attempt" } }
        }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.ForeignDefinitionId)).Throws<NotFoundException>();
        await Assert.That((await DetailAsync(factory, data.ForeignTenantId, data.ForeignDefinitionId)).DisplayName).IsEqualTo("foreign-internal");
    }

    [Test]
    public async Task AllSixNativeWrites_DenyMemberWithForgedAdminClaims_WithoutMutation()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId, data.MemberId);
        var services = scope.ServiceProvider;
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<CreateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { DefinitionDto = CreateDto(data.EventId) }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<UpdateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { DefinitionId = data.DefinitionId, ExpectedConcurrencyStamp = data.Stamp,
                DefinitionDto = new() { Metadata = new() { DisplayName = "Denied" } } }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<DeleteEventCustomPropertyDefinitionCommand, bool>>()
            .ExecuteAsync(new() { Id = data.DefinitionId }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<PurgeEventCustomPropertyDefinitionCommand, BaseCommandResponse<CustomPropertyPurgeResultDto>>>()
            .ExecuteAsync(new() { Id = data.DefinitionId, Reason = "Denied" }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<SetEventCustomPropertyValueCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { ValueDto = ValueDto(data, "Denied") }, default)).Throws<AuthorizationException>();
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<SetEventCustomPropertyMultiValuesCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { DefinitionId = data.DefinitionId, EventId = data.EventId, Values = [ValueDto(data, "Denied")] }, default)).Throws<AuthorizationException>();
        client.DefaultRequestHeaders.Remove(TestAuthHandler.AuthHeaderName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(data.MemberId, PlatformDefaults.DefaultTenantId));
        using var denied = await client.PostAsJsonAsync(Root, CreateDto(data.EventId));
        await ProblemAsync(denied, HttpStatusCode.Forbidden);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId, 1, 20)).TotalCount).IsEqualTo(3);
        await Assert.That((await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId)).DisplayName).IsEqualTo("own");
        await Assert.That((await ValuesAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId)).Count).IsEqualTo(0);
        await Assert.That((await services.GetRequiredService<IAuditLogRepository>().GetAll()).Count).IsEqualTo(0);
    }

    // Replaces the former mediator stub in AdminRoleEndpointParityTests with persisted authority and audit.
    [Test]
    [Arguments("anonymous", HttpStatusCode.Unauthorized)]
    [Arguments("member", HttpStatusCode.Forbidden)]
    [Arguments("admin", HttpStatusCode.OK)]
    public async Task PurgeHttp_RoleCohortsUseRealAuthorityAndDurableAudit(string caller, HttpStatusCode expected)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        if (caller != "admin")
        {
            client.DefaultRequestHeaders.Remove(TestAuthHandler.AuthHeaderName);
            if (caller == "member") client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
                TestAuthHandler.CreateTenantAdminHeaderValue(data.MemberId, PlatformDefaults.DefaultTenantId));
        }
        using var response = await MutateAsync(client, data, "purge");
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        var dependencies = await scope.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>().GetPurgeDependencies(data.DefinitionId, default);
        var audits = await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetAll();
        if (caller == "admin")
        {
            await Assert.That(dependencies).IsNull();
            var result = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<CustomPropertyPurgeResultDto>>())!.Id!;
            await Assert.That(result.Purged).IsTrue();
            await Assert.That(audits.Single().Id).IsEqualTo(result.AuditLogId!.Value);
            await Assert.That(audits.Single().ActorId).IsEqualTo(data.AdminId);
            await Assert.That(audits.Single().EntityId).IsEqualTo(data.DefinitionId.ToString());
            await Assert.That(audits.Single().TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        }
        else
        {
            await Assert.That(dependencies).IsNotNull();
            await Assert.That(audits.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Http_PreservesHalPagingPatchAndExactValidationContracts()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var response = await client.GetAsync($"{Root}/{data.DefinitionId}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(detail.RootElement.GetProperty("_links").GetProperty("edit").GetProperty("method").GetString()).IsEqualTo("PATCH");
        using var listResponse = await client.GetAsync($"{Root}?eventId={data.EventId}&pageNumber=2&pageSize=1");
        await Assert.That(listResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        await Assert.That(list.RootElement.GetProperty("totalCount").GetInt32()).IsEqualTo(3);
        await Assert.That(list.RootElement.GetProperty("pageNumber").GetInt32()).IsEqualTo(2);
        await Assert.That(list.RootElement.GetProperty("_links").TryGetProperty("next", out _)).IsTrue();
        await Assert.That(list.RootElement.GetProperty("_links").TryGetProperty("prev", out _)).IsTrue();
        await Assert.That(list.RootElement.GetProperty("_links").GetProperty("create").GetProperty("method").GetString()).IsEqualTo("POST");
        using var missingStamp = await client.PatchAsJsonAsync($"{Root}/{data.DefinitionId}", new { metadata = new { displayName = "Missing" } });
        await ProblemAsync(missingStamp, HttpStatusCode.BadRequest, errorKey: "if-Match");
        using var invalid = await client.PostAsJsonAsync(Root, CreateDto(data.EventId) with { DisplayName = "" });
        await ProblemAsync(invalid, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyDefinition");
        using var invalidValue = await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "Invalid") with { Ordinal = 1 });
        await ProblemAsync(invalidValue, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyValue");
        using var missing = await client.GetAsync($"{Root}/{Guid.CreateVersion7()}");
        await ProblemAsync(missing, HttpStatusCode.NotFound, "resource_not_found");
        using var foreign = await client.GetAsync($"{Root}/{data.ForeignDefinitionId}");
        await ProblemAsync(foreign, HttpStatusCode.NotFound, "resource_not_found");
        var createBody = JsonSerializer.SerializeToNode(CreateDto(data.EventId), JsonOptions)!.AsObject();
        createBody["tenantId"] = data.ForeignTenantId;
        using var forged = await client.PostAsJsonAsync(Root, createBody);
        await ProblemAsync(forged, HttpStatusCode.BadRequest, "validation_failed");
        using var rejected = JsonDocument.Parse(await forged.Content.ReadAsStringAsync());
        await Assert.That(rejected.RootElement.GetProperty("errors").GetProperty("body").GetArrayLength()).IsEqualTo(1);
        using (var ownScope = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var persisted = await ownScope.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>()
                .GetDefinitionsForEventPaged(data.EventId, 1, 20);
            await Assert.That(persisted.TotalCount).IsEqualTo(3);
            await Assert.That(persisted.Items.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { data.DefinitionId, data.SecondId, data.ThirdId });
        }
        using (var foreignScope = Scope(factory, data.ForeignTenantId))
        {
            var persisted = await foreignScope.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>()
                .GetDefinitionsForEventPaged(data.EventId, 1, 20);
            await Assert.That(persisted.TotalCount).IsEqualTo(0);
        }
        // The identical body without the unknown authority member must be accepted.
        createBody.Remove("tenantId");
        using var valid = await client.PostAsJsonAsync(Root, createBody);
        await Assert.That(valid.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = (await valid.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
        await Assert.That((await DetailAsync(factory, PlatformDefaults.DefaultTenantId, created.Id)).Key).IsEqualTo("created");
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code = null, string? errorKey = null)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Content.Headers.ContentType!.MediaType is "application/problem+json" or "application/json").IsTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        if (code is not null) await Assert.That(json.RootElement.GetProperty("code").GetString()).IsEqualTo(code);
        if (errorKey is not null) await Assert.That(json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ToArray())
            .IsEquivalentTo(new[] { errorKey });
    }

    private static async Task<EventCustomPropertyDefinitionDto> DetailAsync(EventPropertyFactory factory, Guid tenantId, Guid id)
    {
        using var scope = Scope(factory, tenantId);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCustomPropertyDefinitionDetailsRequest, EventCustomPropertyDefinitionDto>>()
            .QueryAsync(new() { Id = id }, default);
    }

    private static async Task<List<EventCustomPropertyValueDto>> ValuesAsync(EventPropertyFactory factory, Guid tenantId, Guid eventId)
    {
        using var scope = Scope(factory, tenantId);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCustomPropertyValuesRequest, List<EventCustomPropertyValueDto>>>()
            .QueryAsync(new() { EventId = eventId }, default);
    }

    private static SetEventCustomPropertyValueDto ValueDto(SeedData data, string value) => new()
    {
        EventCustomPropertyDefinitionId = data.DefinitionId, EventId = data.EventId, TextValue = value
    };
}
