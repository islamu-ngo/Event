using System.Reflection;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

public sealed class RegistrationProviderCapabilityContractTests
{
    private const string RoutePrefix = "api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers";

    [Test]
    public async Task Capabilities_DeclareOnlyTheirCqsAndHalDependenciesAndPreserveClassMetadata()
    {
        Dictionary<Type, Type[]> expected = new()
        {
            [typeof(RegistrationProviderConnectionsController)] =
            [
                typeof(IQueryHandler<GetRegistrationProviderConnectionsQuery, IReadOnlyList<RegistrationProviderConnectionDto>>),
                typeof(IQueryHandler<GetRegistrationProviderConnectionQuery, RegistrationProviderConnectionDto?>),
                typeof(ICommandHandler<UpsertRegistrationProviderConnectionCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<DeleteRegistrationProviderConnectionCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<ReplaceRegistrationProviderApprovedOriginsCommand, BaseCommandResponse<Guid>>),
                typeof(IResourceAssembler<RegistrationProviderConnectionDto, RegistrationProviderConnectionDto>)
            ],
            [typeof(RegistrationProviderBindingsController)] =
            [
                typeof(ICommandHandler<ImportExternalRegistrationProviderFormVersionCommand, BaseCommandResponse<Guid>>),
                typeof(IQueryHandler<GetRegistrationProviderBindingsQuery, IReadOnlyList<RegistrationProviderBindingDto>>),
                typeof(IQueryHandler<GetRegistrationProviderBindingQuery, RegistrationProviderBindingDto?>),
                typeof(ICommandHandler<CreateRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<UpdateRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<DeleteRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<PublishEventRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<ReplaceEventDraftRegistrationProviderMappingsCommand, BaseCommandResponse<Guid>>),
                typeof(IResourceAssembler<RegistrationProviderBindingDto, RegistrationProviderBindingDto>)
            ],
            [typeof(RegistrationProviderChannelsController)] =
            [
                typeof(IQueryHandler<GetRegistrationProviderLaunchDescriptorQuery, RegistrationProviderLaunchDescriptorDto>),
                typeof(IQueryHandler<GetRegistrationChannelsQuery, IReadOnlyList<RegistrationChannelDto>>),
                typeof(ICommandHandler<UpsertRegistrationChannelCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<DeleteRegistrationChannelCommand, BaseCommandResponse<Guid>>),
                typeof(IResourceAssembler<RegistrationChannelDto, RegistrationChannelDto>),
                typeof(IResourceAssembler<RegistrationProviderLaunchDescriptorDto, RegistrationProviderLaunchDescriptorDto>)
            ],
            [typeof(RegistrationProviderOperationsController)] =
            [
                typeof(IQueryHandler<GetRegistrationProviderHealthQuery, IReadOnlyList<RegistrationProviderBindingHealthDto>>),
                typeof(IQueryHandler<GetRegistrationProviderQueueQuery, IReadOnlyList<RegistrationProviderParkedQueueItemDto>>),
                typeof(ICommandHandler<PollRegistrationProviderReconciliationCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<QueueManualRegistrationProviderImportCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<RetryRegistrationProviderParkedItemCommand, BaseCommandResponse<Guid>>),
                typeof(ICommandHandler<ResolveRegistrationProviderQueueItemCommand, BaseCommandResponse<Guid>>),
                typeof(IResourceAssembler<RegistrationProviderBindingHealthDto, RegistrationProviderBindingHealthDto>),
                typeof(IResourceAssembler<RegistrationProviderParkedQueueItemDto, RegistrationProviderParkedQueueItemDto>)
            ]
        };
        Type[] controllers = typeof(EventControllerBase).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(EventControllerBase).IsAssignableFrom(type) && type.GetCustomAttributes<RouteAttribute>().Any(route => route.Template == "api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers"))
            .ToArray();

