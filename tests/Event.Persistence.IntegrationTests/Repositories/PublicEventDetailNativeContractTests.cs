using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class PublicEventDetailNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeReadsSessionlessPublicDetailWithoutPrivateSourcePrivileges()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var detail = await ReadAsync(context, seeded.Event);
            await Assert.That(detail).IsNotNull();
            var dto = EventMapper.ToDetail(detail)!;
            await Assert.That(dto.Title).IsEqualTo("Public detail");
            await Assert.That(dto.Content).IsEqualTo("Published full description");
            await Assert.That(dto.ActorDisplayName).IsEqualTo("Public organizer");
            await Assert.That(dto.EventStatusMasterCode).IsEqualTo("PUBLISHED");
            await Assert.That(detail!.Sessions.Count).IsEqualTo(0);
            await Assert.That(detail.Actor.User).IsNull();
            await Assert.That(detail.Actor.Organization).IsNull();
            await Assert.That(detail.ProvenanceExternalId).IsNull();
            await Assert.That(detail.FeaturedImage).IsNull();
            await Assert.That(detail.CapacityPools.Count).IsEqualTo(0);
            await Assert.That(context.ChangeTracker.Entries().Count()).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            var byCode = await PostgresPublicEventDetailContract.ReadAsync(context, Binding(context),
                null, seeded.Code, default);
            await Assert.That(byCode!.Id).IsEqualTo(seeded.Event);
            await transaction.RollbackAsync();
        }
        string table = new HumanPrincipalSourceSql(context).Table(typeof(Explore.Domain.Event));
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 1"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        var ownerDenied = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync($"SET ROLE {PostgresPublicEventDetailContract.OwnerRole}"));
        await Assert.That(ownerDenied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task PublicEnrichmentProjectsLabelsAndLatestModerationWithoutPrivateHistory()
    {
        var seeded = await SeedAsync();
        Guid tagId = Guid.CreateVersion7();
        Guid categoryId = Guid.CreateVersion7();
        await using (var writer = fixture.CreateDbContext())
        {
            var entity = await writer.Events.SingleAsync(value => value.Id == seeded.Event);
            var tag = new Tag
            {
                Id = tagId, TenantId = seeded.Tenant, Tenant = null!,
                MasterCode = "PUBLIC_LABEL", FullName = "Public label"
            };
            var category = new Category
            {
                Id = categoryId, ConcurrencyStamp = Guid.CreateVersion7(),
                TenantId = seeded.Tenant, Tenant = null!,
                MasterCode = "PUBLIC_CATEGORY", FullName = "Public category"
            };
            var moderated = EventModerationRecord.CreateLightModeration(
                Guid.CreateVersion7(), seeded.Tenant, seeded.Event, null,
                "private_reason", (int)EventStatusEnum.Published, "private_correlation",
                new DateTimeOffset(2028, 6, 15, 10, 0, 0, TimeSpan.Zero));
            var restored = EventModerationRecord.CreateUnmoderation(
                Guid.CreateVersion7(), moderated, null, "private_restoration_reason",
                "private_restoration_correlation", moderated.CreatedAt.AddMinutes(1));
            writer.AddRange(tag, category, moderated, restored, new EventTags
            {
                Id = Guid.CreateVersion7(), EventId = entity.Id, Event = entity,
                TagId = tag.Id, Tag = tag, TenantId = seeded.Tenant, Tenant = null!
            }, new EventCategories
            {
                Id = Guid.CreateVersion7(), EventId = entity.Id, Event = entity,
                CategoryId = category.Id, Category = category, TenantId = seeded.Tenant, Tenant = null!
            });
            await writer.SaveChangesAsync();
        }
        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        var detail = (await ReadAsync(reader, seeded.Event))!;
        var presentation = detail.DiscoveryPresentation!;
        await Assert.That(presentation.Tags.Single().Id).IsEqualTo(tagId);
        await Assert.That(presentation.Tags.Single().FullName).IsEqualTo("Public label");
        await Assert.That(presentation.Categories.Single().Id).IsEqualTo(categoryId);
        await Assert.That(presentation.Categories.Single().FullName).IsEqualTo("Public category");
        await Assert.That(presentation.IsUnmoderationEligible).IsFalse();
        await Assert.That(detail.ModerationRecords.Count).IsEqualTo(0);
        var sql = new HumanPrincipalSourceSql(reader);
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            reader.Database.ExecuteSqlRawAsync($"SELECT * FROM {sql.Table(typeof(EventModerationRecord))} LIMIT 1"));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task HiddenForeignAndMissingEventsDoNotBecomePublicDetails()
    {
        var seeded = await SeedAsync();
        await using (var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
        {
            await Assert.That(await ReadAsync(reader, Guid.CreateVersion7())).IsNull();
            await Assert.That(await ReadAsync(reader, seeded.Hidden)).IsNull();
        }
        await using var foreign = Open(PrimaryDatabaseRole.Runtime, Guid.CreateVersion7());
        await Assert.That(await ReadAsync(foreign, seeded.Event)).IsNull();
        await Assert.That(await PostgresPublicEventDetailContract.ReadAsync(foreign, Binding(foreign),
            null, seeded.Code, default)).IsNull();
    }

    [Test]
    public async Task DetailHydratesPublicAspectsActionsAndSafeImageReferencesOnly()
    {
        var seeded = await SeedAsync();
        Guid imageId;
        await using (var writer = fixture.CreateDbContext())
        {
            var entity = await writer.Events.SingleAsync(value => value.Id == seeded.Event);
            entity.ParticipationConfiguration = EventParticipationConfiguration.Create(entity.Id, entity.TenantId,
                (int)ParticipationHandlingModeEnum.InformationOnly,
                (int)AdvanceRegistrationObligationEnum.NotApplicable, null, null,
                new DateTime(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc));
            writer.Add(new EventIslamicAspect { Id = entity.Id, IncludesQuranRecitation = true });
            writer.Add(new EventTechAspect { Id = entity.Id, RequiresLaptop = true, TechStackTags = ".NET" });
            foreach (var kind in new[] { EventPublicActionKindEnum.OriginalSource, EventPublicActionKindEnum.ExternalRegistration })
            {
                var action = new EventPublicAction
                {
                    Id = Guid.CreateVersion7(), TenantId = entity.TenantId, EventId = entity.Id,
                    EventPublicActionKindId = (int)kind, HealthStateId = (int)EventPublicActionHealthStateEnum.Active
                };
                action.SetDestination(ExternalActionUrl.Create("https://example.test/event"));
                writer.Add(action);
            }
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            writer.Add(binding);
            var image = new StorageObject
            {
                Id = Guid.CreateVersion7(), TenantId = seeded.Tenant, Tenant = null!,
                FileTypeId = await writer.FileTypes.Select(value => value.Id).FirstAsync(), FileType = null!,
                Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
                ObjectKey = "public-detail/image.png", FullName = "Image", SafeDisplayName = "Image",
                Extension = "png", ContentType = "image/png", Size = 10,
                Visibility = StorageObjectVisibilities.PublicImage, Purpose = StorageObjectPurposes.EventImage,
                LifecycleState = StorageObjectLifecycleStates.Active
            };
            writer.Add(image);
            imageId = image.Id;
            entity.FeaturedImageId = image.Id;
            await writer.SaveChangesAsync();
        }
        await using (var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
        {
            var detail = (await ReadAsync(reader, seeded.Event))!;
            var dto = EventMapper.ToDetail(detail)!;
            await Assert.That(dto.IslamicAspect!.IncludesQuranRecitation).IsTrue();
            await Assert.That(dto.TechAspect!.RequiresLaptop).IsTrue();
            await Assert.That(dto.ParticipationConfiguration!.ParticipationHandlingModeCode).IsEqualTo("INFORMATION_ONLY");
            await Assert.That(dto.PublicActions.Count).IsEqualTo(1);
            await Assert.That(dto.PublicActions.Single().KindCode).IsEqualTo("ORIGINAL_SOURCE");
            await Assert.That(detail.DiscoveryPresentation!.FeaturedImageUri)
                .IsEqualTo($"/api/storageobject/{imageId}/public");
            await Assert.That(dto.FeaturedImageUri).IsEqualTo($"/api/storageobject/{imageId}/public");
            await Assert.That(detail.FeaturedImage).IsNull();
            await Assert.That(detail.ParticipationConfiguration!.RequirementAttachments.Count).IsEqualTo(0);
        }
        await using (var writer = fixture.CreateDbContext())
        {
            var image = await writer.StorageObjects.SingleAsync(value => value.Id == imageId);
            image.Visibility = StorageObjectVisibilities.PrivateOwner;
            await writer.SaveChangesAsync();
        }
        await using var current = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        var hiddenImage = (await ReadAsync(current, seeded.Event))!;
        await Assert.That(hiddenImage.DiscoveryPresentation!.FeaturedImageUri).IsNull();
        await Assert.That(EventMapper.ToDetail(hiddenImage)!.FeaturedImageUri).IsNull();
    }

    [Test]
    public async Task RevokedSourceParticipationHidesPreviouslyReadableDetail()
    {
        var seeded = await SeedAsync();
        await using (var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(reader, seeded.Event)).IsNotNull();
        await using (var writer = fixture.CreateDbContext())
        {
            var membership = await writer.TenantUsers.SingleAsync(value => value.TenantId == seeded.Tenant);
            membership.IsDeleted = true;
            await writer.SaveChangesAsync();
        }
        await using var current = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.That(await ReadAsync(current, seeded.Event)).IsNull();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    public async Task WrongPhysicalLoginCannotBorrowPublicDetail(PrimaryDatabaseRole role)
    {
        var seeded = await SeedAsync();
        await using var context = Open(role, seeded.Tenant);
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seeded.Event));
    }

    [Test]
    public async Task WrongPurposeAndChangedTenantAreRejected()
    {
        var seeded = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(context, seeded.Tenant, PolicyMutationPurpose.EmailDelivery);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
        {
            _ = new PublicEventDiscoveryReadBinding(context, lease, fixture.NativeQueueEnrollment);
            return Task.CompletedTask;
        });
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.current_tenant_id',{0},true)", Guid.CreateVersion7().ToString());
        var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seeded.Event));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("acl")]
    [Arguments("body")]
    [Arguments("policy")]
    public async Task AdmissionRejectsDriftWithoutRepairingIt(string change)
    {
        await SeedAsync();
        await using var writer = fixture.CreateDbContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(writer);
        string function = PostgresPublicEventDetailContract.FunctionSql(writer);
        await writer.Database.ExecuteSqlRawAsync(change switch
        {
            "acl" => $"GRANT EXECUTE ON FUNCTION {function}(uuid,uuid,text) TO PUBLIC",
            "body" => $"""
                GRANT CREATE ON SCHEMA {sql.Quote(sql.Schema)} TO {PostgresPublicEventDetailContract.OwnerRole};
                SET LOCAL ROLE {PostgresPublicEventDetailContract.OwnerRole};
                CREATE OR REPLACE FUNCTION {function}(p_tenant uuid,p_id uuid,p_code text) RETURNS jsonb
                LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
                AS $changed$ BEGIN RETURN pg_catalog.jsonb_build_object(); END; $changed$;
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {sql.Quote(sql.Schema)} FROM {PostgresPublicEventDetailContract.OwnerRole};
                """,
            "policy" => $"ALTER POLICY public_event_detail_read ON {sql.Table(typeof(Explore.Domain.Event))} USING (true)",
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        });
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresPublicEventDetailContract.RequireReadyAsync(writer, fixture.NativeQueueEnrollment));
        await Assert.That(denied!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    private Task<Explore.Domain.Event?> ReadAsync(ExploreDbContext context, Guid id) =>
        PostgresPublicEventDetailContract.ReadAsync(context, Binding(context), id, null, default);

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

    private async Task<(Guid Tenant, Guid Event, Guid Hidden, string Code)> SeedAsync()
    {
        await using var writer = fixture.CreateDbContext();
        await PostgresPublicEventDetailContract.ApplyAsync(writer, fixture.NativeQueueEnrollment);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "Public detail reader",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), Pii = null! };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(), User = user, UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            Pii = new ActorPii { DisplayName = "Public organizer" }
        };
        writer.AddRange(tenant, user, actor, new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            UserId = user.Id, User = user, ActorId = actor.Id, Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        var visible = Create(VisibilityTypeEnum.Public);
        var hidden = Create(VisibilityTypeEnum.Private);
        await writer.SaveChangesAsync();
        return (tenant.Id, visible.Id, hidden.Id, visible.PublicCode!);

        Explore.Domain.Event Create(VisibilityTypeEnum visibility)
        {
            var entity = new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                ActorId = actor.Id, Actor = actor, Title = "Public detail",
                Content = "Published full description", PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
                VisibilityTypeId = (int)visibility, VisibilityType = null!, EventStatus = null!,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                Timezone = "UTC", CreatedAt = new DateTime(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc)
            };
            writer.Add(entity);
            return entity;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
