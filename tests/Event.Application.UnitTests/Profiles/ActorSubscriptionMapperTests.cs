using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Application.UnitTests.Operations;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorSubscription;
using Explore.Application.Features.ActorSubscriptions.Handlers.Queries;
using Explore.Application.Features.ActorSubscriptions.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Profiles;

public sealed class ActorSubscriptionMapperTests
{
    private static readonly Guid SubscriptionId = Guid.Parse("01900000-0000-7000-8000-000000000001");
    private static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    private static readonly Guid MembershipId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    private static readonly Guid UserId = Guid.Parse("01900000-0000-7000-8000-000000000004");
    private static readonly Guid ActorId = Guid.Parse("01900000-0000-7000-8000-000000000005");
    private static readonly Guid Stamp = Guid.Parse("01900000-0000-7000-8000-000000000006");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task GeneratedProjections_DoNotRequireMapperlyAtRuntime()
    {
        var runtimeReferences = typeof(ActorSubscriptionMapper).Assembly.GetReferencedAssemblies();
        await Assert.That(runtimeReferences.Any(reference => reference.Name?.StartsWith("Riok.Mapperly", StringComparison.Ordinal) == true)).IsFalse();
    }

    [Test]
    public async Task DetailProjection_SerializesOnlyTheIndependentScalarContract()
    {
        await AssertContract(MapDetail(CreateSubscription()), ExpectedDetail());
    }

    [Test]
    public async Task ListProjection_SerializesOnlyTheIndependentScalarContract()
    {
        await AssertContract(MapListItem(CreateSubscription()), ExpectedList());
    }