        await Assert.That(controllers).IsEquivalentTo(expected.Keys.ToArray());
        foreach (Type controller in controllers)
        {
            await Assert.That(controller.IsSealed).IsTrue();
            await Assert.That(controller.IsGenericType).IsFalse();
            await Assert.That(controller.BaseType).IsEqualTo(typeof(EventControllerBase));
            await Assert.That(controller.GetConstructors()).HasSingleItem();
            Type[] dependencies = controller.GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType).ToArray();
            await Assert.That(dependencies.SequenceEqual(expected[controller])).IsTrue();
            await Assert.That(controller.GetCustomAttributes<RouteAttribute>(inherit: false)).HasSingleItem();
            await Assert.That(controller.GetCustomAttribute<RouteAttribute>(inherit: false)!.Template).IsEqualTo(RoutePrefix);
            await Assert.That(controller.GetCustomAttribute<ApiVersionAttribute>(inherit: false)!.Versions
                .Select(version => version.ToString()).SequenceEqual(["0.1"])).IsTrue();
            await Assert.That(controller.GetCustomAttribute<ApiControllerAttribute>(inherit: false)).IsNotNull();
            await Assert.That(controller.GetCustomAttributes<AuthorizeAttribute>(inherit: false)).HasSingleItem();
            AuthorizeAttribute authorization = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: false)!;
            await Assert.That(authorization.Policy).IsNull();
            await Assert.That(authorization.Roles).IsNull();
            await Assert.That(authorization.AuthenticationSchemes).IsNull();
            await Assert.That(controller.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
            await Assert.That(controller.GetCustomAttribute<EndpointClassificationAttribute>(inherit: false)!.Class)
                .IsEqualTo(EndpointClass.Authenticated);
            await Assert.That(controller.GetCustomAttribute<ProducesAttribute>(inherit: false)!.ContentTypes
                .SequenceEqual(["application/json", "application/hal+json"])).IsTrue();
            await Assert.That(controller.GetCustomAttribute<TagsAttribute>(inherit: false)!.Tags
                .SequenceEqual(["RegistrationProviderManagement"])).IsTrue();
        }
    }

    [Test]
    public async Task RuntimeEndpoints_PreserveAllNamedRoutesPayloadsAndEffectivePolicies()
    {
        Type connections = typeof(RegistrationProviderConnectionsController);
        Type bindings = typeof(RegistrationProviderBindingsController);
        Type channels = typeof(RegistrationProviderChannelsController);
        Type operations = typeof(RegistrationProviderOperationsController);
        ExpectedEndpoint[] expected =
        [
            new(connections, nameof(RegistrationProviderConnectionsController.GetConnections), "GET", "connections",
                RouteNames.GetRegistrationProviderConnections, typeof(HalCollectionResource<RegistrationProviderConnectionDto>)),
            new(connections, nameof(RegistrationProviderConnectionsController.GetConnection), "GET", "connections/{connectionId:guid}",
                RouteNames.GetRegistrationProviderConnection, typeof(HalResource<RegistrationProviderConnectionDto>), 1),
            new(connections, nameof(RegistrationProviderConnectionsController.CreateConnection), "POST", "connections",
                RouteNames.CreateRegistrationProviderConnection),
            new(connections, nameof(RegistrationProviderConnectionsController.UpdateConnection), "PUT", "connections/{connectionId:guid}",
                RouteNames.UpdateRegistrationProviderConnection),
            new(connections, nameof(RegistrationProviderConnectionsController.DeleteConnection), "DELETE", "connections/{connectionId:guid}",
                RouteNames.DeleteRegistrationProviderConnection),
            new(connections, nameof(RegistrationProviderConnectionsController.ReplaceApprovedOrigins), "PUT", "connections/{connectionId:guid}/approved-origins",
                RouteNames.ReplaceRegistrationProviderApprovedOrigins),
            new(bindings, nameof(RegistrationProviderBindingsController.ImportExternalFormVersion), "POST", "connections/{connectionId:guid}/external-imports",
                RouteNames.ImportExternalRegistrationProviderFormVersion),
            new(bindings, nameof(RegistrationProviderBindingsController.GetBindings), "GET", "bindings",
                RouteNames.GetRegistrationProviderBindings, typeof(HalCollectionResource<RegistrationProviderBindingDto>)),
            new(bindings, nameof(RegistrationProviderBindingsController.GetBinding), "GET", "bindings/{bindingId:guid}",
                RouteNames.GetRegistrationProviderBinding, typeof(HalResource<RegistrationProviderBindingDto>), 1),
            new(bindings, nameof(RegistrationProviderBindingsController.CreateBinding), "POST", "bindings",
                RouteNames.CreateRegistrationProviderBinding),
            new(bindings, nameof(RegistrationProviderBindingsController.UpdateBinding), "PUT", "bindings/{bindingId:guid}",
                RouteNames.UpdateRegistrationProviderBinding),
            new(bindings, nameof(RegistrationProviderBindingsController.DeleteBinding), "DELETE", "bindings/{bindingId:guid}",
                RouteNames.DeleteRegistrationProviderBinding),
            new(bindings, nameof(RegistrationProviderBindingsController.PublishBinding), "POST", "bindings/{bindingId:guid}/publish",
                RouteNames.PublishRegistrationProviderBinding),
            new(bindings, nameof(RegistrationProviderBindingsController.ReplaceMappings), "PUT", "bindings/{bindingId:guid}/mappings",
                RouteNames.ReplaceRegistrationProviderMappings),
            new(channels, nameof(RegistrationProviderChannelsController.GetLaunchDescriptor), "GET", "workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}/bindings/{bindingId:guid}/launch-descriptor",
                RouteNames.GetRegistrationProviderLaunchDescriptor, typeof(HalResource<RegistrationProviderLaunchDescriptorDto>), 1),
            new(channels, nameof(RegistrationProviderChannelsController.GetChannels), "GET", "workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels",
                RouteNames.GetRegistrationChannels, typeof(HalCollectionResource<RegistrationChannelDto>)),
            new(channels, nameof(RegistrationProviderChannelsController.CreateChannel), "POST", "workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels",
                RouteNames.CreateRegistrationChannel),
            new(channels, nameof(RegistrationProviderChannelsController.UpdateChannel), "PUT", "workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}",
                RouteNames.UpdateRegistrationChannel),
            new(channels, nameof(RegistrationProviderChannelsController.DeleteChannel), "DELETE", "workflows/{workflowId:guid}/requirements/{requirementId:guid}/channels/{channelId:guid}",
                RouteNames.DeleteRegistrationChannel),
            new(operations, nameof(RegistrationProviderOperationsController.GetHealth), "GET", "health",
                RouteNames.GetRegistrationProviderHealth, typeof(HalCollectionResource<RegistrationProviderBindingHealthDto>), 1),
            new(operations, nameof(RegistrationProviderOperationsController.GetQueue), "GET", "queue",
                RouteNames.GetRegistrationProviderQueue, typeof(HalCollectionResource<RegistrationProviderParkedQueueItemDto>), 2),
            new(operations, nameof(RegistrationProviderOperationsController.PollReconciliation), "POST", "{bindingId:guid}/reconcile",
                RouteNames.PollRegistrationProviderReconciliation),
            new(operations, nameof(RegistrationProviderOperationsController.QueueManualImport), "POST", "manual-imports",
                RouteNames.QueueManualRegistrationProviderImport),
            new(operations, nameof(RegistrationProviderOperationsController.RetryQueueItem), "POST", "queue/retry",
                RouteNames.RetryRegistrationProviderParkedItem),
            new(operations, nameof(RegistrationProviderOperationsController.ResolveQueueItem), "POST", "queue/resolve",
                RouteNames.ResolveRegistrationProviderQueueItem)
        ];

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(connections.Assembly);
        builder.Services.AddApiVersioning().AddMvc();
        await using var app = builder.Build();
        app.MapControllers();
        RouteEndpoint[] endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(RoutePrefix + "/", StringComparison.Ordinal) == true)
            .ToArray();

        await Assert.That(endpoints.Length).IsEqualTo(25);
        await Assert.That(endpoints.Select(endpoint => endpoint.Metadata
            .GetMetadata<ControllerActionDescriptor>()!.AttributeRouteInfo!.Name
            ?? throw new InvalidOperationException("A provider endpoint must have a route name.")).ToArray())
            .IsEquivalentTo(expected.Select(route => route.Name).ToArray());
        foreach (ExpectedEndpoint route in expected)
        {
            RouteEndpoint endpoint = endpoints.Single(candidate => candidate.Metadata
                .GetMetadata<ControllerActionDescriptor>()!.AttributeRouteInfo!.Name == route.Name);
            ControllerActionDescriptor action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
            await Assert.That(action.ControllerTypeInfo.AsType()).IsEqualTo(route.Controller);
            await Assert.That(action.MethodInfo.Name).IsEqualTo(route.Action);
            await Assert.That(endpoint.RoutePattern.RawText).IsEqualTo($"{RoutePrefix}/{route.Template}");
            await Assert.That(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)
                .IsEquivalentTo(new[] { route.Verb });
            await Assert.That(endpoint.Metadata.GetMetadata<IAllowAnonymous>()).IsNull();
            await Assert.That(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()).HasSingleItem();
            IAuthorizeData authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Single();
            await Assert.That(authorization.Policy).IsNull();
            await Assert.That(authorization.Roles).IsNull();
            await Assert.That(authorization.AuthenticationSchemes).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<EndpointClassificationAttribute>()!.Class)
                .IsEqualTo(EndpointClass.Authenticated);
            await Assert.That(endpoint.Metadata.GetMetadata<TagsAttribute>()!.Tags
                .SequenceEqual(["RegistrationProviderManagement"])).IsTrue();
            await Assert.That(endpoint.Metadata.GetMetadata<PrivateNoStoreAttribute>()).IsNotNull();
            await Assert.That(endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()!.PolicyName)
                .IsEqualTo(route.Verb == "GET"
                    ? Explore.API.Extensions.RateLimitingExtensions.AuthenticatedPolicy
                    : Explore.API.Extensions.RateLimitingExtensions.WritePolicy);
            await Assert.That(endpoint.Metadata.GetMetadata<RequestTimeoutAttribute>()!.PolicyName)
                .IsEqualTo(route.Verb == "GET"
                    ? Explore.API.Extensions.RequestTimeoutExtensions.LookupPolicy
                    : Explore.API.Extensions.RequestTimeoutExtensions.DefaultPolicy);
            await Assert.That(endpoint.Metadata.GetMetadata<OutputCacheAttribute>()).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<RequestSizeLimitAttribute>()).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<DisableRequestSizeLimitAttribute>()).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<RequireIdempotencyKeyAttribute>()).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<SuppressIdempotencyResponseStorageAttribute>()).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<ProtectIdempotencyReplayAttribute>()).IsNull();
            await Assert.That(endpoint.Metadata.GetMetadata<RevalidateIdempotencyReplayAttribute>()).IsNull();

            Type payload = route.ReadPayload ?? typeof(BaseCommandResponse<Guid>);
            await Assert.That(action.MethodInfo.ReturnType)
                .IsEqualTo(typeof(Task<>).MakeGenericType(typeof(ActionResult<>).MakeGenericType(payload)));
            (int Status, Type Body)[] expectedResponses =
            [
                (200, payload),
                (400, route.Verb == "GET" ? typeof(ProblemDetails) : typeof(ValidationProblemDetails)),
                (401, typeof(ProblemDetails)),
                (403, typeof(ProblemDetails)),
                .. Enumerable.Repeat((404, typeof(ProblemDetails)), route.NotFoundCount)
            ];
            var actualResponses = action.MethodInfo.GetCustomAttributes<ProducesResponseTypeAttribute>()
                .Select(response => (response.StatusCode, response.Type)).ToArray();
            await Assert.That(actualResponses.SequenceEqual(expectedResponses)).IsTrue();

            ParameterInfo[] parameters = action.MethodInfo.GetParameters();
            await Assert.That(parameters[0].Name).IsEqualTo("tenantId");
            await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(Guid));
            await Assert.That(parameters[1].Name).IsEqualTo("eventId");
            await Assert.That(parameters[1].ParameterType).IsEqualTo(typeof(Guid));
            await Assert.That(parameters[^1].Name).IsEqualTo("cancellationToken");
            await Assert.That(parameters[^1].ParameterType).IsEqualTo(typeof(CancellationToken));
            await Assert.That(parameters[^1].IsOptional).IsTrue();
            await Assert.That(parameters.Any(parameter => parameter.GetCustomAttribute<FromServicesAttribute>() is not null))
                .IsFalse();
        }
    }

    [Test]
    public async Task QueueAndReconciliation_PreserveQueryBindingAndQueueDefault()
    {
        ParameterInfo limit = typeof(RegistrationProviderOperationsController)
            .GetMethod(nameof(RegistrationProviderOperationsController.GetQueue))!
            .GetParameters().Single(parameter => parameter.Name == "limit");
        await Assert.That(limit.ParameterType).IsEqualTo(typeof(int));
        await Assert.That(limit.GetCustomAttribute<FromQueryAttribute>()).IsNotNull();
        await Assert.That(limit.DefaultValue).IsEqualTo(50);

        ParameterInfo sinceUtc = typeof(RegistrationProviderOperationsController)
            .GetMethod(nameof(RegistrationProviderOperationsController.PollReconciliation))!
            .GetParameters().Single(parameter => parameter.Name == "sinceUtc");
        await Assert.That(sinceUtc.ParameterType).IsEqualTo(typeof(DateTime));
        await Assert.That(sinceUtc.GetCustomAttribute<FromQueryAttribute>()).IsNotNull();
        await Assert.That(sinceUtc.IsOptional).IsFalse();
    }

    private sealed record ExpectedEndpoint(
        Type Controller,
        string Action,
        string Verb,
        string Template,
        string Name,
        Type? ReadPayload = null,
        int NotFoundCount = 0);
}
