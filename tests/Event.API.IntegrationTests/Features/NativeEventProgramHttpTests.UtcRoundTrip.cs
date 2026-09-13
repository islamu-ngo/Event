using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventProgramHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SqliteLocationUtcRoundTrip_PreservesCreationAndOptionalReveal(bool explicitReveal)
    {
        DateTime? reveal = explicitReveal ? new DateTime(2026, 8, 1, 10, 15, 0, DateTimeKind.Utc) : null;
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true, revealFullDetailsFromUtc: reveal);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventLocationRepository>();
        var placement = (await repository.GetByEventIdAsync(factory.PublicId, default)).Single();
        if (explicitReveal)
        {
            await Assert.That(placement.RevealFullDetailsFromUtc?.Kind).IsEqualTo(DateTimeKind.Utc);
            await Assert.That(placement.RevealFullDetailsFromUtc?.Ticks)
                .IsEqualTo(new DateTime(2026, 8, 1, 10, 15, 0, DateTimeKind.Utc).Ticks);
        }
        else
        {
            await Assert.That(placement.RevealFullDetailsFromUtc).IsNull();
        }
        await Assert.That(placement.CreatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(placement.CreatedAt.Ticks).IsEqualTo(new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc).Ticks);
        await Assert.That(scope.ServiceProvider.GetRequiredService<ExploreDbContext>().ChangeTracker.Entries<EventLocation>()).IsEmpty();
        var batch = (await repository.GetByIdsAsync([placement.Id], default)).Single();
        await Assert.That(batch.CreatedAt.Kind).IsEqualTo(DateTimeKind.Utc);
        await Assert.That(batch.RevealFullDetailsFromUtc?.Kind).IsEqualTo(reveal?.Kind);
        using var anonymous = factory.CreateClient();
        var summary = await SummaryAsync(anonymous, Public(factory.PublicId));
        await Assert.That(Groups(summary).First().GetProperty("eventLocation").GetProperty("fields")
            .GetProperty("venueName").GetString()).IsEqualTo("Approved venue");
    }

    private static async Task RequireVenuePrivacyReviewAsync(ProgramFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventLocationRepository>();
        var existing = (await repository.GetByEventIdAsync(factory.PublicId, default)).Single();
        var placement = await repository.GetForUpdateAsync(existing.Id, default)
            ?? throw new InvalidOperationException("Seeded placement is missing.");
        var audit = placement.ApplyGovernanceTightening(true, factory.OwnerId,
            new DateTime(2026, 8, 2, 10, 15, 0, DateTimeKind.Utc));
        scope.ServiceProvider.GetRequiredService<ExploreDbContext>().EventLocationDisclosureAudits.Add(audit);
        await repository.SaveChangesAsync(default);
    }
}
