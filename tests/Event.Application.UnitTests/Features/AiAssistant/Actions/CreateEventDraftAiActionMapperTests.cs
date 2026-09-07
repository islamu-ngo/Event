using Explore.Application.Features.AiAssistant.Actions;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.AiAssistant.Actions;

public sealed class CreateEventDraftAiActionMapperTests
{
    [Test]
    public async Task Map_WhenPayloadHasLookupReferences_PreservesDraftReferences()
    {
        var categoryId = Guid.CreateVersion7();
        var tagId = Guid.CreateVersion7();

        var result = new CreateEventDraftAiActionMapper().Map(
            $$"""
              {
                "title": "  Community Iftar  ",
                "description": "Evening meal",
                "eventTypeId": 999,
                "audienceGenderId": 999,
                "audienceAgeId": 999,
                "visibilityTypeId": 999,
                "eventFormatId": 999,
                "madhabId": 999,
                "categoryIds": ["{{categoryId}}"],
                "tagIds": ["{{tagId}}", "{{tagId}}"],
                "participationConfiguration": {
                  "participationHandlingModeId": 1,
                  "advanceRegistrationObligationId": 1
                }
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Draft).IsNotNull();
        await Assert.That(result.Draft!.Title).IsEqualTo("Community Iftar");
        await Assert.That(result.Draft.Description).IsEqualTo("Evening meal");
        await Assert.That(result.Draft.EventTypeId).IsEqualTo(999);
        await Assert.That(result.Draft.AudienceGenderId).IsEqualTo(999);
        await Assert.That(result.Draft.AudienceAgeId).IsEqualTo(999);
        await Assert.That(result.Draft.VisibilityTypeId).IsEqualTo(999);
        await Assert.That(result.Draft.EventFormatId).IsEqualTo(999);
        await Assert.That(result.Draft.MadhabId).IsEqualTo(999);
        await Assert.That(result.Draft.CategoryIds).IsEquivalentTo([categoryId]);
        await Assert.That(result.Draft.TagIds).IsEquivalentTo([tagId]);

        var createRequest = result.Draft.ToCreateEventDto();
        await Assert.That(createRequest.EventStatusId).IsEqualTo(1);
        await Assert.That(createRequest.Sessions).IsEmpty();
        await Assert.That(createRequest.Days).IsEmpty();
        await Assert.That(createRequest.Rooms).IsEmpty();
        await Assert.That(createRequest.AgendaItems).IsEmpty();
    }

    [Test]
    public async Task Map_WhenPayloadContainsPrimaryStructuredDetails_MapsDetailsToDraftRequest()
    {
        var speakerActorId = Guid.CreateVersion7();
        var result = new CreateEventDraftAiActionMapper().Map(
            $$"""
              {
                "title": "  Poster Event  ",
                "islamicAspect": {
                  "genderMode": 3,
                  "includesQuranRecitation": true
                },
                "location": {
                  "fullName": " Islamic Centre Brussels ",
                  "address": " Rue Example 10 ",
                  "postcode": " 1000 ",
                  "country": " Belgium ",
                  "city": " Brussels ",
                  "timezone": " Europe/Brussels "
                },
                "room": {
                  "name": " Main Hall "
                },
                "session": {
                  "startTime": "2026-07-10T18:00:00Z",
                  "endTime": "2026-07-10T20:00:00Z",
                  "title": " Opening Lecture ",
                  "eventSessionKindId": 2,
                  "speakerActorIds": ["{{speakerActorId}}", "{{speakerActorId}}"]
                },
                "participationConfiguration": {
                  "participationHandlingModeId": 1,
                  "advanceRegistrationObligationId": 1
                }
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Draft).IsNotNull();
        await Assert.That(result.Draft!.IslamicAspect).IsNotNull();
        await Assert.That(result.Draft.IslamicAspect!.GenderMode).IsEqualTo(GenderSegregationMode.Segregated);
        await Assert.That(result.Draft.Locations.Single().TempKey).IsEqualTo("primary-location");
        await Assert.That(result.Draft.Locations.Single().FullName).IsEqualTo("Islamic Centre Brussels");
        await Assert.That(result.Draft.Rooms.Single().TempKey).IsEqualTo("primary-room");
        await Assert.That(result.Draft.Rooms.Single().LocationTempKey).IsEqualTo("primary-location");
        await Assert.That(result.Draft.Sessions.Single().Title).IsEqualTo("Opening Lecture");
        await Assert.That(result.Draft.Sessions.Single().RoomTempKey).IsEqualTo("primary-room");
        await Assert.That(result.Draft.Sessions.Single().EventSessionKindId).IsEqualTo(2);
        await Assert.That(result.Draft.Sessions.Single().SpeakerActorIds).IsEquivalentTo([speakerActorId]);
        await Assert.That(result.Draft.AgendaItems).IsEmpty();
    }

    [Test]
    public async Task Map_WhenPayloadContainsIncompleteStructuredDetails_SkipsInvalidNestedRows()
    {
        var result = new CreateEventDraftAiActionMapper().Map(
            """
              {
                "title": "Poster Event",
                "location": {
                  "fullName": "Islamic Centre Brussels"
                },
                "room": {
                  "name": "Main Hall"
                },
                "session": {
                  "startTime": "2026-07-10T18:00:00Z"
                },
                "participationConfiguration": {
                  "participationHandlingModeId": 1,
                  "advanceRegistrationObligationId": 1
                }
              }
              """);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Draft).IsNotNull();
        await Assert.That(result.Draft!.Locations).IsEmpty();
        await Assert.That(result.Draft.Rooms).IsEmpty();
        await Assert.That(result.Draft.Sessions).IsEmpty();
    }

