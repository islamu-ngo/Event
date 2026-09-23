using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Authorization;

public sealed class EventResourceParentModerationFactsTests
{
    private static readonly DateTimeOffset Now = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FrozenNativeCollectionsUseStructuralEqualityAndDoNotBorrowMutableInputs()
    {
        Guid user = Guid.CreateVersion7(), tenant = Guid.CreateVersion7(), other = Guid.CreateVersion7();
        Guid[] tenants = [tenant, other];
        var first = Principal(user, tenants);
        var equivalent = Principal(user, [other, tenant, tenant]);
        tenants[0] = Guid.CreateVersion7();
        await Assert.That(first == equivalent).IsTrue();
        await Assert.That(first.GetHashCode()).IsEqualTo(equivalent.GetHashCode());
        await Assert.That(first.AdminTenantIds.Contains(tenant)).IsTrue();
        await Assert.That(first == first with { AdminTenantIds = new([tenant]) }).IsFalse();
        await Assert.That(first == first with { EventFinanceGroupIds = new([other]) }).IsFalse();
        var repeated = first with { EventCreateOrganizationIds = new([tenant, other, tenant]) };
        await Assert.That(repeated == first with { EventCreateOrganizationIds = new([tenant, tenant, other]) }).IsTrue();
        await Assert.That(repeated == first with { EventCreateOrganizationIds = new([tenant, other]) }).IsFalse();
    }

    [Test]
    [Arguments("download", false)]
    [Arguments("access", false)]
    [Arguments("export", false)]
    [Arguments("publish", false)]
    [Arguments("update", false)]
    [Arguments("moderate", true)]
    public async Task NativeModeratorDoesNotGainOrganizerContentOrExportAuthority(string action, bool expected)
    {
        Guid user = Guid.CreateVersion7(), tenant = Guid.CreateVersion7(), parentId = Guid.CreateVersion7(), id = Guid.CreateVersion7();
        var resource = EventResource.CreateDraft(id, tenant, parentId, null,
            new() { Title = "Restricted", Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly },
            EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(tenant, parentId, id, EventResourceAudienceKindEnum.Organizer)], user, Now.UtcDateTime);
        resource.SetStoredFile(Guid.CreateVersion7(), resource.ConcurrencyStamp, user, Now.UtcDateTime);
        var parent = new EventResourceParentFacts(tenant, parentId, null, EventStatusEnum.Published,
            false, true, null, false, new(Now, Now.AddHours(1), null, null));
        var frozen = new EventResourceParentModerationFacts(new(Principal(user, [tenant]), tenant, [parentId], []),
            new(tenant, parentId, Guid.CreateVersion7(), user, null, null, null, null, null, null, "native", user));
        var facts = new EventResourceAuthorizationFacts(resource, new(tenant, user, false, parent, [], true),
            new(new(false), new(true), new(true), [], managementCeiling: true, publicationCeiling: true), "attachment", frozen);
        var route = new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "resource-scope", "1",
            ParentEventPolicy: new("parent-scope"));
        var decision = facts.Evaluate(new(tenant, id, user, false, action), route, Now);
        await Assert.That(decision.Allowed).IsEqualTo(expected);
        await Assert.That(decision.ProviderInput!.Principal.ControlsOrganizer).IsFalse();
        await Assert.That(decision.ProviderInput.Principal.HasEventUpdate).IsFalse();
        await Assert.That(decision.ProviderInput.Principal.HasEventPublish).IsFalse();
        await Assert.That(decision.ProviderInput.ParentModeration is not null).IsEqualTo(action == "moderate");
    }

    [Test]
    public async Task EffectiveAssignmentChangesInvalidateTheFrozenProviderInputAtTheFinalClockGate()
    {
        Guid user = Guid.CreateVersion7(), tenant = Guid.CreateVersion7(), parent = Guid.CreateVersion7();
        var facts = new EventResourceParentModerationFacts(new(Principal(user, [tenant]), tenant, [parent],
            [new(parent, "event.manager", new(["event:update"]), new(true, Now.AddMinutes(-1), Now.AddSeconds(1)))]),
            new(tenant, parent, Guid.CreateVersion7(), user, null, null, null, null, null, null, "native", user));
        var before = facts.Evaluate(new(""), Now);
        var same = facts.Evaluate(new(""), Now.AddMilliseconds(999));
        var expired = facts.Evaluate(new(""), Now.AddSeconds(1));
        await Assert.That(before == same).IsTrue();
        await Assert.That(before == expired).IsFalse();
        await Assert.That(expired.Principal.CanModerate(tenant)).IsTrue();
        await Assert.That(expired.Principal.EventAssignments.Single().Permissions).IsEmpty();
    }

    private static EventModerationPrincipal Principal(Guid user, IEnumerable<Guid> tenants) => new(user, false,
        new(tenants), new([]), new([]), new([]), new([]), new([]), new([]), new([]));
}
