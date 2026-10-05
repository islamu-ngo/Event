using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services.Federation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence.Repositories;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoveryResponseBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2040, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Future_boundary_in_an_omitted_event_still_bounds_empty_filtered_responses()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AddSession(fixture, "not matched by the requested filter", Now.AddMinutes(1), Now.AddMinutes(2));
        await fixture.Context.SaveChangesAsync();
        await Assert.That(await Reader(fixture).GetNextAsync(fixture.TenantId, Now, CancellationToken.None))
            .IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task Current_occurrence_end_precedes_a_later_start()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AddSession(fixture, "ongoing", Now.AddMinutes(-1), Now.AddSeconds(20));
        AddSession(fixture, "future", Now.AddMinutes(1), Now.AddMinutes(2));
        await fixture.Context.SaveChangesAsync();
        await Assert.That(await Reader(fixture).GetNextAsync(fixture.TenantId, Now, CancellationToken.None))
            .IsEqualTo(Now.AddSeconds(20));
    }

    [Test]
    public async Task Private_sources_cannot_influence_the_public_clock_boundary()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var hidden = AddSession(fixture, "private", Now.AddSeconds(1), Now.AddSeconds(2));
        hidden.Event.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        AddSession(fixture, "public", Now.AddMinutes(1), Now.AddMinutes(2));
        await fixture.Context.SaveChangesAsync();
        await Assert.That(await Reader(fixture).GetNextAsync(fixture.TenantId, Now, CancellationToken.None))
            .IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task Past_only_sources_have_no_future_clock_transition()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AddSession(fixture, "past", Now.AddMinutes(-2), Now.AddMinutes(-1));
        await fixture.Context.SaveChangesAsync();
        await Assert.That(await Reader(fixture).GetNextAsync(fixture.TenantId, Now, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task Reveal_components_supply_a_safe_boundary_without_provider_duration_rounding()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var session = AddSession(fixture, "venue", Now.AddDays(1), Now.AddDays(2));
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            FullName = "Public venue",
            City = "Brussels",
            Country = "BE"
        };
        location.SetManualAddress("Public venue address", "1000");
        fixture.Context.Locations.Add(location);
        var carrier = EventLocation.CreatePhysical(
            fixture.TenantId, session.EventId, location.Id, fixture.UserId, Now.AddHours(-1).UtcDateTime);
        carrier.ChangeDisclosurePolicy(EventLocationDisclosureFields.StreetAddress,
            LocationDisclosureAudienceEnum.Never, Now.AddMinutes(1).UtcDateTime,
            carrier.PolicyVersion, fixture.UserId, EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            Now.UtcDateTime, needsPrivacyReview: false);
        fixture.Context.EventLocations.Add(carrier);
        session.AssignEventLocation(carrier);
        await fixture.Context.SaveChangesAsync();
        var offset = TimeSpan.FromHours(1) + TimeSpan.FromTicks(1);
        await Assert.That(await Reader(fixture, offset).GetNextAsync(fixture.TenantId, Now, CancellationToken.None))
            .IsEqualTo(Now.AddTicks(1));
    }

    private static EventDiscoveryResponseBoundaryRepository Reader(
        EventVisitorCapabilitySqliteFixture fixture, TimeSpan? revealOffset = null)
    {
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ResolvedSetting>());
        var privacy = Substitute.For<ILocationPrivacyGovernanceService>();
        privacy.ResolveAsync(fixture.TenantId, Arg.Any<CancellationToken>()).Returns(
            new EffectiveLocationPrivacyGovernance(true, LocationPrivacyGovernanceReasonCode.Resolved,
                true, true, true, LocationDisclosureAudienceEnum.Never, revealOffset ?? TimeSpan.Zero));
        return new(fixture.Context, privacy, new AtprotoEventGovernanceResolver(settings));
    }

    private static EventSession AddSession(
        EventVisitorCapabilitySqliteFixture fixture, string title, DateTimeOffset start, DateTimeOffset end)
    {
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            Title = title,
            PublicCode = Guid.CreateVersion7().ToString("N"),
            ActorId = fixture.ActorId,
            Actor = null!,
            OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC",
            CreatedAt = Now.UtcDateTime
        };
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            EventId = entity.Id,
            Event = entity,
            StartTime = start,
            EndTime = end,
            EndTimeType = SessionEndTimeType.Fixed
        };
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        fixture.Context.Events.Add(entity);
        fixture.Context.EventSessions.Add(session);
        return session;
    }
}