    [Test]
    public async Task Map_WhenTitleIsMissing_ReturnsMissingTitleFailure()
    {
        var result = new CreateEventDraftAiActionMapper().Map("{\"description\":\"No title\"}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_title");
    }

    [Test]
    public async Task Map_WhenTitleIsTooLong_ReturnsFieldTooLongFailure()
    {
        var result = new CreateEventDraftAiActionMapper().Map($"{{\"title\":\"{new string('A', 201)}\"}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("field_too_long");
    }

    [Test]
    [Arguments("tenantId")]
    [Arguments("eventStatusId")]
    [Arguments("firstSessionStartUtc")]
    [Arguments("sessionCount")]
    [Arguments("actorId")]
    [Arguments("roleAssignments")]
    public async Task Map_WhenPayloadContainsPrivilegedField_ReturnsUnsupportedPayloadFieldFailure(string fieldName)
    {
        var result = new CreateEventDraftAiActionMapper().Map($"{{\"title\":\"Draft\",\"{fieldName}\":\"not allowed\"}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }

    [Test]
    public async Task Map_WhenPayloadContainsUnknownField_ReturnsUnsupportedPayloadFieldFailure()
    {
        var result = new CreateEventDraftAiActionMapper().Map("{\"title\":\"Draft\",\"unexpected\":true}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }

    [Test]
    public async Task Map_WhenOrganizationAndGroupAreBothSet_ReturnsConflictingOwnerScopeFailure()
    {
        var organizationId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        var result = new CreateEventDraftAiActionMapper().Map(
            $"{{\"title\":\"Draft\",\"organizationId\":\"{organizationId}\",\"groupId\":\"{groupId}\",\"participationConfiguration\":{{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}}}",
            new CreateEventDraftAiActionMappingContext(
                new HashSet<Guid> { organizationId },
                new HashSet<Guid> { groupId }));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("conflicting_owner_scope");
    }

    [Test]
    public async Task Map_WhenOrganizationIsNotAllowed_ReturnsInvalidOrganizationScopeFailure()
    {
        var organizationId = Guid.CreateVersion7();
        var result = new CreateEventDraftAiActionMapper().Map(
            $"{{\"title\":\"Draft\",\"organizationId\":\"{organizationId}\",\"participationConfiguration\":{{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_organization_scope");
    }

    [Test]
    public async Task Map_WhenOrganizationIsAllowed_MapsOrganizationId()
    {
        var organizationId = Guid.CreateVersion7();
        var result = new CreateEventDraftAiActionMapper().Map(
            $"{{\"title\":\"Draft\",\"organizationId\":\"{organizationId}\",\"participationConfiguration\":{{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}}}",
            new CreateEventDraftAiActionMappingContext(
                new HashSet<Guid> { organizationId },
                new HashSet<Guid>()));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Draft).IsNotNull();
        await Assert.That(result.Draft!.OrganizationId).IsEqualTo(organizationId);
    }

    [Test]
    public async Task Map_WhenGroupIsNotAllowed_ReturnsInvalidGroupScopeFailure()
    {
        var groupId = Guid.CreateVersion7();
        var result = new CreateEventDraftAiActionMapper().Map(
            $"{{\"title\":\"Draft\",\"groupId\":\"{groupId}\",\"participationConfiguration\":{{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}}}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_group_scope");
    }

    [Test]
    public async Task Map_WhenRemovedPriceFieldIsPresent_ReturnsUnsupportedPayloadFieldFailure()
    {
        var result = new CreateEventDraftAiActionMapper().Map("{\"title\":\"Draft\",\"price\":-1}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_payload_field");
    }
}
