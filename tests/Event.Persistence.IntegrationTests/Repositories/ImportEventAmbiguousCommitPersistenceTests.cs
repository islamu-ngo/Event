using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Caching;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Services.Lifecycle;
using Explore.Application.Services;
using Explore.Persistence;
using Microsoft.Extensions.Configuration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("PersistenceDb")]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ImportEventAmbiguousCommitPersistenceTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Handle_WhenFirstAttemptPersists_RetryReturnsCommittedImport()
    {
        using var context = fixture.CreateDbContext();
        TenantStatus? activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Ambiguous Import Tenant",
            Slug = $"ambiguous-import-{Guid.NewGuid():N}",
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"ambiguous-import-{Guid.NewGuid():N}@example.com",
                FirstName = "Import",
                LastName = "Owner"
            }
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Import Owner" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var repository = new EventRepository(context);
        var cache = Substitute.For<HybridCache>();
        var policyProvider = Substitute.For<IEventLifecyclePolicyProvider>();
        policyProvider
            .GetEffectivePolicyAsync(tenant.Id, ValidationProfile.EventImportCreate, Arg.Any<CancellationToken>())
            .Returns(new EventLifecyclePolicy
            {
                Profile = ValidationProfile.EventImportCreate,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status,
                    EventFieldKey.ProvenanceSource,
                    EventFieldKey.ProvenanceExternalId
                },
                RequiredSessionFields = new HashSet<Enum>()
            });
        var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        var systemSettings = new SystemSettingRepository(context, mutationLock);
        var visitorCapabilities = new VisitorAccessCapabilityResolver(systemSettings,
            new TenantSettingRepository(context, mutationLock),
            new VisitorAccessProviderReader(systemSettings, new ConfigurationBuilder().Build()));
        var handler = new ImportEventCommandHandler(
            repository,
            Substitute.For<IStorageObjectRepository>(),
            new TwoPassUnitOfWork(),
            cache,
            policyProvider,
            new EventLifecycleReadinessEvaluator(),
            new FixedTimeProvider(Now),
            mutationLock,
            visitorCapabilities);
        var request = new ImportEventRequestDto
        {
            Title = "Committed import",
            OwnerActorId = actor.Id,
            ProvenanceSource = "integration-test",
            ProvenanceExternalId = Guid.NewGuid().ToString("N"),
            ParticipationConfiguration = new ConfigureEventParticipationDto
            {
                ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
            }
        };

        var result = await handler.Handle(new ImportEventCommand { Request = request, TenantId = tenant.Id }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await context.Events.CountAsync(entity => entity.Id == result.Id)).IsEqualTo(1);
        await Assert.That(await context.EventParticipationConfigurations.CountAsync(configuration => configuration.Id == result.Id)).IsEqualTo(1);
        await cache.Received(1).RemoveByTagAsync(CacheTags.EventListByTenant(tenant.Id), Arg.Any<CancellationToken>());
    }

    private sealed class TwoPassUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            ExecuteInTransactionAsync<object?>(async token =>
            {
                await operation(token);
                return null;
            }, ct);

        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            await operation(ct);
            return await operation(ct);
        }

        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            ExecuteInTransactionAsync(operation, ct);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
