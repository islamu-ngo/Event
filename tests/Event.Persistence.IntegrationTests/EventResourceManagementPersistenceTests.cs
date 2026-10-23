using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed partial class EventResourceManagementPersistenceTests(EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task LostCreateResponseCannotDuplicateResourceOrAudit()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext();
        var workflow = Workflow(context, scope.TenantAId, actor);
        var id = Guid.CreateVersion7();
        var first = await workflow.CreateAsync(scope.EventAId, id, Draft(), default);
        var replay = await workflow.CreateAsync(scope.EventAId, id, Draft(), default);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsFalse();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.CountAsync(value => value.Id == id)).IsEqualTo(1);
        await Assert.That(await verify.EventResourceAuditEntries.CountAsync(value => value.EventResourceId == id)).IsEqualTo(1);
    }

    [Test]
    public async Task RequiredAuditFailureRollsBackCreation()
    {
        var (scope, actor) = await SeedAsync();
        var interceptor = new AuditFailure();
        await using var context = database.CreateContext(interceptor);
        var id = Guid.CreateVersion7();
        await Assert.That(async () => await Workflow(context, scope.TenantAId, actor)
            .CreateAsync(scope.EventAId, id, Draft(), default)).Throws<DbUpdateException>();
        await Assert.That(interceptor.ResourceWriteExecuted).IsTrue();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.AnyAsync(value => value.Id == id)).IsFalse();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == id)).IsFalse();
    }

    [Test]
    public async Task ZeroRetentionDoesNotCollectAudit()
    {
        var (scope, actor) = await SeedAsync();
        await using var context = database.CreateContext(new AuditFailure());
        var id = Guid.CreateVersion7();
        var result = await Workflow(context, scope.TenantAId, actor, Policy(retention: 0))
            .CreateAsync(scope.EventAId, id, Draft(), default);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var verify = database.CreateContext();
        await Assert.That(await verify.EventResources.AnyAsync(value => value.Id == id)).IsTrue();
        await Assert.That(await verify.EventResourceAuditEntries.AnyAsync(value => value.EventResourceId == id)).IsFalse();
    }

    private async Task<(EventResourcePersistenceTests.ResourceScope Scope, Guid Actor)> SeedAsync(
        DateTime? eventPublishedAt = null)
    {
        var scope = await database.SeedScopeAsync(eventPublishedAt);
        await using var seed = database.CreateContext();
        var actor = (await seed.Actors.SingleAsync(value => value.Id == scope.ActorId)).UserId!.Value;
        var parent = await seed.Events.SingleAsync(value => value.Id == scope.EventAId);
        parent.OrganizerActorId = scope.ActorId;
        seed.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantAId,
            Tenant = null!,
            UserId = actor,
            User = null!,
            ActorId = scope.ActorId,
            StatusId = (int)TenantUserStatusEnum.Active,
            CreatedAt = Now
        });
        await seed.SaveChangesAsync();
        return (scope, actor);
    }

    private static async Task<T> DiscloseAsync<T>(EventResourceManagementReadResult<T> result) where T : class
    {
        await Assert.That(result.Projection).IsNotNull();
        T? released = null;
        var outcome = await result.Projection!.DiscloseAsync((value, _) => Task.FromResult(value), (value, _) =>
        {
            released = value;
            return Task.CompletedTask;
        });
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        return released!;
    }

    private EventResourceManagementWorkflow Workflow(ExploreDbContext context, Guid tenantId, Guid actor,
        EventResourceGovernancePolicy? policy = null, IEventResourceAuthorizationProvider? provider = null,
        IUnitOfWork? unitOfWork = null, TimeProvider? clock = null)
    {
        var repository = new EventResourceRepository(context);
        var unit = unitOfWork ?? new EfCoreUnitOfWork(context);
        clock ??= new Clock();
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(tenantId, Arg.Any<CancellationToken>()).Returns(policy ?? Policy());
        var routes = Substitute.For<IEventResourceProviderSnapshotReader>();
        routes.ReadAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new EventResourceProviderSnapshot(EventResourceProviderMode.Local, "", "default"));
        if (provider is null)
        {
            provider = Substitute.For<IEventResourceAuthorizationProvider>();
            provider.CheckAsync(Arg.Any<EventResourceProviderInput>(), Arg.Any<CancellationToken>())
                .Returns(EventResourceProviderDecision.Allow);
            provider.CheckBatchAsync(Arg.Any<IReadOnlyList<EventResourceProviderInput>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<IReadOnlyList<EventResourceProviderInput>>()
                    .Select(_ => EventResourceProviderDecision.Allow).ToArray());
        }
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        context.TenantContext = tenant;
        var user = Substitute.For<ICurrentUserService>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(actor);
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString("D"))], "event-resource-native"))
            }
        };
        var identity = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new UserExternalLoginRepository(context),
            NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        return new(repository, unit, new EventResourceStorageLifecycleService(
            new EventResourceStorageLifecycleRepository(context), unit, clock), new(unit,
            new EventResourceAuthoritySnapshotReader(repository, new EventAuthoritySnapshotService(context), governance,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                Substitute.For<IConfiguredApiKeyResourceAuthorityReader>(),
                new Explore.Persistence.Services.EventResourceModerationSnapshotReader(context,
                    Substitute.For<Explore.Application.Contracts.Persistence.IModerationMachineSubjectBindingRepository>(),
                    global::Event.Persistence.IntegrationTests.Moderation.ModerationRecognitionResourceFixture.Reader(context))),
            routes, provider, clock), tenant, user, machine, clock,
            new EventPublicationMutationBoundary(context, database.Privacy.Authority(context, clock),
                new UserContext(http), identity, machine, clock,
                global::Event.Persistence.IntegrationTests.Moderation.ModerationRecognitionResourceFixture.Reader(context)));
    }

    private static EventResourceDraftDto Draft() => new()
    {
        Title = "Protected material",
        Kind = (EventResourceKindEnum)1,
        DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly,
        DeliveryType = EventResourceDeliveryTypeEnum.StoredFile,
        AudienceRules = [new(EventResourceAudienceKindEnum.Public)]
    };

    private static EventResourceGovernancePolicy Policy(int retention = 30, int capacity = 500,
        string[]? origins = null, bool allowUnscanned = false) =>
        EventResourceGovernancePolicy.Create(Enum.GetValues<EventResourceDeliveryTypeEnum>(),
            Enum.GetValues<EventResourceAudienceKindEnum>(), [EventResourceGovernancePolicy.PdfMediaType],
            10_485_760, allowUnscanned, origins ?? [], retention, capacity, long.MaxValue);

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private sealed class AuditWriteException : InvalidOperationException;
    private sealed class AuditFailure : DbCommandInterceptor
    {
        public bool ResourceWriteExecuted { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"ie_event_resource_audit_entries\"", StringComparison.Ordinal))
                throw new AuditWriteException();
            return ValueTask.FromResult(result);
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"ie_event_resources\"", StringComparison.Ordinal))
                ResourceWriteExecuted = true;
            return ValueTask.FromResult(result);
        }
    }
}
