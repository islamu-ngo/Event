using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventInitialCreationMutationTests
{
    [Test]
    [Arguments("user", false)]
    [Arguments("user", true)]
    [Arguments("organization", false)]
    [Arguments("organization", true)]
    [Arguments("group", false)]
    [Arguments("group", true)]
    public async Task Organizer_creation_commits_parent_participation_and_exact_original_human_owner(string publisher, bool absentHost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, publisher, absentHost: absentHost);
        var session = Session(database, context);

        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(creation.EventId);
        await using var inspect = database.Context();
        await AssertCommittedAsync(inspect, creation, absentHost);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Community_report_commits_the_parent_without_manufacturing_any_owner(bool absentHost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "user", community: true, absentHost: absentHost);
        var session = Session(database, context);

        var result = await session.Boundary.ExecuteCreationAsync(creation, (_, _) => Task.FromResult(true),
            (creator, token) => InsertAsync(context, creation, creator, token), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await using var inspect = database.Context();
        await AssertCommittedAsync(inspect, creation, absentHost);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("organization", false)]
    [Arguments("organization", true)]
    [Arguments("group", false)]
    [Arguments("group", true)]
    public async Task Actual_terminal_publisher_source_loss_rolls_back_parent_participation_owner_and_epoch(string publisher, bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var creation = await PrepareAsync(database, baseline, publisher);
        var checkpoint = new LosePublisherOnEpochSave(creation);
        await using var context = database.Context(checkpoint);
        await using var retained = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = Session(database, context, retained?.Authority(context, TimeProvider.System));

        await Assert.ThrowsAsync<AuthorizationException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default));

        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation);
        await Assert.That(await QualifyPublisherAsync(inspect, creation, default)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Installed_host_without_its_epoch_cannot_commit_any_initial_event_records()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "user");
        context.ModerationQualificationEpochs.RemoveRange(await context.ModerationQualificationEpochs.ToArrayAsync());
        await context.SaveChangesAsync();
        var session = Session(database, context);

        await Assert.ThrowsAsync<AuthorizationException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, _) => Task.FromResult(true), (creator, token) => InsertAsync(context, creation, creator, token), default));

        await using var inspect = database.Context();
        await Assert.That(await inspect.Events.AnyAsync(row => row.Id == creation.EventId)).IsFalse();
        await Assert.That(await inspect.Set<EventParticipationConfiguration>().AnyAsync(row => row.Id == creation.EventId)).IsFalse();
        await Assert.That(await inspect.EventRoleAssignments.AnyAsync(row => row.EventId == creation.EventId)).IsFalse();
        await Assert.That(await inspect.ModerationHostBindings.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationQualificationEpochs.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Dirty_caller_state_is_rejected_without_saving_or_discarding_the_callers_changes()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "user");
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var original = tenant.FullName;
        tenant.FullName = "Unrelated caller edit must survive";
        var session = Session(database, context);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, _) => Task.FromResult(true), (creator, token) => InsertAsync(context, creation, creator, token), default));

        await Assert.That(context.Entry(tenant).State).IsEqualTo(EntityState.Modified);
        await Assert.That(tenant.FullName).IsEqualTo("Unrelated caller edit must survive");
        await using var inspect = database.Context();
        await Assert.That(await inspect.Tenants.Where(row => row.Id == tenant.Id).Select(row => row.FullName).SingleAsync()).IsEqualTo(original);
        await AssertRolledBackAsync(inspect, creation);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Failure_after_the_callback_flush_rolls_back_the_entire_new_parent(bool absentHost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "group", absentHost: absentHost);
        var session = Session(database, context);

        await Assert.ThrowsAsync<InitialCreationFailure>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), async (creator, token) =>
            {
                await InsertAsync(context, creation, creator, token);
                throw new InitialCreationFailure();
            }, default));

        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation, absentHost);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [Arguments(false, true, false)]
    public async Task Unknown_commit_reconciles_the_whole_parent_or_preserves_the_rolled_back_transport_failure(bool verify, bool rollback, bool retry)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var creation = await PrepareAsync(database, baseline, "organization");
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, rollback);
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = database };
        var session = Session(database, context);

        Task<BaseCommandResponse<Guid>> ExecuteAsync() => session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (creator, token) => InsertAsync(context, creation, creator, token), default);

        if (rollback && !retry)
            await Assert.ThrowsAsync<TenantUserRoleGrantMutationBoundaryTests.LostRoleCommitException>(async () => await ExecuteAsync());
        else
        {
            var result = await ExecuteAsync();
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Id).IsEqualTo(creation.EventId);
        }

        await Assert.That(fault.Injected).IsTrue();
        await using var inspect = database.Context();
        if (rollback && !retry) await AssertRolledBackAsync(inspect, creation);
        else await AssertCommittedAsync(inspect, creation);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("owner")]
    [Arguments("participation")]
    [Arguments("creator")]
    [Arguments("result")]
    [Arguments("failure")]
    public async Task Callback_cannot_commit_an_incomplete_parent_or_disguise_partial_writes_as_a_failed_result(string corruption)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "organization");
        var session = Session(database, context);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), async (creator, token) =>
            {
                await InsertAsync(context, creation, creator, token);
                switch (corruption)
                {
                    case "owner":
                        await context.EventRoleAssignments.Where(row => row.Id == creation.OwnerAssignmentId).ExecuteDeleteAsync(token);
                        break;
                    case "participation":
                        await context.Set<EventParticipationConfiguration>().Where(row => row.Id == creation.EventId).ExecuteDeleteAsync(token);
                        break;
                    case "creator":
                        await context.Events.Where(row => row.Id == creation.EventId)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedBy, database.AdministratorId), token);
                        break;
                    case "result":
                        return BaseCommandResponse.Success(Guid.CreateVersion7());
                    case "failure":
                        return BaseCommandResponse.NotFound<Guid>();
                }
                return BaseCommandResponse.Success(creation.EventId);
            }, default));

        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Unwritten_callback_failure_does_not_create_an_owner_or_advance_the_epoch()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "organization");
        var session = Session(database, context);
        var result = await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token),
            (_, _) => Task.FromResult(BaseCommandResponse.NotFound<Guid>()), default);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation);
    }

    [Test]
    public async Task Invocation_cannot_nominate_another_existing_human_as_the_original_creator()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = (await PrepareAsync(database, context, "organization")) with { CreatorUserId = database.AdministratorId };
        var session = Session(database, context);
        await Assert.ThrowsAsync<AuthorizationException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, _) => Task.FromResult(true), (creator, token) => InsertAsync(context, creation, creator, token), default));
        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation);
    }

    [Test]
    public async Task Failed_callback_cannot_commit_a_bulk_only_disclosure_change()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "organization");
        var discovery = await context.Set<EventDiscoveryRevision>().AsNoTracking().SingleOrDefaultAsync();
        if (discovery is null)
        {
            discovery = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = database.TenantId };
            context.Add(discovery);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        var before = await context.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
        var session = Session(database, context);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), async (_, token) =>
            {
                await context.Set<EventDiscoveryRevision>().Where(row => row.Id == before.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DisclosureEpoch, row => row.DisclosureEpoch + 1), token);
                return BaseCommandResponse.NotFound<Guid>();
            }, default));
        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation);
        var after = await inspect.Set<EventDiscoveryRevision>().AsNoTracking().SingleAsync();
        await Assert.That(after.DisclosureEpoch).IsEqualTo(before.DisclosureEpoch);
        await Assert.That(after.IdentityEpoch).IsEqualTo(before.IdentityEpoch);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Featured_image_previous_actor_is_acquired_before_tenant_and_relationship_drift_aborts(bool moveAfterPlan)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var setup = database.Context();
        var creation = await PrepareAsync(database, setup, "organization");
        var previousActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = database.AdministratorId,
            Pii = new ActorPii { DisplayName = "Original image owner" }
        };
        var image = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            ActorId = previousActor.Id,
            Actor = previousActor,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Provider = "legacy_external",
            FullName = "Native event image",
            SafeDisplayName = "image.png",
            Extension = ".png",
            ContentType = "image/png",
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active
        };
        setup.Add(image);
        await setup.SaveChangesAsync();
        creation = creation with { ImageIds = [image.Id] };
        var order = new ImageActorBeforeTenant(image.Id, creation.PublisherActorId, moveAfterPlan);
        await using var context = database.Context(order);
        var session = Session(database, context);
        async Task<BaseCommandResponse<Guid>> CreateAsync(Guid creator, CancellationToken token)
        {
            var result = await InsertAsync(context, creation, creator, token);
            var saved = await context.StorageObjects.SingleAsync(row => row.Id == image.Id, token);
            saved.ActorId = creation.PublisherActorId;
            await context.SaveChangesAsync(token);
            return result;
        }
        Task<BaseCommandResponse<Guid>> ExecuteAsync() => session.Boundary.ExecuteCreationAsync(creation,
            (_, token) => QualifyPublisherAsync(context, creation, token), CreateAsync, default);
        if (moveAfterPlan) await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await ExecuteAsync());
        else await Assert.That((await ExecuteAsync()).IsSuccess).IsTrue();
        await Assert.That(order.ActorsBeforeTenant.Contains(previousActor.Id)).IsTrue();
        await Assert.That(order.ActorsBeforeTenant.Contains(creation.PublisherActorId)).IsTrue();
        await using var inspect = database.Context();
        if (moveAfterPlan) await AssertRolledBackAsync(inspect, creation);
        else await AssertCommittedAsync(inspect, creation);
        await Assert.That(await inspect.StorageObjects.Where(row => row.Id == image.Id).Select(row => row.ActorId).SingleAsync())
            .IsEqualTo(moveAfterPlan ? previousActor.Id : creation.PublisherActorId);
    }

    private sealed class ImageActorBeforeTenant(Guid imageId, Guid publisherActor, bool moveAfterPlan) : DbCommandInterceptor
    {
        private readonly HashSet<Guid> _actors = [];
        private bool _tenant;
        public Guid[] ActorsBeforeTenant { get; private set; } = [];
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context) return result;
            bool Updates<T>()
            {
                var entity = context.Model.FindEntityType(typeof(T))!;
                var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                return command.CommandText.StartsWith($"UPDATE {table} ", StringComparison.Ordinal);
            }
            if (!_tenant && Updates<Actor>())
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is Guid id) _actors.Add(id);
            if (!_tenant && Updates<Tenant>())
            {
                _tenant = true;
                ActorsBeforeTenant = _actors.Order().ToArray();
                if (moveAfterPlan)
                    await context.StorageObjects.Where(row => row.Id == imageId).ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.ActorId, publisherActor), cancellationToken);
            }
            return result;
        }
    }

    [Test]
    public async Task Draft_creation_cannot_smuggle_a_published_parent_past_the_publication_overlay()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var creation = await PrepareAsync(database, context, "organization");
        var session = Session(database, context);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => await session.Boundary.ExecuteCreationAsync(
            creation, (_, token) => QualifyPublisherAsync(context, creation, token), async (creator, token) =>
            {
                var result = await InsertAsync(context, creation, creator, token);
                await context.Events.Where(row => row.Id == creation.EventId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.EventStatusId, (int)EventStatusEnum.Published), token);
                return result;
            }, default));
        await using var inspect = database.Context();
        await AssertRolledBackAsync(inspect, creation);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task<EventInitialCreation> PrepareAsync(SourceDatabase database, ExploreDbContext context,
        string publisher, bool community = false, bool absentHost = false)
    {
        Guid? organizationId = null;
        Guid? groupId = null;
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = database.SubjectId,
            Pii = new ActorPii { DisplayName = "Initial event publisher" }
        };
        if (publisher == "organization")
        {
            organizationId = Guid.CreateVersion7();
            var organization = new Organization
            {
                Id = organizationId.Value,
                Pii = new OrganizationPii { FullName = "Native event organization" }
            };
            var participation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationId = organization.Id,
                Organization = organization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!,
                IsOrganizerEligible = true
            };
            context.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                OrganizationTenantId = participation.Id,
                OrganizationTenant = participation,
                UserId = database.SubjectId,
                User = null!,
                RoleId = (int)RoleEnum.OrganizationAdmin,
                Role = null!
            });
            actor.ActorTypeId = (int)ActorTypeEnum.Organization;
            actor.UserId = null;
            actor.OrganizationId = organization.Id;
        }
        else if (publisher == "group")
        {
            if (!await context.Set<GroupPosition>().AnyAsync(row => row.Id == 4))
                context.Add(new GroupPosition { Id = 4, MasterCode = "member", FullName = "Member" });
            groupId = Guid.CreateVersion7();
            var group = new Group { Id = groupId.Value, FullName = "Native event group" };
            var participation = new GroupTenant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                GroupId = group.Id,
                Group = group,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                ApprovalStatus = null!,
                IsOrganizerEligible = true
            };
            context.GroupMembers.Add(new GroupMember
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                GroupTenantId = participation.Id,
                GroupTenant = participation,
                UserId = database.SubjectId,
                User = null!,
                RoleId = (int)RoleEnum.GroupAdmin,
                Role = null!
            });
            actor.ActorTypeId = (int)ActorTypeEnum.Group;
            actor.UserId = null;
            actor.GroupId = group.Id;
        }
        context.Actors.Add(actor);
        if (absentHost)
        {
            context.ModerationQualificationEpochs.RemoveRange(await context.ModerationQualificationEpochs.ToArrayAsync());
            context.ModerationHostBindings.RemoveRange(await context.ModerationHostBindings.ToArrayAsync());
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return new(database.TenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), database.SubjectId,
            actor.Id, organizationId, groupId, community, DateTime.UtcNow, [], EventStatusEnum.Draft);
    }

    private static async Task<bool> QualifyPublisherAsync(ExploreDbContext context, EventInitialCreation creation, CancellationToken token)
    {
        if (!await context.Actors.AsNoTracking().AnyAsync(row => row.Id == creation.PublisherActorId && !row.IsDeleted, token)) return false;
        if (creation.OrganizationId is { } organizationId)
            return await context.OrganizationTenants.AsNoTracking().AnyAsync(row => row.TenantId == creation.TenantId
                && row.OrganizationId == organizationId && row.IsOrganizerEligible && !row.IsSuspended && !row.IsDeleted, token);
        if (creation.GroupId is { } groupId)
            return await context.GroupTenants.AsNoTracking().AnyAsync(row => row.TenantId == creation.TenantId
                && row.GroupId == groupId && row.IsOrganizerEligible && !row.IsDeleted, token);
        return true;
    }

    private static async Task<BaseCommandResponse<Guid>> InsertAsync(ExploreDbContext context, EventInitialCreation creation,
        Guid creator, CancellationToken token)
    {
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = creation.EventId,
            TenantId = creation.TenantId,
            Tenant = null!,
            ActorId = creation.PublisherActorId,
            Actor = null!,
            OrganizerActorId = creation.CommunitySubmission ? null : creation.PublisherActorId,
            SubmittedByUserId = creation.CommunitySubmission ? creator : null,
            Title = "Native initial event",
            PublicCode = creation.EventId.ToString("N")[..12],
            EventProvenanceTypeId = (int)(creation.CommunitySubmission ? EventProvenanceTypeEnum.CommunityReported : EventProvenanceTypeEnum.OrganizerCreated),
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC",
            CreatedAt = creation.CreatedAtUtc,
            CreatedBy = creator,
            ParticipationConfiguration = EventParticipationConfiguration.Create(creation.EventId, creation.TenantId,
                (int)ParticipationHandlingModeEnum.InformationOnly, (int)AdvanceRegistrationObligationEnum.NotApplicable,
                null, null, creation.CreatedAtUtc)
        };
        if (creation.RequestedStatus == EventStatusEnum.Published)
            parent.Publish(creation.CreatedAtUtc);
        context.Events.Add(parent);
        if (!creation.CommunitySubmission)
        {
            var owner = EventRoleAssignment.Create(creation.TenantId, creation.EventId, creator, (int)RoleEnum.EventOwner,
                EventRoleAssignmentStatus.Active, creation.CreatedAtUtc, null, creator);
            owner.Id = creation.OwnerAssignmentId;
            owner.CreatedAt = creation.CreatedAtUtc;
            context.EventRoleAssignments.Add(owner);
        }
        await context.SaveChangesAsync(token);
        return BaseCommandResponse.Success(creation.EventId);
    }

    private static async Task AssertCommittedAsync(ExploreDbContext context, EventInitialCreation creation, bool absentHost = false)
    {
        await Assert.That(await context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<EventParticipationConfiguration>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.EventRoleAssignments.CountAsync()).IsEqualTo(creation.CommunitySubmission ? 0 : 1);
        var parent = await context.Events.AsNoTracking().SingleAsync(row => row.Id == creation.EventId);
        await Assert.That(parent.ActorId).IsEqualTo(creation.PublisherActorId);
        await Assert.That(parent.EventStatusId).IsEqualTo((int)creation.RequestedStatus);
        await Assert.That(parent.CreatedBy).IsEqualTo((Guid?)creation.CreatorUserId);
        await Assert.That(parent.CreatedAt).IsEqualTo(creation.CreatedAtUtc);
        await Assert.That(parent.EventProvenanceTypeId).IsEqualTo((int)(creation.CommunitySubmission
            ? EventProvenanceTypeEnum.CommunityReported : EventProvenanceTypeEnum.OrganizerCreated));
        await Assert.That(parent.OrganizerActorId).IsEqualTo(creation.CommunitySubmission ? null : (Guid?)creation.PublisherActorId);
        await Assert.That(parent.SubmittedByUserId).IsEqualTo(creation.CommunitySubmission ? (Guid?)creation.CreatorUserId : null);
        var participation = await context.Set<EventParticipationConfiguration>().AsNoTracking().SingleAsync(row => row.Id == creation.EventId);
        await Assert.That(participation.TenantId).IsEqualTo(creation.TenantId);
        await Assert.That(participation.ParticipationHandlingModeId).IsEqualTo((int)ParticipationHandlingModeEnum.InformationOnly);
        var owners = await context.EventRoleAssignments.AsNoTracking().Where(row => row.EventId == creation.EventId).ToArrayAsync();
        await Assert.That(owners.Length).IsEqualTo(creation.CommunitySubmission ? 0 : 1);
        if (!creation.CommunitySubmission)
        {
            var owner = owners.Single();
            await Assert.That(owner.Id).IsEqualTo(creation.OwnerAssignmentId);
            await Assert.That(owner.TenantId).IsEqualTo(creation.TenantId);
            await Assert.That(owner.UserId).IsEqualTo(creation.CreatorUserId);
            await Assert.That(owner.CreatedBy).IsEqualTo((Guid?)creation.CreatorUserId);
            await Assert.That(owner.RoleId).IsEqualTo((int)RoleEnum.EventOwner);
            await Assert.That(owner.Status).IsEqualTo(EventRoleAssignmentStatus.Active);
            await Assert.That(owner.StartsAtUtc).IsEqualTo(creation.CreatedAtUtc);
            await Assert.That(owner.ExpiresAtUtc).IsNull();
            await Assert.That(owner.Version).IsEqualTo(1L);
        }
        await Assert.That(await context.ModerationHostBindings.CountAsync()).IsEqualTo(absentHost ? 0 : 1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).ToArrayAsync())
            .IsEquivalentTo(absentHost ? Array.Empty<long>() : [2L]);
    }

    private static async Task AssertRolledBackAsync(ExploreDbContext context, EventInitialCreation creation, bool absentHost = false)
    {
        await Assert.That(await context.Events.AnyAsync(row => row.Id == creation.EventId)).IsFalse();
        await Assert.That(await context.Set<EventParticipationConfiguration>().AnyAsync(row => row.Id == creation.EventId)).IsFalse();
        await Assert.That(await context.EventRoleAssignments.AnyAsync(row => row.EventId == creation.EventId)).IsFalse();
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).ToArrayAsync())
            .IsEquivalentTo(absentHost ? Array.Empty<long>() : [1L]);
    }

    private static (EventRoleAssignmentMutationBoundary Boundary, HttpContextAccessor Http) Session(SourceDatabase database,
        ExploreDbContext context, IPrivacyIdentityFenceAuthority? privacy = null, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, database.SubjectId.ToString("D"))], "initial-event-native"))
            }
        };
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
            new OrganizationMemberRepository(context), new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        return (new EventRoleAssignmentMutationBoundary(context, privacy ?? database.Privacy.Authority(context, clock),
            new UserContext(http), admin, new MachinePrincipalAccessor(http),
            new EventOwnershipTransferMutationTests.Provider(context), clock, ModerationRecognitionResourceFixture.Reader(context)), http);
    }

    private sealed class InitialCreationFailure : Exception;

    private sealed class LosePublisherOnEpochSave(EventInitialCreation creation) : SaveChangesInterceptor
    {
        private bool _pending;
        public bool Observed { get; private set; }
        public bool PrivacyOwned { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationQualificationEpoch>().Any(entry => entry.State == EntityState.Modified))
            {
                _pending = true;
                PrivacyOwned = context.IdentityFenceOwnsTransaction;
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pending && eventData.Context is ExploreDbContext context)
            {
                _pending = false;
                Observed = true;
                if (creation.OrganizationId is { } organizationId)
                    await context.OrganizationTenants.Where(row => row.TenantId == creation.TenantId && row.OrganizationId == organizationId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsOrganizerEligible, false), cancellationToken);
                else if (creation.GroupId is { } groupId)
                    await context.GroupTenants.Where(row => row.TenantId == creation.TenantId && row.GroupId == groupId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsOrganizerEligible, false), cancellationToken);
            }
            return result;
        }
    }
}
