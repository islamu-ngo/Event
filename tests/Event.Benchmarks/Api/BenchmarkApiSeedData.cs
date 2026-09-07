using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Benchmarks.Api;

internal static class BenchmarkApiSeedData
{
    internal static readonly Guid BenchmarkUserId = Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b6a11");

    private static readonly Guid BenchmarkActorId = Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b6a12");
    private static readonly Guid BenchmarkEventIdStart = Guid.Parse("018f6d10-7b7b-7f20-8c61-3c3e7f1b7000");
    private static readonly DateTime SeedTimestamp = new(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);
    private const string TimezoneId = "Europe/Brussels";
    private const int EventCount = 24;

    public static async Task SeedAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Events.AnyAsync(evt => EF.Functions.Like(evt.Slug, "benchmark-api-event-%"), cancellationToken))
        {
            return;
        }

        await EnsureTenantAsync(context, cancellationToken);
        await EnsureBenchmarkUserAndActorAsync(context, cancellationToken);

        context.Events.AddRange(CreateEvents());
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureTenantAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Tenants.AnyAsync(tenant => tenant.Id == SeedIds.DefaultTenantId, cancellationToken))
        {
            return;
        }

        context.Tenants.Add(new Tenant
        {
            Id = SeedIds.DefaultTenantId,
            FullName = "Benchmark API Tenant",
            Slug = "benchmark-api",
            Description = "Tenant used by Event.Benchmarks API endpoint scenarios.",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = SeedTimestamp
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureBenchmarkUserAndActorAsync(ExploreDbContext context, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == BenchmarkUserId, cancellationToken);
        if (user is null)
        {
            user = new User
            {
                Id = BenchmarkUserId,
                EmailVerified = true,
                CreatedAt = SeedTimestamp,
                Pii = new UserPii
                {
                    Email = "benchmark-api-user@example.test",
                    FirstName = "Benchmark",
                    LastName = "User"
                }
            };
            context.Users.Add(user);
            await context.SaveChangesAsync(cancellationToken);
        }

        if (await context.Actors.AnyAsync(actor => actor.UserId == user.Id, cancellationToken))
        {
            return;
        }

        var actor = new Actor
        {
            Id = BenchmarkActorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = BenchmarkUserId,
            CreatedAt = SeedTimestamp,
            Pii = new ActorPii { DisplayName = "Benchmark API Organizer" }
        };
        var identity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:benchmark-api-organizer"))
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,

            Handle = "benchmark-api-organizer",
            PdsHost = "https://pds.benchmark.example.test",
            IsActive = true,
            LastResolvedAt = SeedTimestamp,
            CreatedAt = SeedTimestamp
        };

        context.AddRange(actor, identity);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<Explore.Domain.Event> CreateEvents()
    {
        for (var index = 0; index < EventCount; index++)
        {
            Guid eventId = IncrementGuid(BenchmarkEventIdStart, index);
            var sessionStartUtc = DateTimeOffset.UtcNow.AddDays(index + 1).AddHours(index % 6);
            var sessionDate = DateOnly.FromDateTime(sessionStartUtc.UtcDateTime);
            bool requiresRegistration = index % 3 == 0;

            yield return new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = eventId,
                Title = $"Benchmark API Event {index + 1:00}",
                Subtitle = "Representative benchmark event",
                Description = "Seeded by Event.Benchmarks so API list benchmarks serialize non-empty event payloads.",
                Content = "This deterministic benchmark event gives API endpoint benchmarks realistic data volume without relying on Development seed data.",
                Slug = $"benchmark-api-event-{index + 1:00}",
                ActorId = BenchmarkActorId,
                Actor = null!,
                TenantId = SeedIds.DefaultTenantId,
                Tenant = null!,
                EventStatus = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                FirstSessionDate = sessionDate,
                LastSessionDate = sessionDate,
                FirstSessionStartUtc = sessionStartUtc,
                LastSessionStartUtc = sessionStartUtc.AddHours(2),
                Timezone = TimezoneId,
                EventTimeZoneId = TimezoneId,
                TotalViews = index * 17,
                ParticipationConfiguration = EventParticipationConfiguration.Create(
                    eventId,
                    SeedIds.DefaultTenantId,
                    requiresRegistration
                        ? (int)ParticipationHandlingModeEnum.PlatformManaged
                        : (int)ParticipationHandlingModeEnum.InformationOnly,
                    requiresRegistration
                        ? (int)AdvanceRegistrationObligationEnum.Required
                        : (int)AdvanceRegistrationObligationEnum.NotApplicable,
                    requiresRegistration ? (int)IdentityAccessModeEnum.AccountRequired : null,
                    guestRecoveryPolicy: null,
                    SeedTimestamp),
                RegistrationPolicyId = requiresRegistration ? (int)EventRegistrationPolicyEnum.SessionSelectionOnly : null,
                SessionCount = 1,
                CreatedAt = SeedTimestamp.AddMinutes(index),
                CreatedBy = BenchmarkUserId,
                ConcurrencyStamp = IncrementGuid(BenchmarkEventIdStart, index + EventCount)
            };
        }
    }

    private static Guid IncrementGuid(Guid baseGuid, int offset)
    {
        var bytes = baseGuid.ToByteArray();
        var value = BitConverter.ToInt32(bytes, 0);
        BitConverter.GetBytes(value + offset).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
