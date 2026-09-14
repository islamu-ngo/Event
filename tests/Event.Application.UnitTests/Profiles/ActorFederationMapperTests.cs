using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Actors.Handlers.Commands;
using Explore.Application.Features.Actors.Handlers.Queries;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Features.Actors.Requests.Queries;
using Explore.Application.Features.StorageObjects.Handlers.Queries;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Microsoft.Extensions.Logging.Abstractions;
using Explore.Application.DTOs.Actor;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

public sealed class ActorFederationMapperTests
{
    private static readonly Guid ActorId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid OwnerId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid Stamp = Guid.Parse("01900000-0000-7000-8000-000000000004");
    private static readonly DateTime ResolvedAt = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ActorConsumers_PreserveMissingResultsAndRepositorySelectedOrder()
    {
        var first = CreateActor();
        var second = CreateActor();
        second.Id = Stamp;
        var actors = new ActorStore([first, second]);
        var detail = new GetActorDetailsRequestHandler(actors, new TenantContext(TenantId), NullLogger<GetActorDetailsRequestHandler>.Instance);
        var byDid = new GetActorByDidRequestHandler(actors, null!, NullLogger<GetActorByDidRequestHandler>.Instance);
        var list = new GetActorListRequestHandler(actors, null!, NullLogger<GetActorListRequestHandler>.Instance);
        await AssertContract((await detail.QueryAsync(new GetActorDetailsRequest { Id = ActorId }, default))!, ExpectedActor(false));
        await Assert.That(await detail.QueryAsync(new GetActorDetailsRequest { Id = OwnerId }, default)).IsNull();
        await AssertContract(await byDid.QueryAsync(new GetActorByDidRequest { Did = "did:plc:first" }, default), ExpectedActor(false));
        await Assert.That(await byDid.QueryAsync(new GetActorByDidRequest { Did = "did:plc:missing" }, default)).IsNull();
        var page = await list.QueryAsync(new GetActorListRequest { PageNumber = -1, PageSize = 500 }, default);
        await Assert.That(page.PageNumber).IsEqualTo(1);
        await Assert.That(page.PageSize).IsEqualTo(100);
        await Assert.That(page.TotalCount).IsEqualTo(2);
        await Assert.That(page.Items.Select(item => item.Id).SequenceEqual(new[] { ActorId, Stamp })).IsTrue();
        actors.Items.Clear();
        first.Pii.DisplayName = "changed";
        await AssertContract(page.Items[0], ExpectedActor(true));
    }

