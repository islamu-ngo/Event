using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocationAddressPromotionConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Guid TenantId = Guid.Parse("019b0000-0024-7000-8000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("019b0000-0024-7000-8000-000000000002");
    private static readonly Guid CreatorId = Guid.Parse("019b0000-0024-7000-8000-000000000003");
    private static readonly Guid FirstActorId = Guid.Parse("019b0000-0024-7000-8000-000000000004");
    private static readonly Guid SecondActorId = Guid.Parse("019b0000-0024-7000-8000-000000000005");
    private static readonly DateTime FirstChangedAtUtc = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondChangedAtUtc = new(2026, 8, 26, 12, 0, 1, DateTimeKind.Utc);

    [Test]
    public async Task TwoSynchronizedPromotionsHaveOneWinnerAndOneStaleResult()
    {
        await fixture.ResetAsync();
        LocationSnapshot before = await SeedAsync();
        var bothLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrivals = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        Task<Exception?> first = PromoteAsync(
            FirstActorId,
            FirstChangedAtUtc,
            bothLoaded,
            release,
            () => Interlocked.Increment(ref arrivals),
            timeout.Token);
        Task<Exception?> second = PromoteAsync(
            SecondActorId,
            SecondChangedAtUtc,
            bothLoaded,
            release,
            () => Interlocked.Increment(ref arrivals),
            timeout.Token);

        await bothLoaded.Task.WaitAsync(timeout.Token);
        release.TrySetResult();
        Exception?[] outcomes = await Task.WhenAll(first, second).WaitAsync(timeout.Token);

        await Assert.That(outcomes.Count(outcome => outcome is null)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome is ConcurrencyConflictException
            { Code: ConcurrencyConflictException.ConcurrentUpdate })).IsEqualTo(1);

        await using ExploreDbContext verification = fixture.CreateDbContext();
        Location saved = await verification.Locations
            .AsNoTracking()
            .Include(location => location.Pii)
            .SingleAsync(location => location.Id == LocationId, timeout.Token);
        LocationSnapshot after = Snapshot(saved);

        await Assert.That(after).IsEqualTo(before with
        {
            Visibility = LocationAddressVisibilityEnum.TenantApproved,
            UpdatedAt = after.UpdatedAt,
            UpdatedBy = after.UpdatedBy,
            ConcurrencyStamp = after.ConcurrencyStamp
        });
        await Assert.That(after.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        await Assert.That(after.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(after.UpdatedBy == FirstActorId || after.UpdatedBy == SecondActorId).IsTrue();
        await Assert.That(after.UpdatedAt == FirstChangedAtUtc || after.UpdatedAt == SecondChangedAtUtc).IsTrue();

        void SignalLoaded()
        {
            if (Interlocked.CompareExchange(ref arrivals, 0, 0) == 2)
            {
                bothLoaded.TrySetResult();
            }
        }

        async Task<Exception?> PromoteAsync(
            Guid actorId,
            DateTime changedAtUtc,
            TaskCompletionSource loaded,
            TaskCompletionSource releaseGate,
            Func<int> arrive,
            CancellationToken cancellationToken)
        {
            await using ExploreDbContext context = fixture.CreateDbContext();
            var repository = new LocationRepository(context);
            Location location = await repository.GetById(LocationId, cancellationToken)
                ?? throw new InvalidOperationException("Seeded promotion target was not found.");
            arrive();
            SignalLoaded();
            await releaseGate.Task.WaitAsync(cancellationToken);
            location.PromoteAddressToTenantApproved(actorId, changedAtUtc);
            try
            {
                await repository.Update(location, cancellationToken);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    private async Task<LocationSnapshot> SeedAsync()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        TenantStatus status = await context.TenantStatuses
            .SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = TenantId,
            FullName = "Address promotion tenant",
            Slug = "address-promotion-tenant",
            TenantStatusId = status.Id,
            TenantStatus = status,
            CreatedAt = DateTime.UnixEpoch
        };
        var location = new Location
        {
            Id = LocationId,
            TenantId = TenantId,
            Tenant = tenant,
            FullName = "Promotion venue",
            Country = "BE",
            City = "Brussels",
            Timezone = "Europe/Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Guid.Parse("019b0000-0024-7000-8000-000000000006")
        };
        location.SetProviderAddress(
            "Synthetic promotion address",
            "1000",
            GeoCoordinate.Create(50.8503, 4.3517));
        location.ApplyAddressGovernance(
            CreatorId,
            LocationAddressSourceEnum.ProviderSelection,
            LocationAddressVisibilityEnum.CreatorPrivate,
            null);
        context.AddRange(tenant, location);
        await context.SaveChangesAsync();
        return Snapshot(location);
    }

    private static LocationSnapshot Snapshot(Location location) => new(
        location.TenantId,
        location.FullName,
        location.Country,
        location.City,
        location.Timezone,
        location.LocationKindId,
        location.LocationPrivacyStateId,
        location.OwnerUserId,
        location.AddressSource,
        location.AddressVisibility,
        location.AddressOrganizationId,
        location.CreatedAt,
        location.CreatedBy,
        location.Address,
        location.Postcode,
        location.Pii?.Latitude,
        location.Pii?.Longitude,
        location.UpdatedAt,
        location.UpdatedBy,
        location.ConcurrencyStamp);

    private sealed record LocationSnapshot(
        Guid TenantId,
        string FullName,
        string Country,
        string City,
        string? Timezone,
        int LocationKindId,
        int LocationPrivacyStateId,
        Guid? OwnerUserId,
        LocationAddressSourceEnum Source,
        LocationAddressVisibilityEnum Visibility,
        Guid? OrganizationId,
        DateTime CreatedAt,
        Guid? CreatedBy,
        string? Address,
        string? Postcode,
        double? Latitude,
        double? Longitude,
        DateTime? UpdatedAt,
        Guid? UpdatedBy,
        Guid ConcurrencyStamp);
}
