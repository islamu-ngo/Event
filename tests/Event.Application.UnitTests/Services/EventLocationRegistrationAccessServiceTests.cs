using System.Collections.Immutable;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public sealed class EventLocationRegistrationAccessServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private readonly EventLocationRegistrationAccessService _service = new();

    [Test]
    public async Task Resolve_ConfirmedOrder_CoversItsAdmittedLocation()
    {
        Guid orderId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        var request = new EventLocationRegistrationAccessRequest(
            locationId,
            Now,
            new(orderId, eventId, (int)RegistrationOrderStatusEnum.Confirmed, false, null),
            [new(orderId, eventId, null, Guid.CreateVersion7(), locationId, null, (int)RegistrationModeEnum.Open, false, null)]);

        EventLocationRegistrationAccess result = _service.Resolve(request);

        await Assert.That(result.OrderId).IsEqualTo(orderId);
        await Assert.That(result.EffectiveState).IsEqualTo(EventLocationRegistrationEffectiveState.Confirmed);
        await Assert.That(result.CoversRequestedEventLocation).IsTrue();
    }

    [Test]
    public async Task Resolve_ConfirmedOrder_DoesNotCoverAnotherLocation()
    {
        Guid orderId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid admittedLocationId = Guid.CreateVersion7();
        var request = new EventLocationRegistrationAccessRequest(
            Guid.CreateVersion7(),
            Now,
            new(orderId, eventId, (int)RegistrationOrderStatusEnum.Confirmed, false, null),
            [new(orderId, eventId, null, Guid.CreateVersion7(), admittedLocationId, null, (int)RegistrationModeEnum.Open, false, null)]);

        EventLocationRegistrationAccess result = _service.Resolve(request);

        await Assert.That(result.CoversRequestedEventLocation).IsFalse();
        await Assert.That(result.EffectiveState).IsEqualTo(EventLocationRegistrationEffectiveState.Denied);
    }

    [Test]
    public async Task Resolve_CancelledOrder_DeniesItsAdmission()
    {
        Guid orderId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        var request = new EventLocationRegistrationAccessRequest(
            locationId,
            Now,
            new(orderId, eventId, (int)RegistrationOrderStatusEnum.Cancelled, false, null),
            [new(orderId, eventId, null, Guid.CreateVersion7(), locationId, null, (int)RegistrationModeEnum.Open, false, null)]);

        EventLocationRegistrationAccess result = _service.Resolve(request);

        await Assert.That(result.EffectiveState).IsEqualTo(EventLocationRegistrationEffectiveState.Cancelled);
        await Assert.That(result.CoversRequestedEventLocation).IsFalse();
    }
}