    [Test]
    [Arguments("actorType")]
    [Arguments("actor")]
    [Arguments("pii")]
    [Arguments("status")]
    [Arguments("notificationLevel")]
    public async Task MissingNavigation_PreservesNullMetadata(string missing)
    {
        var source = CreateSubscription();
        var detail = ExpectedDetail();
        var list = ExpectedList();
        string[] fields;
        switch (missing)
        {
            case "actorType":
                source.TargetActorType = null!;
                fields = ["targetActorTypeName"];
                break;
            case "actor":
                source.TargetActor = null!;
                fields = ["targetActorName"];
                break;
            case "pii":
                source.TargetActor.Pii = null!;
                fields = ["targetActorName"];
                break;
            case "status":
                source.Status = null!;
                fields = ["statusCode", "statusName"];
                break;
            default:
                source.NotificationLevel = null!;
                fields = ["notificationLevelCode", "notificationLevelName"];
                break;
        }

        foreach (var field in fields)
        {
            detail[field] = null;
            list[field] = null;
        }

        await AssertContract(MapDetail(source), detail);
        await AssertContract(MapListItem(source), list);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task NullableScalars_DistinguishNullFromEmpty(string? value)
    {
        var source = CreateSubscription();
        source.TargetActorType.FullName = value!;
        source.TargetActor.Pii.DisplayName = value!;
        source.Status.MasterCode = value!;
        source.Status.FullName = value!;
        source.NotificationLevel.MasterCode = value!;
        source.NotificationLevel.FullName = value!;
        source.UnsubscribedAt = null;
        var detail = ExpectedDetail();
        var list = ExpectedList();
        foreach (var field in new[] { "targetActorTypeName", "targetActorName", "statusCode", "statusName", "notificationLevelCode", "notificationLevelName", "unsubscribedAt" })
        {
            detail[field] = field == "unsubscribedAt" ? null : JsonValue.Create(value);
            list[field] = field == "unsubscribedAt" ? null : JsonValue.Create(value);
        }

        await AssertContract(MapDetail(source), detail);
        await AssertContract(MapListItem(source), list);
    }

    [Test]
    [Arguments("anonymous")]
    [Arguments("missing-membership")]
    [Arguments("other-tenant")]
    [Arguments("other-user")]
    public async Task UnresolvedIdentity_ReturnsNullDetailAndNormalizedEmptyPage(string identity)
    {
        var source = CreateSubscription();
        var subscriptions = new SubscriptionStore([source]);
        var memberships = new MembershipStore(identity == "missing-membership" ? [] : [source.SubscriberTenantUser]);
        var context = new RequestContext(
            identity == "other-tenant" ? Stamp : TenantId,
            identity == "anonymous" ? null : identity == "other-user" ? Stamp : UserId);
        using var provider = QueryProvider(subscriptions, memberships, context);
        using var scope = provider.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetActorSubscriptionRequest, ActorSubscriptionDto?>>()
            .QueryAsync(new GetActorSubscriptionRequest { TargetActorId = ActorId }, default);
        var page = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetActorSubscriptionsRequest, PaginatedResult<ActorSubscriptionListDto>>>()
            .QueryAsync(new GetActorSubscriptionsRequest { PageNumber = -4, PageSize = 500 }, default);

        await Assert.That(detail).IsNull();
        await Assert.That(page.Items).IsEmpty();
        await Assert.That(page.TotalCount).IsEqualTo(0);
        await Assert.That(page.PageNumber).IsEqualTo(1);
        await Assert.That(page.PageSize).IsEqualTo(100);
    }

    [Test]
    public async Task DetailHandler_UsesResolvedIdentityAndPreservesMissingTarget()
    {
        var source = CreateSubscription();
        var subscriptions = new SubscriptionStore([source]);
        var memberships = new MembershipStore([source.SubscriberTenantUser]);
        using var provider = QueryProvider(subscriptions, memberships, new RequestContext(TenantId, UserId));
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetActorSubscriptionRequest, ActorSubscriptionDto?>>();

        await AssertContract(await handler.QueryAsync(new GetActorSubscriptionRequest { TargetActorId = ActorId }, default), ExpectedDetail());
        await Assert.That(await handler.QueryAsync(new GetActorSubscriptionRequest { TargetActorId = Stamp }, default)).IsNull();
    }

    [Test]
    [Arguments(-3, 0, 1, 1, 1)]
    [Arguments(1, 500, 1, 100, 3)]
    [Arguments(2, 1, 2, 1, 1)]
    public async Task ListHandler_PreservesNormalizedPagingCountOrderAndSourceIndependence(
        int requestedPage, int requestedSize, int expectedPage, int expectedSize, int expectedItems)
    {
        var oldest = CreateSubscription();
        oldest.Id = Guid.Parse("01900000-0000-7000-8000-000000000011");
        oldest.SubscribedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var middle = CreateSubscription();
        middle.Id = Guid.Parse("01900000-0000-7000-8000-000000000012");
        middle.SubscribedAt = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        var newest = CreateSubscription();
        var foreign = CreateSubscription();
        foreign.TenantId = Stamp;
        var otherSubscriber = CreateSubscription();
        otherSubscriber.SubscriberTenantUserId = Stamp;
        var subscriptions = new SubscriptionStore([oldest, newest, foreign, middle, otherSubscriber]);
        var memberships = new MembershipStore([newest.SubscriberTenantUser]);
        using var provider = QueryProvider(subscriptions, memberships, new RequestContext(TenantId, UserId));
        using var scope = provider.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetActorSubscriptionsRequest, PaginatedResult<ActorSubscriptionListDto>>>()
            .QueryAsync(new GetActorSubscriptionsRequest { PageNumber = requestedPage, PageSize = requestedSize }, default);

        await Assert.That(page.PageNumber).IsEqualTo(expectedPage);
        await Assert.That(page.PageSize).IsEqualTo(expectedSize);
        await Assert.That(page.TotalCount).IsEqualTo(3);
        await Assert.That(page.Items.Count).IsEqualTo(expectedItems);
        string[] expectedIds = requestedPage == 2
            ? ["01900000-0000-7000-8000-000000000012"]
            : expectedItems == 3
                ? ["01900000-0000-7000-8000-000000000001", "01900000-0000-7000-8000-000000000012", "01900000-0000-7000-8000-000000000011"]
                : ["01900000-0000-7000-8000-000000000001"];
        await Assert.That(page.Items.Select(item => item.Id.ToString()).SequenceEqual(expectedIds)).IsTrue();
        var serialized = JsonSerializer.Serialize(page.Items, JsonOptions);
        subscriptions.LastPage!.Clear();
        newest.TargetActor.Pii.DisplayName = "changed after projection";
        middle.Status.FullName = "changed after projection";
        await Assert.That(JsonSerializer.Serialize(page.Items, JsonOptions)).IsEqualTo(serialized);
    }

    private static async Task AssertContract<T>(T actual, JsonObject expected)
    {
        var serialized = JsonSerializer.SerializeToNode(actual, JsonOptions);
        await Assert.That(JsonNode.DeepEquals(serialized, expected)).IsTrue()
            .Because($"{typeof(T).Name} must preserve the scalar JSON contract. Expected {expected}; actual {serialized}");
    }

    private static JsonObject ExpectedList() => JsonNode.Parse("""
        {
          "id": "01900000-0000-7000-8000-000000000001",
          "tenantId": "01900000-0000-7000-8000-000000000002",
          "targetActorId": "01900000-0000-7000-8000-000000000005",
          "targetActorTypeId": 7,
          "targetActorTypeName": "Community",
          "targetActorName": "Public display",
          "statusId": 8,
          "statusCode": "ACTIVE",
          "statusName": "Active label",
          "notificationLevelId": 9,
          "notificationLevelCode": "ALL",
          "notificationLevelName": "All updates",
          "subscribedAt": "2026-09-03T12:00:00Z",
          "unsubscribedAt": "2026-09-04T13:00:00Z",
          "concurrencyStamp": "01900000-0000-7000-8000-000000000006"
        }
        """)!.AsObject();

    private static JsonObject ExpectedDetail()
    {
        var expected = ExpectedList();
        expected["subscriberTenantUserId"] = "01900000-0000-7000-8000-000000000003";
        expected["subscriberUserId"] = "01900000-0000-7000-8000-000000000004";
        return expected;
    }

    private static ActorSubscription CreateSubscription()
    {
        var user = new User { Id = UserId, Pii = new UserPii { Email = "private@example.test", FirstName = "Private", LastName = "Subscriber" } };
        var membership = new TenantUser { Id = MembershipId, TenantId = TenantId, UserId = UserId, User = user, Tenant = null!, ModerationNote = "private moderation" };
        var type = new ActorType { Id = 7, MasterCode = "PRIVATE_TYPE_CODE", FullName = "Community", Description = "private type metadata" };
        var actor = new Actor { Id = ActorId, ActorTypeId = 7, ActorType = type, User = user, Pii = new ActorPii { DisplayName = "Public display", ProfilePictureUri = "https://private.example.test/avatar" } };
        actor.Pii.Actor = actor;
        user.Actor = actor;
        return new ActorSubscription
        {
            Id = SubscriptionId, TenantId = TenantId, Tenant = null!,
            SubscriberTenantUserId = MembershipId, SubscriberTenantUser = membership,
            SubscriberUserId = UserId, SubscriberUser = user,
            TargetActorId = ActorId, TargetActor = actor, TargetActorTypeId = 7, TargetActorType = type,
            StatusId = 8, Status = new ActorSubscriptionStatus { Id = 8, MasterCode = "ACTIVE", FullName = "Active label", Description = "private status metadata" },
            NotificationLevelId = 9, NotificationLevel = new ActorSubscriptionNotificationLevel { Id = 9, MasterCode = "ALL", FullName = "All updates", Description = "private notification metadata" },
            SubscribedAt = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc),
            UnsubscribedAt = new DateTime(2026, 9, 4, 13, 0, 0, DateTimeKind.Utc),
            ConcurrencyStamp = Stamp, CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = Stamp, UpdatedAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), UpdatedBy = Stamp,
            IsDeleted = true, DeletedAt = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc), DeletedBy = Stamp
        };
    }

    private static ActorSubscriptionDto MapDetail(ActorSubscription source) => ActorSubscriptionMapper.ToDetail(source);
    private static ActorSubscriptionListDto MapListItem(ActorSubscription source) => ActorSubscriptionMapper.ToListItem(source);
    private static ServiceProvider QueryProvider(SubscriptionStore subscriptions, MembershipStore memberships, RequestContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IActorSubscriptionRepository>(subscriptions);
        services.AddSingleton<ITenantUserRepository>(memberships);
        services.AddSingleton<ITenantContext>(context);
        services.AddSingleton<ICurrentUserService>(context);
        services.AddSingleton<IAuthorizationProvider>(new OperationAuthorizationTests.Policy(
            AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)));
        services.AddNativeOperations(
        [
            typeof(GetActorSubscriptionRequest), typeof(GetActorSubscriptionRequestHandler),
            typeof(GetActorSubscriptionsRequest), typeof(GetActorSubscriptionsRequestHandler)
        ]);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private sealed record RequestContext(Guid TenantId, Guid? UserId) : ITenantContext, ICurrentUserService
    {
        public bool IsAuthenticated => UserId.HasValue;
    }

    // Only read seams used by these handlers are implemented; accidental writes fail explicitly.
    private abstract class ReadStore<T> : IGenericRepository<T, Guid> where T : class
    {
        public Task<T?> GetById(Guid id) => throw new NotSupportedException();
        public Task<IReadOnlyList<T>> GetAll() => throw new NotSupportedException();
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(Guid id) => throw new NotSupportedException();
        public Task<T> Create(T entity) => throw new NotSupportedException();
        public Task Update(T entity) => throw new NotSupportedException();
        public Task Delete(T entity) => throw new NotSupportedException();
    }

    private sealed class MembershipStore(List<TenantUser> memberships) : ReadStore<TenantUser>, ITenantUserRepository
    {
        public Task<TenantUser?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(memberships.SingleOrDefault(member => member.TenantId == tenantId && member.UserId == userId));
        public Task<TenantUser?> GetByTenantAndActorAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsActiveTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TenantUser>> GetActiveTenantsForUserAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryRemoveMembershipAsync(Guid tenantId, Guid userId, Guid removedBy, DateTime removedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SubscriptionStore(List<ActorSubscription> subscriptions) : ReadStore<ActorSubscription>, IActorSubscriptionRepository
    {
        public List<ActorSubscription>? LastPage { get; private set; }
        public Task<ActorSubscription?> GetDiscoverableBySubscriberAndTargetAsync(Guid tenantId, Guid subscriberTenantUserId, Guid targetActorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(subscriptions.SingleOrDefault(item => item.TenantId == tenantId && item.SubscriberTenantUserId == subscriberTenantUserId && item.TargetActorId == targetActorId));
        public Task<(List<ActorSubscription> Items, int TotalCount)> GetBySubscriberPagedAsync(Guid tenantId, Guid subscriberTenantUserId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            var visible = subscriptions.Where(item => item.TenantId == tenantId && item.SubscriberTenantUserId == subscriberTenantUserId).OrderByDescending(item => item.SubscribedAt).ToList();
            LastPage = visible.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult((LastPage, visible.Count));
        }
        public Task<ActorSubscription?> GetBySubscriberAndTargetAsync(Guid tenantId, Guid subscriberTenantUserId, Guid targetActorId, bool trackChanges = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ActorSubscription>> GetActiveFanoutBatchAsync(Guid tenantId, Guid targetActorId, Guid? afterSubscriberTenantUserId, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
