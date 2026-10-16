using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class SupportAccessTemporalSqliteTests
{
    [Test]
    [Arguments(0, "before", false)]
    [Arguments(0, "start", true)]
    [Arguments(0, "last", true)]
    [Arguments(0, "expiry", false)]
    [Arguments(210, "before", false)]
    [Arguments(210, "start", true)]
    [Arguments(210, "last", true)]
    [Arguments(210, "expiry", false)]
    public async Task ActiveReadersPreserveUtcOffsetsAndSingleTickBoundaries(
        int offsetMinutes, string boundary, bool active)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var started = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero)
            .AddTicks(1234567).ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        var expires = started.AddMinutes(5);
        var session = SupportAccessSession.Start(fixture.UserId, fixture.TenantId,
            SupportAccessModeEnum.ReadOnly, "precision", "UTC tick boundary",
            "fixture", started, expires);
        fixture.Context.SupportAccessSessions.Add(session);
        await fixture.Context.SaveChangesAsync();
        var now = boundary switch
        {
            "before" => started.ToUniversalTime().AddTicks(-1),
            "start" => started.ToUniversalTime(),
            "last" => expires.ToUniversalTime().AddTicks(-1),
            "expiry" => expires.ToUniversalTime(),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var repository = new SupportAccessSessionRepository(fixture.Context);

        var selected = await repository.GetActiveForActorAsync(fixture.UserId, now);
        var owned = await repository.GetActiveOwnedSessionAsync(
            session.Id, fixture.UserId, fixture.TenantId, now);
        bool exists = await repository.HasActiveSessionForActorAsync(fixture.UserId, now);

        await Assert.That(selected?.Id).IsEqualTo(active ? session.Id : (Guid?)null);
        await Assert.That(owned?.Id).IsEqualTo(active ? session.Id : (Guid?)null);
        await Assert.That(exists).IsEqualTo(active);
        await Assert.That(await repository.GetActiveOwnedSessionAsync(
            session.Id, Guid.CreateVersion7(), fixture.TenantId, now)).IsNull();
        await Assert.That(await repository.GetActiveOwnedSessionAsync(
            session.Id, fixture.UserId, Guid.CreateVersion7(), now)).IsNull();
    }
}
