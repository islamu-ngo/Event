using Explore.API.Hateoas;
using Explore.API.Mcp;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[Category("EventLocationPrivacy")]
[Category("EventLocationPrivacyMcp")]
public sealed class EventLocationPrivacyMcpContractTests
{
    private static readonly HashSet<string> PhysicalLocationFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LocationId",
        "PhysicalLocationId",
        "LocationName",
        "LocationFullName",
        "LocationCity",
        "LocationCountry",
        "RoomId",
        "RoomName",
        "Address",
        "Postcode",
        "PostalCode",
        "Latitude",
        "Longitude",
        "Coordinates"
    };

    [Test]
    public async Task AnonymousEventProgramAndSessionDescriptors_OmitPhysicalLocationFields()
    {
        var anonymousDescriptorTypes = new[]
        {
            typeof(EventMcpSummaryDescriptor),
            typeof(EventMcpDetailDescriptor),
            typeof(EventMcpProgramSessionGroupDescriptor),
            typeof(EventMcpProgramItemDescriptor),
            typeof(EventMcpSessionSummaryDescriptor),
            typeof(EventMcpSessionGroupDescriptor)
        };

        var leakedFields = anonymousDescriptorTypes
            .SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .Where(field => PhysicalLocationFieldNames.Contains(field[(field.LastIndexOf('.') + 1)..]))
            .ToArray();

        await Assert.That(leakedFields).IsEmpty().Because("anonymous-safe MCP contracts must not expose physical location or room identifiers");
    }

    [Test]
    public async Task EventMcpTools_RequireAiContextGateway()
    {
        await Assert.That(typeof(EventManagementMcpTools).GetConstructors()
            .Single()
            .GetParameters()).Contains(parameter => parameter.ParameterType == typeof(EventMcpLocationDisclosureGuard)).Because("location-bearing MCP tool context must pass through the disclosure guard");
        await Assert.That(typeof(EventMcpLocationDisclosureGuard).GetConstructors()
            .Single()
            .GetParameters()).Contains(parameter => parameter.ParameterType == typeof(IAiContextGateway)).Because("the disclosure guard must pass location context through the AI context gateway");
    }

    [Test]
    public async Task ListPublicEventSessions_InvokesAiContextGatewayOnRealAdapterPath()
    {
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        var gateway = CreateZeroDisclosureGateway();

        mediator.Send(Arg.Any<GetEventDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatePublishedEvent(eventId));
        mediator.Send(Arg.Any<GetSessionsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionListDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    EventTitle = "Privacy contract event",
                    LocationId = Guid.NewGuid(),
                    LocationFullName = "PRIVATE-VENUE",
                    LocationCity = "PRIVATE-CITY",
                    RoomId = Guid.NewGuid(),
                    RoomName = "PRIVATE-ROOM"
                }
            });

        var tools = await CreateTools(mediator, gateway);

        await tools.ListPublicEventSessionsAsync(eventId);

        var gatewayWasInvoked = gateway.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == nameof(IAiContextGateway.Sanitize)
            || call.GetMethodInfo().Name == nameof(IAiContextGateway.SanitizeMany));
        await Assert.That(gatewayWasInvoked).IsTrue().Because("a real anonymous MCP adapter path must invoke the disclosure gateway, not merely inject it");
    }

    [Test]
    public async Task GetPublicEventProgramSummary_InvokesGatewayAndOmitsPhysicalValues()
    {
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        var gateway = CreateZeroDisclosureGateway();
        mediator.Send(Arg.Any<GetEventDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatePublishedEvent(eventId));
        mediator.Send(Arg.Any<GetEventProgramSummaryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EventProgramSummaryDto
            {
                EventId = eventId,
                EventTitle = "Privacy contract event",
                Sections =
                [
                    new EventProgramSectionDto
                    {
                        SectionKey = "main",
                        Title = "Program",
                        SessionGroups =
                        [
                            new EventProgramSessionGroupSectionDto
                            {
                                Title = "Private group",
                                LocationName = "PRIVATE-VENUE",
                                RoomName = "PRIVATE-ROOM"
                            }
                        ]
                    }
                ]
            });

        var result = await (await CreateTools(mediator, gateway))
            .GetPublicEventProgramSummaryAsync(eventId);

        await Assert.That(gateway.ReceivedCalls()).Contains(call =>
            call.GetMethodInfo().Name == nameof(IAiContextGateway.SanitizeMany));
        await Assert.That(result).DoesNotContain("PRIVATE-VENUE");
        await Assert.That(result).DoesNotContain("PRIVATE-ROOM");
    }

    [Test]
    public async Task PublicSessionAdapter_FailsClosedWhenGatewayDisclosesPhysicalValue()
    {
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetEventDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatePublishedEvent(eventId));
        mediator.Send(Arg.Any<GetSessionsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionListDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    EventTitle = "Privacy contract event",
                    EventLocation = CreateSuppressedPublicLocation()
                }
            });
        var gateway = Substitute.For<IAiContextGateway>();
        gateway.SanitizeMany(Arg.Any<IReadOnlyList<AiContextSanitizationInput>>())
            .Returns(call => call.Arg<IReadOnlyList<AiContextSanitizationInput>>()!
                .Select(DisclosePrivateVenue)
                .ToArray());

        var act = async () => await (await CreateTools(mediator, gateway)).ListPublicEventSessionsAsync(eventId);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Test]
    [Arguments(1, 0)]
    [Arguments(2, 1)]
    [Arguments(1, 2)]
    public async Task PublicSessionAdapter_FailsClosedWhenGatewayResultCountDoesNotMatchRequests(
        int requestCount,
        int resultCount)
    {
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetEventDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatePublishedEvent(eventId));
        mediator.Send(Arg.Any<GetSessionsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, requestCount)
                .Select(index => new EventSessionListDto
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    EventTitle = $"Privacy contract event {index}",
                    EventLocation = CreateSuppressedPublicLocation()
                })
                .ToList());
        var gateway = Substitute.For<IAiContextGateway>();
        gateway.SanitizeMany(Arg.Any<IReadOnlyList<AiContextSanitizationInput>>())
            .Returns(Enumerable.Range(0, resultCount)
                .Select(_ => PassThroughLocationEnvelope())
                .ToArray());

        var act = async () => await (await CreateTools(mediator, gateway)).ListPublicEventSessionsAsync(eventId);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Test]
    public async Task PublicSessionAdapter_FailsClosedWhenGatewayResultEntityDoesNotMatchRequest()
    {
        var eventId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetEventDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns(CreatePublishedEvent(eventId));
        mediator.Send(Arg.Any<GetSessionsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionListDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    EventTitle = "Privacy contract event"
                }
            });
        var gateway = Substitute.For<IAiContextGateway>();
        gateway.SanitizeMany(Arg.Any<IReadOnlyList<AiContextSanitizationInput>>())
            .Returns([AiContextSanitizedEnvelope.Success("EventPii", [], [], [])]);

        var act = async () => await (await CreateTools(mediator, gateway)).ListPublicEventSessionsAsync(eventId);

        await Assert.ThrowsAsync<InvalidOperationException>(act);
    }

    [Test]
    public async Task ProgramManagementContext_UsesManagedQueriesAndGatewayWithoutPhysicalLocationContract()
    {
        var eventId = Guid.NewGuid();
        var eventDto = CreatePublishedEvent(eventId);
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetEventManagementDetailsRequest>(), Arg.Any<CancellationToken>())
            .Returns(eventDto);
        mediator.Send(Arg.Any<GetManagedSessionsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionListDto>());
        mediator.Send(Arg.Any<GetManagedEventSessionGroupsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionGroupListDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    Name = "PRIVATE-DRAFT-TRACK",
                    LocationId = Guid.NewGuid(),
                    LocationName = "PRIVATE-MANAGEMENT-VENUE",
                    RoomId = Guid.NewGuid(),
                    RoomName = "PRIVATE-MANAGEMENT-ROOM"
                }
            });
        mediator.Send(Arg.Any<GetManagedEventDaysByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventDayListDto>());
        mediator.Send(Arg.Any<GetManagedEventAgendaItemsByEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<EventAgendaItemListDto>());

        var assembler = Substitute.For<IResourceAssembler<EventDto, EventListDto>>();
        assembler.ToResource(eventDto, Arg.Any<HttpContext>())
            .Returns(new HalResource<EventDto>(eventDto, new Dictionary<string, HalLink>
            {
                [LinkRelations.Edit] = HalLink.CreateAction(
                    $"/api/event/{eventId}",
                    HttpMethods.Put)
            }));
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());
        var gateway = CreateZeroDisclosureGateway();

        var result = await (await CreateTools(mediator, gateway, assembler, httpContextAccessor))
            .GetEventProgramManagementContextAsync(eventId);

        await Assert.That(gateway.ReceivedCalls()).Contains(call =>
            call.GetMethodInfo().Name == nameof(IAiContextGateway.SanitizeMany));
        await mediator.Received(1).Send(
            Arg.Any<GetManagedEventSessionGroupsByEventRequest>(),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(
            Arg.Any<GetManagedEventAgendaItemsByEventRequest>(),
            Arg.Any<CancellationToken>());
        await Assert.That(result).Contains("PRIVATE-DRAFT-TRACK");
        await Assert.That(result).DoesNotContain("LocationName");
        await Assert.That(result).DoesNotContain("RoomName");
        await Assert.That(result).DoesNotContain("PRIVATE-MANAGEMENT-VENUE");
        await Assert.That(result).DoesNotContain("PRIVATE-MANAGEMENT-ROOM");
    }

    private static async Task<EventManagementMcpTools> CreateTools(
        IMediator mediator,
        IAiContextGateway gateway,
        IResourceAssembler<EventDto, EventListDto>? eventResourceAssembler = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var dependencies = new Dictionary<Type, object>
        {
            [typeof(IMediator)] = mediator,
            [typeof(IUserContext)] = Substitute.For<IUserContext>(),
            [typeof(ITenantContext)] = Substitute.For<ITenantContext>(),
            [typeof(IResourceAssembler<EventDto, EventListDto>)] = eventResourceAssembler
                ?? Substitute.For<IResourceAssembler<EventDto, EventListDto>>(),
            [typeof(IHttpContextAccessor)] = httpContextAccessor ?? Substitute.For<IHttpContextAccessor>(),
            [typeof(EventMcpLocationDisclosureGuard)] = new EventMcpLocationDisclosureGuard(gateway)
        };
        var constructor = typeof(EventManagementMcpTools).GetConstructors().Single();
        var parameters = constructor.GetParameters();

        // The tools class reaches the gateway through the disclosure guard, so the guarantee is asserted
        // across both hops rather than on a single constructor.
        await Assert.That(parameters).Contains(parameter => parameter.ParameterType == typeof(EventMcpLocationDisclosureGuard)).Because("the public-session MCP path must receive the location disclosure guard");
        await Assert.That(typeof(EventMcpLocationDisclosureGuard).GetConstructors().Single().GetParameters())
            .Contains(parameter => parameter.ParameterType == typeof(IAiContextGateway)).Because("the disclosure guard must resolve its ceilings through the AI context gateway");

        return (EventManagementMcpTools)constructor.Invoke(
            parameters.Select(parameter => dependencies[parameter.ParameterType]).ToArray());
    }

    private static IAiContextGateway CreateZeroDisclosureGateway()
    {
        var gateway = Substitute.For<IAiContextGateway>();
        gateway.Sanitize(Arg.Any<AiContextSanitizationInput>())
            .Returns(call => PassThrough(call.Arg<AiContextSanitizationInput>()!));
        gateway.SanitizeMany(Arg.Any<IReadOnlyList<AiContextSanitizationInput>>())
            .Returns(call => call.Arg<IReadOnlyList<AiContextSanitizationInput>>()!
                .Select(PassThrough)
                .ToArray());
        return gateway;
    }

    private static AiContextSanitizedEnvelope PassThrough(AiContextSanitizationInput input) =>
        AiContextSanitizedEnvelope.Success(
            input.EntityName,
            [],
            [],
            input.Fields.Keys.ToArray());

    private static AiContextSanitizedEnvelope PassThroughLocationEnvelope() =>
        AiContextSanitizedEnvelope.Success("LocationPii", [], [], []);

    private static AiContextSanitizedEnvelope DiscloseAll(AiContextSanitizationInput input) =>
        AiContextSanitizedEnvelope.Success(
            input.EntityName,
            input.Fields
                .Select(field => new AiContextDisclosedField(
                    field.Key,
                    field.Value,
                    AiContextDisclosureRuleEnum.Allow))
                .ToArray(),
            [],
            []);

    private static AiContextSanitizedEnvelope DisclosePrivateVenue(AiContextSanitizationInput input) =>
        AiContextSanitizedEnvelope.Success(
            input.EntityName,
            input.Fields
                .Select(field => new AiContextDisclosedField(
                    field.Key,
                    field.Key == nameof(EventLocationPublicFieldsDto.VenueName) ? "PRIVATE-VENUE" : field.Value,
                    AiContextDisclosureRuleEnum.Allow))
                .ToArray(),
            [],
            []);

    private static EventLocationPublicDto CreateSuppressedPublicLocation() =>
        EventLocationPublicDto.FromDisclosureResult(EventLocationDisclosureResult.Suppressed(
            Guid.NewGuid(),
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureState.Hidden));

    private static EventDto CreatePublishedEvent(Guid eventId) => new()
    {
        Id = eventId,
        Title = "Privacy contract event",
        ActorDisplayName = "Privacy organizer",
        ActorTypeFullName = "Organization",
        EventStatusId = (int)EventStatusEnum.Published,
        EventStatusFullName = "Published",
        EventStatusMasterCode = "PUBLISHED",
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        VisibilityTypeFullName = "Public",
        VisibilityTypeMasterCode = "PUBLIC",
        EventFormatFullName = "In person",
        EventFormatMasterCode = "IN_PERSON"
    };
}
