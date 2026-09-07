using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocationAddressPromotionCancellationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Guid TenantId = Guid.Parse("019b0000-0025-7000-8000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("019b0000-0025-7000-8000-000000000002");
    private static readonly Guid ActorId = Guid.Parse("019b0000-0025-7000-8000-000000000003");

    [Test]
    public async Task CancellationDuringGetByIdPropagatesAndLeavesDurableRowUnchanged()
    {
        await fixture.ResetAsync();
        LocationSnapshot before = await SeedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new BlockingCommandInterceptor(CommandKind.Select, entered, release);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using ExploreDbContext context = fixture.CreateDbContext(interceptor);
        ILocationRepository repository = new LocationRepository(context);

        Task<Location?> operation = repository.GetById(LocationId, cancellation.Token);
        await entered.Task.WaitAsync(timeout.Token);
        cancellation.Cancel();
        release.TrySetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await operation.WaitAsync(timeout.Token));
        await AssertDurableSnapshotAsync(before, timeout.Token);
    }

    [Test]
    public async Task CancellationDuringSaveChangesPropagatesAndLeavesDurableRowUnchanged()
    {
        await fixture.ResetAsync();
        LocationSnapshot before = await SeedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interceptor = new BlockingCommandInterceptor(CommandKind.Update, entered, release);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using ExploreDbContext context = fixture.CreateDbContext(interceptor);
        ILocationRepository repository = new LocationRepository(context);
        Location location = await repository.GetById(LocationId, timeout.Token)
            ?? throw new InvalidOperationException("Seeded promotion target was not found.");
        location.PromoteAddressToTenantApproved(
            ActorId,
            new DateTime(2026, 8, 26, 14, 0, 0, DateTimeKind.Utc));

        Task operation = repository.Update(location, cancellation.Token);
        await entered.Task.WaitAsync(timeout.Token);
        cancellation.Cancel();
        release.TrySetResult();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await operation.WaitAsync(timeout.Token));
        await AssertDurableSnapshotAsync(before, timeout.Token);
    }

    private async Task<LocationSnapshot> SeedAsync()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        TenantStatus status = await context.TenantStatuses
            .SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            Id = TenantId,
            FullName = "Promotion cancellation tenant",
            Slug = "promotion-cancellation-tenant",
            TenantStatusId = status.Id,
            TenantStatus = status,
            CreatedAt = DateTime.UnixEpoch
        };
        var location = new Location
        {
            Id = LocationId,
            TenantId = TenantId,
            Tenant = tenant,
            FullName = "Cancellation venue",
            Country = "BE",
            City = "Brussels",
            CreatedAt = DateTime.UnixEpoch,
            ConcurrencyStamp = Guid.Parse("019b0000-0025-7000-8000-000000000004")
        };
        location.SetManualAddress("Synthetic cancellation address", "1000");
        location.ApplyAddressGovernance(
            ActorId,
            LocationAddressSourceEnum.Manual,
            LocationAddressVisibilityEnum.CreatorPrivate,
            null);
        context.AddRange(tenant, location);
        await context.SaveChangesAsync();
        return Snapshot(location);
    }

    private async Task AssertDurableSnapshotAsync(
        LocationSnapshot expected,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext verification = fixture.CreateDbContext();
        Location durable = await verification.Locations
            .AsNoTracking()
            .Include(location => location.Pii)
            .SingleAsync(location => location.Id == LocationId, cancellationToken);
        await Assert.That(Snapshot(durable)).IsEqualTo(expected);
    }

    private static LocationSnapshot Snapshot(Location location) => new(
        location.AddressSource,
        location.AddressVisibility,
        location.AddressOrganizationId,
        location.Address,
        location.Postcode,
        location.UpdatedAt,
        location.UpdatedBy,
        location.ConcurrencyStamp);

    private sealed record LocationSnapshot(
        LocationAddressSourceEnum Source,
        LocationAddressVisibilityEnum Visibility,
        Guid? OrganizationId,
        string? Address,
        string? Postcode,
        DateTime? UpdatedAt,
        Guid? UpdatedBy,
        Guid ConcurrencyStamp);

    private enum CommandKind
    {
        Select,
        Update
    }

    private sealed class BlockingCommandInterceptor(
        CommandKind commandKind,
        TaskCompletionSource entered,
        TaskCompletionSource release) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Matches(command.CommandText))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return result;
        }

        private bool Matches(string commandText)
        {
            string trimmed = commandText.TrimStart();
            return commandKind switch
            {
                CommandKind.Select => trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase),
                CommandKind.Update => trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
    }
}