    [Test]
    public async Task TenantActorConsumers_KeepContextualOverridesAndDoNotLeakAnotherTenant()
    {
        var actor = CreateActor();
        actor.Organization = new Organization { Id = OwnerId, Pii = null! };
        actor.Organization.TenantParticipations =
        [
            new OrganizationTenant
            {
                TenantId = TenantId, Tenant = null!, Organization = actor.Organization, ApprovalStatus = null!,
                DisplayNameOverride = "Tenant display", DescriptionOverride = "Tenant description", BackgroundColor = "red",
                ContactEmailOverride = "private-tenant@example.test"
            },
            new OrganizationTenant
            {
                TenantId = Stamp, Tenant = null!, Organization = actor.Organization, ApprovalStatus = null!,
                DisplayNameOverride = "Another tenant", DescriptionOverride = "Another description"
            }
        ];
        var store = new ActorStore([actor]);
        var detail = new GetActorDetailsRequestHandler(store, new TenantContext(Stamp), NullLogger<GetActorDetailsRequestHandler>.Instance);
        var list = new GetActorsByTenantRequestHandler(store, NullLogger<GetActorsByTenantRequestHandler>.Instance);
        var expected = ExpectedActor(false);
        expected["displayName"] = "Tenant display";
        expected["description"] = "Tenant description";
        expected["backgroundColor"] = "red";
        var dto = await detail.QueryAsync(new GetActorDetailsRequest { Id = ActorId, TenantId = TenantId }, default);
        await AssertContract(dto!, expected);
        await Assert.That(dto!.TenantId).IsEqualTo(TenantId);
        await Assert.That(dto.IsLocallyDiscoverable).IsTrue();
        var items = await list.QueryAsync(new GetActorsByTenantRequest { TenantId = TenantId }, default);
        var expectedList = ExpectedActor(true);
        expectedList["displayName"] = "Tenant display";
        expectedList["backgroundColor"] = "red";
        await AssertContract(items.Single(), expectedList);
        await Assert.That(await detail.QueryAsync(new GetActorDetailsRequest { Id = ActorId, TenantId = OwnerId }, default)).IsNull();
        await Assert.That(await list.QueryAsync(new GetActorsByTenantRequest { TenantId = OwnerId }, default)).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StorageConsumers_KeepHandlerOwnedContentDisclosure(bool registrationOwned)
    {
        var source = CreateStorage();
        if (registrationOwned) source.OwningResourceKind = "registration_submission_sink";
        else source.RegistrationContentRetentionUntilUtc = null;
        var store = new StorageStore([source]);
        var clock = new FixedClock();
        var detail = new GetStorageObjectDetailsRequestHandler(store, clock);
        var list = new GetStorageObjectListRequestHandler(store, clock);
        var expected = ExpectedStorage(false);
        var expectedList = ExpectedStorage(true);
        if (registrationOwned)
        {
            expected["owningResourceKind"] = "registration_submission_sink";
            foreach (var field in new[] { "uri", "fullName", "safeDisplayName" })
            {
                expected[field] = string.Empty;
                expectedList[field] = string.Empty;
            }
        }
        var dto = await detail.QueryAsync(new GetStorageObjectDetailsRequest { Id = Stamp }, default);
        await AssertContract(dto!, expected);
        await Assert.That(dto!.ContentEligibility.ContentAllowed).IsEqualTo(!registrationOwned);
        await Assert.That(await detail.QueryAsync(new GetStorageObjectDetailsRequest { Id = OwnerId }, default)).IsNull();
        var page = await list.QueryAsync(new GetStorageObjectListRequest { PageNumber = -1, PageSize = 500 }, default);
        await Assert.That(page.PageNumber).IsEqualTo(1);
        await Assert.That(page.PageSize).IsEqualTo(100);
        await Assert.That(page.TotalCount).IsEqualTo(1);
        await AssertContract(page.Items.Single(), expectedList);
        store.Items.Clear();
        source.FullName = "changed";
        await AssertContract(page.Items.Single(), expectedList);
    }

    [Test]
    [Arguments("missing-owner")]
    [Arguments("existing-actor")]
    [Arguments("cross-tenant-image")]
    public async Task CreateConsumer_RejectsInvalidOwnershipBeforeCreatingActor(string invalid)
    {
        var actors = new ActorStore(invalid == "existing-actor" ? [CreateActor()] : []);
        var image = CreateStorage();
        image.TenantId = Stamp;
        image.IsDeleted = false;
        image.Visibility = StorageObjectVisibilities.PublicImage;
        image.LifecycleState = StorageObjectLifecycleStates.Active;
        var handler = new CreateActorCommandHandler(actors, new ActorTypeStore(), new CustodyTypeStore(), new StorageStore([image]), null!, new UserStore(), null!, new TenantContext(TenantId));
        var result = await handler.ExecuteAsync(new CreateActorCommand
        {
            ActorDto = new CreateActorDto
            {
                ActorTypeId = 7, UserId = invalid == "missing-owner" ? null : OwnerId, DisplayName = "Rejected profile",
                ProfilePictureId = invalid == "cross-tenant-image" ? Stamp : null
            }
        }, default);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(actors.Items.Any(actor => actor.Pii.DisplayName == "Rejected profile")).IsFalse();
        await Assert.That(actors.Items.Count).IsEqualTo(invalid == "existing-actor" ? 1 : 0);
    }

    [Test]
    public async Task CreateConsumer_ConstructsOnlyValidatedProfileFieldsWithoutFederationAuthority()
    {
        var actors = new ActorStore([]);
        var handler = new CreateActorCommandHandler(actors, new ActorTypeStore(), new CustodyTypeStore(), new StorageStore([]), null!, new UserStore(), null!, new TenantContext(TenantId));
        var result = await handler.ExecuteAsync(new CreateActorCommand
        {
            ActorDto = new CreateActorDto
            {
                ActorTypeId = 7, UserId = OwnerId, TenantId = Stamp, DisplayName = "New display",
                Description = "New description", ProfilePictureUri = "https://images.example.test/new.png", ProfilePictureCid = "new-cid",
                BackgroundColor = "blue", BackgroundEffect = "none", BannerColor = "green",
                Did = "did:plc:unverified", Handle = "unverified.example.test", PdsHost = "https://unverified.example.test", IndexedAt = ResolvedAt, DidCustodyTypeId = 10
            }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var created = actors.Items.Single();
        await Assert.That(created.Id).IsEqualTo(ActorId);
        await Assert.That(created.Pii.DisplayName).IsEqualTo("New display");
        await Assert.That(created.Pii.ProfilePictureUri).IsEqualTo("https://images.example.test/new.png");
        await Assert.That(created.ActorTypeId).IsEqualTo(7);
        await Assert.That(created.UserId).IsEqualTo(OwnerId);
        await Assert.That(created.Description).IsEqualTo("New description");
        await Assert.That(created.ProfilePictureCid).IsEqualTo("new-cid");
        await Assert.That(created.BackgroundColor).IsEqualTo("blue");
        await Assert.That(created.BackgroundEffect).IsEqualTo("none");
        await Assert.That(created.BannerColor).IsEqualTo("green");
        await Assert.That(created.AtprotoIdentities).IsEmpty();
        await Assert.That(created.IsDeleted).IsFalse();
        await Assert.That(created.IsSuspended).IsFalse();
        await Assert.That(created.CreatedBy).IsNull();
        await Assert.That(created.ConcurrencyStamp).IsEqualTo(Guid.Empty);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActorProjection_SerializesOnlyReviewedScalarsFromCyclicEntity(bool list)
    {
        await AssertContract(MapActor(CreateActor(), list), ExpectedActor(list));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StorageProjection_SerializesOnlyReviewedScalarsFromCyclicEntity(bool list)
    {
        await AssertContract(MapStorage(CreateStorage(), list), ExpectedStorage(list));
    }

    [Test]
    [Arguments(false, "type")]
    [Arguments(true, "type")]
    [Arguments(false, "pii")]
    [Arguments(true, "pii")]
    [Arguments(false, "identities")]
    [Arguments(true, "identities")]
    public async Task MissingActorNavigation_PreservesNullableMetadataAndEmptyErasedDisplay(bool list, string missing)
    {
        var actor = CreateActor();
        var expected = ExpectedActor(list);
        string[] fields;
        switch (missing)
        {
            case "type":
                actor.ActorType = null!;
                fields = ["actorTypeFullName", "actorTypeMasterCode"];
                break;
            case "pii":
                actor.Pii = null!;
                expected["displayName"] = string.Empty;
                fields = ["profilePictureUri"];
                break;
            default:
                actor.AtprotoIdentities.Clear();
                fields = ["did", "handle", "pdsHost", "indexedAt"];
                break;
        }
        foreach (var field in fields) expected[field] = null;
        await AssertContract(MapActor(actor, list), expected);
    }

    [Test]
    [Arguments("fileType")]
    [Arguments("tenant")]
    [Arguments("actor")]
    [Arguments("pii")]
    public async Task MissingStorageNavigation_PreservesNulls(string missing)
    {
        var storage = CreateStorage();
        var expected = ExpectedStorage(false);
        switch (missing)
        {
            case "fileType":
                storage.FileType = null!;
                expected["fileTypeFullName"] = null;
                expected["fileTypeMasterCode"] = null;
                break;
            case "tenant":
                storage.Tenant = null!;
                expected["tenantFullName"] = null;
                break;
            case "actor":
                storage.Actor = null;
                expected["actorDisplayName"] = null;
                break;
            default:
                storage.Actor!.Pii = null!;
                expected["actorDisplayName"] = null;
                break;
        }
        await AssertContract(MapStorage(storage, false), expected);
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    public async Task FirstIdentity_DoesNotSelectALaterNonNullHandleOrFilterByState(bool list, string? handle)
    {
        var actor = CreateActor();
        var first = actor.AtprotoIdentities.First();
        first.Handle = handle;
        first.PdsHost = string.Empty;
        first.IsActive = false;
        first.IsSuspended = true;
        first.IsDeleted = true;
        var expected = ExpectedActor(list);
        expected["handle"] = JsonValue.Create(handle);
        expected["pdsHost"] = string.Empty;
        await AssertContract(MapActor(actor, list), expected);
    }

    [Test]
    [Arguments(false, null)]
    [Arguments(false, "")]
    [Arguments(true, null)]
    [Arguments(true, "")]
    public async Task ActorLabels_DistinguishNullFromEmpty(bool list, string? value)
    {
        var actor = CreateActor();
        actor.Pii.DisplayName = value!;
        actor.Pii.ProfilePictureUri = value;
        actor.ActorType.FullName = value!;
        actor.ActorType.MasterCode = value!;
        var expected = ExpectedActor(list);
        foreach (var field in new[] { "displayName", "profilePictureUri", "actorTypeFullName", "actorTypeMasterCode" })
            expected[field] = JsonValue.Create(value);
        await AssertContract(MapActor(actor, list), expected);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ErasedActor_ProjectsTombstoneWithoutRecreatingPiiOrIdentity(bool list)
    {
        var actor = CreateActor();
        actor.OrganizationId = null;
        actor.GroupId = null;
        actor.TombstoneForUserPrivacyErasure(ResolvedAt, OwnerId);
        actor.Pii = null!;
        var identity = actor.AtprotoIdentities.First();
        identity.EraseForPrivacy(ResolvedAt);
        var expected = ExpectedActor(list);
        expected["concurrencyStamp"] = actor.ConcurrencyStamp.ToString();
        expected["displayName"] = string.Empty;
        expected["profilePictureUri"] = null;
        expected["did"] = "did:deleted:01900000000070008000000000000004";
        expected["handle"] = null;
        expected["pdsHost"] = string.Empty;
        if (!list)
        {
            expected["organizationId"] = null;
            expected["groupId"] = null;
        }
        await AssertContract(MapActor(actor, list), expected);
        await Assert.That(actor.Pii).IsNull();
        await Assert.That(actor.IsDeleted).IsTrue();
        await Assert.That(actor.UserId).IsNull();
        await Assert.That(identity.IsDeleted).IsTrue();
        await Assert.That(identity.SigningKey).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProjectedValues_AreIndependentOfLaterEntityAndIdentityMutations(bool list)
    {
        var actor = CreateActor();
        var storage = CreateStorage();
        var actorDto = MapActor(actor, list);
        var storageDto = MapStorage(storage, list);
        actor.Pii.DisplayName = "changed";
        actor.ActorType.FullName = "changed";
        actor.AtprotoIdentities.First().Handle = "changed.example.test";
        actor.AtprotoIdentities.Clear();
        storage.FileType.FullName = "changed";
        storage.Tenant.FullName = "changed";
        storage.Actor!.Pii.DisplayName = "changed";
        storage.FullName = "changed";
        await AssertContract(actorDto, ExpectedActor(list));
        await AssertContract(storageDto, ExpectedStorage(list));
    }

    private static object MapActor(Actor source, bool list) => list
        ? ActorFederationMapper.ToActorListItem(source)
        : ActorFederationMapper.ToActorDetail(source);

    private static object MapStorage(StorageObject source, bool list) => list
        ? ActorFederationMapper.ToStorageListItem(source)
        : ActorFederationMapper.ToStorageDetail(source);

    private static async Task AssertContract(object actual, JsonObject expected)
    {
        var serialized = JsonSerializer.SerializeToNode(actual, actual.GetType(), JsonOptions);
        await Assert.That(JsonNode.DeepEquals(serialized, expected)).IsTrue()
            .Because($"Expected {expected}; actual {serialized}");
    }

    private static JsonObject ExpectedActor(bool list)
    {
        var expected = JsonNode.Parse("""
            {
              "id":"01900000-0000-7000-8000-000000000001",
              "concurrencyStamp":"01900000-0000-7000-8000-000000000004",
              "actorTypeId":7,"actorTypeMasterCode":"COMMUNITY","actorTypeFullName":"Community",
              "displayName":"Public display","profilePictureUri":"https://images.example.test/avatar.png",
              "did":"did:plc:first","handle":"first.example.test","pdsHost":"https://pds.example.test",
              "indexedAt":"2026-09-03T12:00:00Z",
              "didCustodyTypeId":null,"didCustodyTypeMasterCode":null,"didCustodyTypeFullName":null,
              "backgroundColor":"blue","backgroundEffect":"none","bannerColor":"green",
              "bannerPictureUri":null,"backgroundImageUri":null
            }
            """)!.AsObject();
        if (!list)
        {
            expected["organizationId"] = "01900000-0000-7000-8000-000000000003";
            expected["groupId"] = "01900000-0000-7000-8000-000000000002";
            expected["profilePictureCid"] = "public-cid";
            expected["description"] = "Public description";
        }
        return expected;
    }

    private static JsonObject ExpectedStorage(bool list)
    {
        var expected = JsonNode.Parse("""
            {
              "id":"01900000-0000-7000-8000-000000000004",
              "fileTypeId":8,"fileTypeFullName":"Image","uri":"https://images.example.test/file.png",
              "provider":"local","fullName":"file.png","safeDisplayName":"Safe file","extension":"png",
              "contentType":"image/png","size":9223372036854775806,"visibility":"public-image",
              "purpose":"profile-image","lifecycleState":"quarantined",
              "tenantId":"01900000-0000-7000-8000-000000000002"
            }
            """)!.AsObject();
        if (!list)
        {
            expected["fileTypeMasterCode"] = "IMAGE";
            expected["sha256Checksum"] = "checksum";
            expected["owningResourceKind"] = "Actor";
            expected["owningResourceId"] = "01900000-0000-7000-8000-000000000001";
            expected["tenantFullName"] = "Tenant display";
            expected["actorId"] = "01900000-0000-7000-8000-000000000001";
            expected["actorDisplayName"] = "Public display";
            expected["isDeleted"] = true;
            expected["deletedAt"] = "2026-09-03T12:00:00Z";
            expected["quarantinedAt"] = "2026-09-03T12:00:00Z";
            expected["quarantineReason"] = "review";
        }
        return expected;
    }

    private static Actor CreateActor()
    {
        var actor = new Actor
        {
            Id = ActorId, ActorTypeId = 7,
            ActorType = new ActorType { Id = 7, MasterCode = "COMMUNITY", FullName = "Community", Description = "private lookup metadata" },
            Pii = new ActorPii { DisplayName = "Public display", ProfilePictureUri = "https://images.example.test/avatar.png" },
            UserId = OwnerId, OrganizationId = OwnerId, GroupId = TenantId,
            User = new User { Id = OwnerId, Pii = new UserPii { Email = "private@example.test", FirstName = "Private", LastName = "Owner" } },
            Description = "Public description", ProfilePictureCid = "public-cid", BackgroundColor = "blue", BackgroundEffect = "none", BannerColor = "green",
            CreatedAt = ResolvedAt, CreatedBy = OwnerId, UpdatedAt = ResolvedAt, UpdatedBy = OwnerId,
            IsSuspended = true, SuspendedAt = ResolvedAt, SuspendedBy = OwnerId, ModerationReasonCode = "private-reason", ConcurrencyStamp = Stamp
        };
        actor.Pii.Actor = actor;
        actor.User.Actor = actor;
        actor.AtprotoIdentities =
        [
            new AtprotoIdentity(AtprotoDid.Parse("did:plc:first"))
            {
                Id = Stamp, ActorId = ActorId, Actor = actor, Handle = "first.example.test", PdsHost = "https://pds.example.test",
                LastResolvedAt = ResolvedAt, DidCustodyTypeId = 10, IsActive = true, SigningKey = Guid.NewGuid().ToString()
            },
            new AtprotoIdentity(AtprotoDid.Parse("did:plc:second"))
            {
                Actor = actor, Handle = "second.example.test", PdsHost = "https://second-pds.example.test", LastResolvedAt = ResolvedAt.AddDays(1), IsActive = true
            }
        ];
        return actor;
    }

    private static StorageObject CreateStorage() => new()
    {
        Id = Stamp, FileTypeId = 8, FileType = new FileType { Id = 8, MasterCode = "IMAGE", FullName = "Image", Description = "private file metadata" },
        Uri = "https://images.example.test/file.png", ObjectKey = "private/object-key", Provider = "local", FullName = "file.png", SafeDisplayName = "Safe file", Extension = "png",
        ContentType = "image/png", Sha256Checksum = "checksum", Size = long.MaxValue - 1,
        Visibility = "public-image", Purpose = "profile-image", LifecycleState = "quarantined", OwningResourceKind = "Actor", OwningResourceId = ActorId,
        TenantId = TenantId, Tenant = new Tenant { Id = TenantId, FullName = "Tenant display", Slug = "private-slug", TenantStatus = null! },
        ActorId = ActorId, Actor = CreateActor(), IsDeleted = true, DeletedAt = ResolvedAt, DeletedBy = OwnerId,
        QuarantinedAt = ResolvedAt, QuarantinedBy = OwnerId, QuarantineReason = "review", RegistrationContentRetentionUntilUtc = ResolvedAt,
        CreatedAt = ResolvedAt, CreatedBy = OwnerId, UpdatedAt = ResolvedAt, UpdatedBy = OwnerId, ConcurrencyStamp = Stamp
    };

    private sealed record TenantContext(Guid TenantId) : ITenantContext;
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(ResolvedAt);
    }

    // Entity stores exercise the real handlers and projection/disclosure pipeline, not database filters.
    // Unused seams throw so these fixtures cannot silently accept an unexpected operation.
    private abstract class Store<T, TKey> : IGenericRepository<T, TKey> where T : class
    {
        public virtual Task<T?> GetById(TKey id) => throw new NotSupportedException();
        public Task<IReadOnlyList<T>> GetAll() => throw new NotSupportedException();
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public virtual Task<bool> Exists(TKey id) => throw new NotSupportedException();
        public virtual Task<T> Create(T entity) => throw new NotSupportedException();
        public Task Update(T entity) => throw new NotSupportedException();
        public Task Delete(T entity) => throw new NotSupportedException();
    }

    private sealed class ActorStore(List<Actor> items) : Store<Actor, Guid>, IActorRepository
    {
        public List<Actor> Items { get; } = items;
        public override Task<Actor> Create(Actor entity)
        {
            entity.Id = ActorId;
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public Task<Actor?> GetPublicActorProfileAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(actor => actor.Id == id));
        public Task<Actor?> GetPublicActorProfileByTenantAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default) =>
            tenantId == TenantId ? GetPublicActorProfileAsync(actorId, cancellationToken) : Task.FromResult<Actor?>(null);
        public Task<Actor?> GetLocallyDiscoverableSubscriptionTargetAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default) => GetPublicActorProfileByTenantAsync(tenantId, actorId, cancellationToken);
        public Task<Actor?> GetActorByDid(string did, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(actor => actor.AtprotoIdentities.Any(identity => identity.Did == did)));
        public Task<List<Actor>> GetActorsByTenant(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(tenantId == TenantId ? Items : []);
        public Task<(List<Actor> Items, int TotalCount)> GetActorsWithDetailsPaged(int pageNumber, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult((Items.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), Items.Count));
        public Task<bool> DidExists(string did) => Task.FromResult(Items.Any(actor => actor.AtprotoIdentities.Any(identity => identity.Did == did)));
        public Task<Actor?> GetActorByUserId(Guid userId) => Task.FromResult(Items.FirstOrDefault(actor => actor.UserId == userId));
        public Task<Actor?> GetActorByOrganizationId(Guid organizationId) => Task.FromResult(Items.FirstOrDefault(actor => actor.OrganizationId == organizationId));
        public Task<Actor?> GetActorWithDetails(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByHandle(string handle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Actor>> SearchAiReferenceActorsAsync(string searchTerm, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Actor?> GetTrackedActorByUserId(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByUserIdAndTenantId(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByGroupId(Guid groupId) => throw new NotSupportedException();
        public Task<int> ForgetPiiAsync(Guid actorId) => throw new NotSupportedException();
    }

    private sealed class StorageStore(List<StorageObject> items) : Store<StorageObject, Guid>, IStorageObjectRepository
    {
        public List<StorageObject> Items { get; } = items;
        public override Task<StorageObject?> GetById(Guid id) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public override Task<bool> Exists(Guid id) => Task.FromResult(Items.Any(item => item.Id == id));
        public Task<(List<StorageObject> Items, int TotalCount)> GetFilesWithDetailsPaged(int pageNumber, int pageSize) =>
            Task.FromResult((Items.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), Items.Count));
        public Task<RegistrationAnswerFile?> GetRegistrationAnswerFileAsync(Guid storageObjectId, Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<RegistrationAnswerFile?>(null);
        public Task<RegistrationOrder?> GetRegistrationContentOrderAsync(StorageObject storageObject, RegistrationAnswerFile? answerFile, CancellationToken cancellationToken) => Task.FromResult<RegistrationOrder?>(null);
        public Task<StorageObject?> GetFileWithDetails(Guid id) => throw new NotSupportedException();
        public Task<StorageObject?> GetForAuthorizationAsync(Guid id, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<StorageObject>> GetFilesWithDetails() => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageObject>> GetAllForInstanceStorageReportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageObject>> ListActiveForReconciliationAsync(DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageObject>> ListDeleteEligibleForReconciliationAsync(DateTime deleteBeforeUtc, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StorageObject>> ListDeleteRequestedForResourceAsync(Guid tenantId, string owningResourceKind, Guid owningResourceId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListKnownObjectKeysAsync(string provider, IReadOnlyCollection<string> objectKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<StorageObject?> GetEvidenceDocumentAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsRetainedEvidenceAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsRegistrationAnswerFileQuarantinedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ActorTypeStore : Store<ActorType, int>, IActorTypeRepository
    {
        public override Task<bool> Exists(int id) => Task.FromResult(id == 7);
    }

    private sealed class CustodyTypeStore : Store<DidCustodyType, int>, IDidCustodyTypeRepository
    {
        public override Task<bool> Exists(int id) => Task.FromResult(id == 10);
    }

    private sealed class UserStore : Store<User, Guid>, IUserRepository
    {
        public override Task<bool> Exists(Guid id) => Task.FromResult(id == OwnerId);
        public Task<User?> GetUserWithDetails(Guid id) => throw new NotSupportedException();
        public Task<User?> GetUserWithDetails(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> GetUserByEmail(string email) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> GetUsersByNormalizedEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsByEmail(string email) => throw new NotSupportedException();
        public Task<List<User>> GetUsersByIdsAsync(List<Guid> ids) => throw new NotSupportedException();
        public Task<int> ForgetPiiAsync(Guid userId) => throw new NotSupportedException();
    }
}
