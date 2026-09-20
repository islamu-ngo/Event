using System.Reflection;
using System.Text.Json;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

[Category("EventProjectionMapping")]
public sealed class EventProjectionMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly DateTime Now = new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnloadedNavigations_PreserveNullStringsEmptyIdsAndTransportDefaults(bool unloadedPii)
    {
        var source = Entity();
        if (unloadedPii)
            source.Actor = new Actor { ActorType = null!, Pii = null! };
        var detail = EventMapper.ToDetail(source)!;
        var list = EventMapper.ToListItem(source);
        await Assert.That(detail.ActorDisplayName).IsNull();
        await Assert.That(detail.ActorTypeFullName).IsNull();
        await Assert.That(detail.ActorProfilePictureUri).IsNull();
        await Assert.That(detail.EventStatusFullName).IsNull();
        await Assert.That(detail.VisibilityTypeMasterCode).IsNull();
        await Assert.That(detail.EventFormatMasterCode).IsNull();
        await Assert.That(detail.PublicActions).IsEmpty();
        await Assert.That(detail.AvailableAspects).IsEmpty();
        await Assert.That(detail.Tags).IsEmpty();
        await Assert.That(detail.Categories).IsEmpty();
        await Assert.That(detail.FeaturedImageId).IsEqualTo(Guid.Empty);
        await Assert.That(list.EventTypeId).IsEqualTo(0);
        await Assert.That(list.AudienceGenderId).IsEqualTo(0);
        await Assert.That(list.AudienceAgeId).IsEqualTo(0);
        await Assert.That(list.EventTypeFullName).IsNull();
        await Assert.That(list.ActorDisplayName).IsNull();
        await Assert.That(list.IsPast).IsFalse();
        await Assert.That(list.CreatedAtUtc).IsEqualTo(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail, JsonSerializerOptions.Web));
        await Assert.That(json.RootElement.GetProperty("actorDisplayName").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(json.RootElement.GetProperty("publicActions").ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(json.RootElement.TryGetProperty("isReportingIntakeEnabled", out _)).IsFalse();
    }

    [Test]
    public async Task CyclicEventSeriesAndActions_ProduceOrderedIndependentBoundedResponses()
    {
        var source = Entity();
        source.ActorId = ActorId;
        source.Actor = new Actor { Id = ActorId, ActorTypeId = 3, ActorType = new ActorType { FullName = "Group", MasterCode = "GROUP" }, GroupId = ActorId, Pii = new ActorPii { DisplayName = "Publisher", ProfilePictureUri = "https://images.example.test/actor.png" } };
        source.OrganizerActorId = ActorId;
        source.OrganizerActor = source.Actor;
        source.EventType = new EventType { Id = 8, FullName = "Lecture", MasterCode = "LECTURE" };
        source.EventTypeId = 8;
        source.AudienceAge = new AudienceAge { FullName = "Youth", MasterCode = "YOUTH", MinAge = 12, MaxAge = 18 };
        source.AudienceGender = new AudienceGender { FullName = "All", MasterCode = "ALL" };
        source.EventProvenanceTypeId = 2;
        source.EventProvenanceType = new EventProvenanceType { FullName = "Community reported", MasterCode = "COMMUNITY_REPORTED" };
        source.IslamicAspect = new EventIslamicAspect { Event = source, GenderMode = GenderSegregationMode.Mixed, IncludesQuranRecitation = true, Madhab = new Madhab { FullName = "Hanafi", MasterCode = "HANAFI" }, PrimaryLanguage = new Language { FullName = "Arabic", MasterCode = "AR" } };
        source.TechAspect = new EventTechAspect { Event = source, RequiresLaptop = true, TechStackTags = "csharp", PrizePool = 125.5m, PrizeCurrencyCode = "EUR" };
        source.ParticipationConfiguration = EventParticipationConfiguration.Create(EventId, TenantId, (int)ParticipationHandlingModeEnum.ExternalManaged, (int)AdvanceRegistrationObligationEnum.Required, null, null, Now);
        var firstId = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var secondId = Guid.Parse("01900000-0000-7000-8000-000000000011");
        source.PublicActions.Add(Action(source, secondId, 3, EventPublicActionHealthStateEnum.Active));
        source.PublicActions.Add(Action(source, firstId, 3, EventPublicActionHealthStateEnum.Active));
        source.PublicActions.Add(Action(source, ActorId, 0, EventPublicActionHealthStateEnum.Disabled));
        var series = new EventSeries { Title = "Series", Actor = source.Actor, VisibilityType = null!, TenantId = TenantId, Tenant = null! };
        source.EventSeries = series;
        ((List<Explore.Domain.Event>)typeof(EventSeries).GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(series)!).Add(source);
        var detail = EventMapper.ToDetail(source)!;
        var seriesDto = EventMapper.ToDetail(series);
        var list = EventMapper.ToListItem(source);
        source.PublicActions.Clear();
        source.TechAspect.TechStackTags = "Changed";
        source.Actor.Pii.DisplayName = "Changed";
        await Assert.That(detail.PublicActions.Select(value => value.Id).SequenceEqual(new[] { firstId, secondId })).IsTrue();
        await Assert.That(detail.PublicActions[0].EventActorGroupId).IsEqualTo(ActorId);
        await Assert.That(detail.PublicActions[0].OpenInNewTab).IsTrue();
        await Assert.That(detail.PublicActions[0].Rel).IsEqualTo("noopener noreferrer");
        await Assert.That(detail.ActorDisplayName).IsEqualTo("Publisher");
        await Assert.That(detail.ActorTypeId).IsEqualTo(3);
        await Assert.That(detail.ActorGroupId).IsEqualTo(ActorId);
        await Assert.That(detail.OrganizerActorGroupId).IsEqualTo(ActorId);
        await Assert.That(detail.ActorProfilePictureId).IsNull();
        await Assert.That(detail.ActorProfilePictureUri).IsEqualTo("https://images.example.test/actor.png");
        await Assert.That(detail.ProvenanceTypeId).IsEqualTo(2);
        await Assert.That(detail.ProvenanceTypeName).IsEqualTo("Community reported");
        await Assert.That(detail.EventTypeMasterCode).IsEqualTo("LECTURE");
        await Assert.That(detail.AudienceAgeMinAge).IsEqualTo(12);
        await Assert.That(detail.AudienceAgeMaxAge).IsEqualTo(18);
        await Assert.That(detail.AudienceGenderMasterCode).IsEqualTo("ALL");
        await Assert.That(detail.AvailableAspects.SequenceEqual(new[] { "Islamic", "Tech" })).IsTrue();
        await Assert.That(detail.IslamicAspect!.MadhabName).IsEqualTo("Hanafi");
        await Assert.That(detail.IslamicAspect.PrimaryLanguageName).IsEqualTo("Arabic");
        await Assert.That(detail.TechAspect!.TechStackTags).IsEqualTo("csharp");
        await Assert.That(detail.TechAspect.PrizePool).IsEqualTo(125.5m);
        await Assert.That(seriesDto.Events.Count).IsEqualTo(1);
        await Assert.That(seriesDto.Events[0].EventSeriesTitle).IsEqualTo("Series");
        await Assert.That(list.ParticipationConfiguration!.EventId).IsEqualTo(EventId);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(detail, JsonSerializerOptions.Web));
        await Assert.That(json.RootElement.TryGetProperty("organizerActorGroupId", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("actor", out _)).IsFalse();
        await Assert.That(json.RootElement.GetProperty("publicActions")[0].TryGetProperty("eventActorGroupId", out _)).IsFalse();
        using var seriesJson = JsonDocument.Parse(JsonSerializer.Serialize(seriesDto, JsonSerializerOptions.Web));
        await Assert.That(seriesJson.RootElement.GetProperty("events")[0].TryGetProperty("eventSeries", out _)).IsFalse();
    }

    [Test]
    [Arguments("valid", true)]
    [Arguments("attachmentDeleted", true)]
    [Arguments("standalone", false)]
    [Arguments("requirement", false)]
    [Arguments("requirementDeleted", false)]
    [Arguments("effect", false)]
    [Arguments("form", false)]
    [Arguments("formDeleted", false)]
    [Arguments("status", false)]
    [Arguments(nameof(RegistrationFormVersion.SchemaHash), false)]
    [Arguments(nameof(RegistrationFormVersion.DataSchemaArtifact), false)]
    [Arguments(nameof(RegistrationFormVersion.UiSchemaArtifact), false)]
    [Arguments(nameof(RegistrationFormVersion.LogicSchemaArtifact), false)]
    [Arguments(nameof(RegistrationFormVersion.MappingArtifact), false)]
    public async Task ParticipationProjection_PreservesLoadedQuestionnairePredicate(string missing, bool expected)
    {
        var source = Entity();
        var configuration = EventParticipationConfiguration.Create(EventId, TenantId, (int)ParticipationHandlingModeEnum.WalkIn, (int)AdvanceRegistrationObligationEnum.NotApplicable, null, null, Now);
        var requirement = (RegistrationRequirement)Activator.CreateInstance(typeof(RegistrationRequirement), nonPublic: true)!;
        Set(requirement, nameof(RegistrationRequirement.CompletionEffectId), (int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect);
        var form = (RegistrationFormVersion)Activator.CreateInstance(typeof(RegistrationFormVersion), nonPublic: true)!;
        Set(form, nameof(RegistrationFormVersion.StatusId), (int)RegistrationFormStatusEnum.Published);
        foreach (var name in new[] { nameof(RegistrationFormVersion.SchemaHash), nameof(RegistrationFormVersion.DataSchemaArtifact), nameof(RegistrationFormVersion.UiSchemaArtifact), nameof(RegistrationFormVersion.LogicSchemaArtifact), nameof(RegistrationFormVersion.MappingArtifact) })
            Set(form, name, "artifact");
        var attachment = (ParticipationRequirementAttachment)Activator.CreateInstance(typeof(ParticipationRequirementAttachment), nonPublic: true)!;
        Set(attachment, nameof(ParticipationRequirementAttachment.IsStandaloneQuestionnaire), true);
        Set(attachment, nameof(ParticipationRequirementAttachment.RegistrationRequirement), requirement);
        Set(attachment, nameof(ParticipationRequirementAttachment.RegistrationFormVersion), form);
        switch (missing)
        {
            case "valid": break;
            case "attachmentDeleted": attachment.IsDeleted = true; break;
            case "standalone": Set(attachment, nameof(ParticipationRequirementAttachment.IsStandaloneQuestionnaire), false); break;
            case "requirement": Set(attachment, nameof(ParticipationRequirementAttachment.RegistrationRequirement), null); break;
            case "requirementDeleted": requirement.IsDeleted = true; break;
            case "effect": Set(requirement, nameof(RegistrationRequirement.CompletionEffectId), -1); break;
            case "form": Set(attachment, nameof(ParticipationRequirementAttachment.RegistrationFormVersion), null); break;
            case "formDeleted": form.IsDeleted = true; break;
            case "status": Set(form, nameof(RegistrationFormVersion.StatusId), (int)RegistrationFormStatusEnum.Draft); break;
            default: Set(form, missing, " "); break;
        }
        ((List<ParticipationRequirementAttachment>)typeof(EventParticipationConfiguration).GetField("_requirementAttachments", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(configuration)!).Add(attachment);
        source.ParticipationConfiguration = configuration;
        var dto = EventMapper.ToDetail(source)!;
        await Assert.That(dto.ParticipationConfiguration!.HasValidOptionalQuestionnaire).IsEqualTo(expected);
        await Assert.That(dto.ParticipationConfiguration.ParticipationHandlingModeCode).IsNull();
        await Assert.That(dto.ParticipationConfiguration.IdentityAccessModeId).IsNull();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto.ParticipationConfiguration, JsonSerializerOptions.Web));
        await Assert.That(json.RootElement.TryGetProperty("hasValidOptionalQuestionnaire", out _)).IsFalse();
    }

    [Test]
    public async Task PastProjection_UsesEndInstantRatherThanStartOrUnspecifiedCreatedKind()
    {
        var source = Entity();
        source.LastSessionEndUtc = DateTimeOffset.MinValue;
        await Assert.That(EventMapper.ToListItem(source).IsPast).IsTrue();
        source.LastSessionEndUtc = DateTimeOffset.MaxValue;
        await Assert.That(EventMapper.ToListItem(source).IsPast).IsFalse();
    }

    private static Explore.Domain.Event Entity() => new()
    {
        Id = EventId,
        TenantId = TenantId,
        Title = "Festival",
        ConcurrencyStamp = ActorId,
        CreatedAt = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified),
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    private static EventPublicAction Action(Explore.Domain.Event parent, Guid id, int order, EventPublicActionHealthStateEnum health)
    {
        var action = new EventPublicAction { Id = id, Event = parent, EventId = EventId, TenantId = TenantId, EventPublicActionKindId = (int)EventPublicActionKindEnum.ExternalRegistration, HealthStateId = (int)health, SortOrder = order };
        action.SetDestination(ExternalActionUrl.Create("https://registration.example.test/event"));
        return action;
    }

    // Hydration of deliberately partial loaded graphs; production mutations remain domain-owned.
    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target, value);

}
