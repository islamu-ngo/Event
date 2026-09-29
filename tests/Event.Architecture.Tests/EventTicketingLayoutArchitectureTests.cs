using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Features.EventTicketing.Requests.Queries;

namespace Event.Architecture.Tests;

public sealed class EventTicketingLayoutArchitectureTests
{
    private const string FeatureNamespace = "Explore.Application.Features.EventTicketing";

    [Test]
    public async Task EventTicketingHandlers_ShouldUseCommandAndQueryNamespaces()
    {
        Type[] handlers = typeof(CreateEventTicketCatalogDraftCommand).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Namespace?.StartsWith($"{FeatureNamespace}.Handlers.", StringComparison.Ordinal) == true
                && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToArray();
        string[] failures = handlers
            .Where(type =>
                type.Name.EndsWith("CommandHandler", StringComparison.Ordinal)
                    ? type.Namespace != $"{FeatureNamespace}.Handlers.Commands"
                    : type.Name.EndsWith("QueryHandler", StringComparison.Ordinal)
                        ? type.Namespace != $"{FeatureNamespace}.Handlers.Queries"
                        : true)
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(handlers).IsNotEmpty();
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task EventTicketingRootNamespace_ShouldNotContainHandlersServicesBasesOrRequests()
    {
        string[] forbiddenTypes = typeof(CreateEventTicketCatalogDraftCommand).Assembly.GetTypes()
            .Where(type => type.Namespace == FeatureNamespace
                && (type.Name.EndsWith("Handler", StringComparison.Ordinal)
                    || type.Name.EndsWith("Service", StringComparison.Ordinal)
                    || type.Name.EndsWith("Base", StringComparison.Ordinal)
                    || type.Name.Contains("Request", StringComparison.Ordinal)))
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(forbiddenTypes).IsEmpty();
    }

    [Test]
    public async Task EventTicketingRequests_ShouldUseRequestNamespaces()
    {
        Type[] requests = typeof(CreateEventTicketCatalogDraftCommand).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Namespace?.StartsWith($"{FeatureNamespace}.Requests.", StringComparison.Ordinal) == true
                && OperationContractDiscovery.IsNativeRequest(type))
            .ToArray();
        string[] failures = requests
            .Where(type => type.Name.EndsWith("Command", StringComparison.Ordinal)
                ? type.Namespace != $"{FeatureNamespace}.Requests.Commands"
                : type.Name.EndsWith("Query", StringComparison.Ordinal)
                    ? type.Namespace != $"{FeatureNamespace}.Requests.Queries"
                    : true)
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(requests).IsNotEmpty();
        await Assert.That(failures).IsEmpty();
    }

    [Test]
    [Category("Phase43Ticketing")]
    public async Task EventTicketingRequests_ShouldAuthorizeParentEventWithManageTickets()
    {
        Type[] requestTypes =
        [
            typeof(CreateEventTicketCatalogDraftCommand),
            typeof(CloneEventTicketCatalogDraftCommand),
            typeof(PublishEventTicketCatalogCommand),
            typeof(CreateEventTicketTypeCommand),
            typeof(UpdateEventTicketTypeCommand),
            typeof(DeleteEventTicketTypeCommand),
            typeof(CreateEventCapacityPoolCommand),
            typeof(UpdateEventCapacityPoolCommand),
            typeof(DeleteEventCapacityPoolCommand),
            typeof(GetEventTicketCatalogManagementQuery)
        ];

        foreach (var requestType in requestTypes)
        {
            var authorization = requestType
                .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
                .Cast<AuthorizeResourceAttribute>()
                .Single();

            await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
            await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Events.ManageTickets);
        }
    }

    [Test]
    public async Task EventTicketingManageDtos_ShouldOmitPersistedIds_ReadDtosShouldRetainThem()
    {
        Type[] manageDtos =
        [
            typeof(ManageEventTicketTypeDto),
            typeof(ManageEventCapacityPoolDto),
            typeof(ManageTicketTypeEntitlementDto)
        ];
        string[] failures = manageDtos
            .Where(type => type.GetProperty("Id") is not null)
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(failures).IsEmpty();
        await Assert.That(typeof(EventTicketTypeDto).GetProperty("Id")).IsNotNull();
        await Assert.That(typeof(EventCapacityPoolDto).GetProperty("Id")).IsNotNull();
        await Assert.That(typeof(CreateEventTicketTypeCommand).GetProperty("TicketType")!.PropertyType)
            .IsEqualTo(typeof(ManageEventTicketTypeDto));
        await Assert.That(typeof(UpdateEventTicketTypeCommand).GetProperty("TicketType")!.PropertyType)
            .IsEqualTo(typeof(ManageEventTicketTypeDto));
        await Assert.That(typeof(CreateEventCapacityPoolCommand).GetProperty("CapacityPool")!.PropertyType)
            .IsEqualTo(typeof(ManageEventCapacityPoolDto));
        await Assert.That(typeof(UpdateEventCapacityPoolCommand).GetProperty("CapacityPool")!.PropertyType)
            .IsEqualTo(typeof(ManageEventCapacityPoolDto));
    }
}
