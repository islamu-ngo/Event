using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Services.Federation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class PublicEventResponseClockNativeContractTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2040, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RuntimeClockRetainsTransactionWithoutRawSessionPrivileges()
    {
        var seeded = await SeedAsync();
        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using (var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That(await ReadAsync(reader)).IsEqualTo(Now.AddSeconds(20));
            await Assert.That(reader.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(reader.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await transaction.RollbackAsync();
        }
        var names = new HumanPrincipalSourceSql(reader);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {names.Table(typeof(EventSession))} LIMIT 1"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        denied = await Assert.ThrowsAsync<PostgresException>(() =>
            reader.Database.ExecuteSqlRawAsync($"SET ROLE {PostgresPublicEventResponseClockContract.OwnerRole}"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task StrictFutureClockIgnoresHiddenForeignAndPastSources()
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            var session = await writer.EventSessions.SingleAsync(value => value.Id == seeded.Session);
            session.StartTime = Now.AddMinutes(-1);
            session.EndTime = Now;
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            await writer.SaveChangesAsync();
        }
        await using (var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(reader)).IsNull();
        await using var foreign = Open(PrimaryDatabaseRole.Runtime, Guid.CreateVersion7());
        await Assert.That(await ReadAsync(foreign)).IsNull();
    }

    [Test]
    public async Task MissingLocationsDoNotRequireResolvedPrivacyGovernance()
    {
        var seeded = await SeedAsync();
        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        var privacy = Substitute.For<ILocationPrivacyGovernanceService>();
        privacy.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<EffectiveLocationPrivacyGovernance>(new InvalidOperationException("No location governance expected.")));
        await Assert.That(await ReadAsync(reader, privacy)).IsEqualTo(Now.AddSeconds(20));
    }

    [Test]
    public async Task RevealComponentsPreserveSubmicrosecondOffsetAndFailClosedGovernance()
    {
        var seeded = await SeedAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                TenantId = seeded.Tenant,
                FullName = "Public venue",
                City = "Brussels",
                Country = "BE"
            };
            location.SetManualAddress("Public venue address", "1000");
            writer.Add(location);
            var carrier = EventLocation.CreatePhysical(seeded.Tenant, seeded.Event, location.Id,
                seeded.User, Now.AddHours(-1).UtcDateTime);
            carrier.ChangeDisclosurePolicy(EventLocationDisclosureFields.StreetAddress,
                LocationDisclosureAudienceEnum.Never, Now.AddSeconds(5).UtcDateTime,
                carrier.PolicyVersion, seeded.User, EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                Now.UtcDateTime, needsPrivacyReview: false);
            writer.Add(carrier);
            await writer.SaveChangesAsync();
        }
        var privacy = Substitute.For<ILocationPrivacyGovernanceService>();
        privacy.ResolveAsync(seeded.Tenant, Arg.Any<CancellationToken>()).Returns(
            new EffectiveLocationPrivacyGovernance(true, LocationPrivacyGovernanceReasonCode.Resolved,
                true, true, true, LocationDisclosureAudienceEnum.Never, TimeSpan.FromHours(1) + TimeSpan.FromTicks(1)));
        await using (var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(reader, privacy)).IsEqualTo(Now.AddTicks(1));
        privacy.ResolveAsync(seeded.Tenant, Arg.Any<CancellationToken>()).Returns(
            new EffectiveLocationPrivacyGovernance(true, LocationPrivacyGovernanceReasonCode.Resolved,
                true, true, true, LocationDisclosureAudienceEnum.Never, TimeSpan.Zero));
        await using (var explicitReveal = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(explicitReveal, privacy)).IsEqualTo(Now.AddSeconds(5));
        privacy.ResolveAsync(seeded.Tenant, Arg.Any<CancellationToken>()).Returns(
            new EffectiveLocationPrivacyGovernance(true, LocationPrivacyGovernanceReasonCode.Resolved,
                false, false, false, LocationDisclosureAudienceEnum.Never, TimeSpan.Zero));
        await using (var disabled = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(disabled, privacy)).IsEqualTo(Now.AddSeconds(20));
        privacy.ResolveAsync(seeded.Tenant, Arg.Any<CancellationToken>()).Returns(
            new EffectiveLocationPrivacyGovernance(false, LocationPrivacyGovernanceReasonCode.Resolved,
                false, false, false, LocationDisclosureAudienceEnum.Never, TimeSpan.Zero));
        await using var unresolved = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.ThrowsAsync<EventDiscoveryUnavailableException>(() => ReadAsync(unresolved, privacy));
    }

    [Test]
    public async Task OpenEndedSessionStartAndPublishedDayRemainClockBoundaries()
    {
        var seeded = await SeedAsync();
        Guid dayId = Guid.CreateVersion7();
        await using (var writer = fixture.CreateDbContext())
        {
            var session = await writer.EventSessions.SingleAsync(value => value.Id == seeded.Session);
            session.StartTime = Now.AddSeconds(7);
            session.EndTime = null;
            session.EndTimeType = SessionEndTimeType.OpenEnded;
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            writer.Add(new EventDay
            {
                Id = dayId,
                TenantId = seeded.Tenant,
                Tenant = null!,
                EventId = seeded.Event,
                Event = null!,
                LocalDate = DateOnly.FromDateTime(Now.UtcDateTime),
                IsPublished = true,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            session.EventDayId = dayId;
            await writer.SaveChangesAsync();
        }
        await using (var published = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(published)).IsEqualTo(Now.AddSeconds(7));
        await using (var writer = fixture.CreateDbContext())
        {
            (await writer.EventDays.SingleAsync(value => value.Id == dayId)).IsPublished = false;
            await writer.SaveChangesAsync();
        }
        await using var unpublished = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.That(await ReadAsync(unpublished)).IsNull();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task OtherPhysicalLoginsCannotBorrowClock(PrimaryDatabaseRole role)
    {
        var seeded = await SeedAsync();
        await using var reader = Open(role, seeded.Tenant);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(reader));
    }

    [Test]
    public async Task NonserializableTransactionAndSubstitutedOwnerAreRejected()
    {
        var seeded = await SeedAsync();
        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using var transaction = await reader.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReadAsync(reader));
        await transaction.RollbackAsync();
        await using var replacement = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAsync(replacement, binding: Binding(reader)));
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(reader, seeded.Tenant, PolicyMutationPurpose.EmailDelivery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            _ = new PublicEventDiscoveryReadBinding(reader, lease, fixture.NativeQueueEnrollment);
            return Task.CompletedTask;
        });
    }

    private Task<DateTimeOffset?> ReadAsync(ExploreDbContext context,
        ILocationPrivacyGovernanceService? privacy = null, PublicEventDiscoveryReadBinding? binding = null)
    {
        var settings = Substitute.For<IHierarchicalSettingsReader>();
        settings.ResolveBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ResolvedSetting>());
        privacy ??= Substitute.For<ILocationPrivacyGovernanceService>();
        return PublicEventResponseClockProviderOperations.GetNextAsync(context, binding ?? Binding(context),
            privacy, new AtprotoEventGovernanceResolver(settings), Now, default);
    }

    private PublicEventDiscoveryReadBinding Binding(ExploreDbContext context) =>
        new(context, new ExploreDbContextExecutionLease(), fixture.NativeQueueEnrollment);

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenant)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        builder.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        builder.UseNpgsql(options => options.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        return new(builder.Options) { TenantContext = new SelectedTenant(tenant) };
    }

    private async Task<(Guid Tenant, Guid Event, Guid Session, Guid User)> SeedAsync()
    {
        await using var writer = fixture.CreateDbContext();
        await PostgresPublicEventResponseClockContract.ApplyAsync(writer, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Public clock",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), Pii = null! };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Public organizer" }
        };
        writer.AddRange(tenant, user, actor, new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        var visible = Add(VisibilityTypeEnum.Public, Now.AddMinutes(-1), Now.AddSeconds(20));
        Add(VisibilityTypeEnum.Private, Now.AddSeconds(1), Now.AddSeconds(2));
        await writer.SaveChangesAsync();
        return (tenant.Id, visible.EventId, visible.Id, user.Id);

        EventSession Add(VisibilityTypeEnum visibility, DateTimeOffset start, DateTimeOffset end)
        {
            var entity = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                ActorId = actor.Id,
                Actor = actor,
                Title = "Public clock",
                PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
                VisibilityTypeId = (int)visibility,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                Timezone = "UTC",
                CreatedAt = Now.UtcDateTime
            };
            var session = new EventSession(EventSessionStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                EventId = entity.Id,
                Event = entity,
                StartTime = start,
                EndTime = end,
                EndTimeType = SessionEndTimeType.Fixed
            };
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            writer.AddRange(entity, session);
            return session;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
