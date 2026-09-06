// ABOUTME: Verifies runtime-seeder parity for the complete registration approval lifecycle vocabulary.
// ABOUTME: Locks stable IDs and codes while proving missing terminal rows are repaired idempotently.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Seed;

[Category("EventLocationPrivacy")]
public sealed class ApprovalStatusLookupSeederTests
{
    [Test]
    public async Task RuntimeSeederMaintainsExactApprovalStatusIdsAndCodes()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"approval-status-lookups-{Guid.NewGuid():N}")
            .Options;

        await using var context = new ExploreDbContext(options);
        await LookupTableSeeder.SeedApprovalStatusesAsync(context, default);
        await AssertExactRowsAsync(context);

        context.Set<ApprovalStatus>().Remove(await context.Set<ApprovalStatus>().SingleAsync(
            row => row.Id == (int)ApprovalStatusEnum.Cancelled));
        context.Set<ApprovalStatus>().Remove(await context.Set<ApprovalStatus>().SingleAsync(
            row => row.Id == (int)ApprovalStatusEnum.Revoked));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedApprovalStatusesAsync(context, default);
        await LookupTableSeeder.SeedApprovalStatusesAsync(context, default);

        await AssertExactRowsAsync(context);
    }

    private static async Task AssertExactRowsAsync(ExploreDbContext context)
    {
        var rows = await context.Set<ApprovalStatus>()
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.MasterCode })
            .ToArrayAsync();

        await Assert.That(rows.Select(row => (row.Id, row.MasterCode)).SequenceEqual(
        [
            (1, "PENDING"),
            (2, "APPROVED"),
            (3, "REJECTED"),
            (4, "WAITLISTED"),
            (5, "CANCELLED"),
            (6, "REVOKED")
        ])).IsTrue();
    }
}
