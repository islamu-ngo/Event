using System.Security.Claims;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventTags;
using Explore.Application.DTOs.Tag;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTags.Requests.Commands;
using Explore.Application.Features.EventTags.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Operations.Decorators;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventTagsOperationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnerDeletesUsingPersistedParent_NotAssignmentId(bool relational)
    {
        await using var factory = new NativeEventTagsFactory(relational);
        var data = await SeedAsync(factory);
        using var scope = Scope(factory, data.OwnerId);
        await Assert.That(await Send(scope, new DeleteEventTagsCommand { Id = data.AssignmentId })).IsTrue();
        await Assert.That(await Send(scope, new GetEventTagsDetailsRequest(data.AssignmentId))).IsNull();
        await Assert.That(async () => await Send(scope, new DeleteEventTagsCommand { Id = data.AssignmentId }))
            .Throws<AuthorizationException>();
        await Assert.That((await ListAsync(factory)).Select(row => row.Id)).IsEquivalentTo(new[] { data.OtherAssignmentId });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceOwnerCannotRelocateToUnauthorizedSameTenantDestination(bool relational)
    {
        await using var factory = new NativeEventTagsFactory(relational);
        var data = await SeedAsync(factory);
        var before = await ListAsync(factory);
        using var scope = Scope(factory, data.OwnerId);
        var original = await Send(scope, new GetEventTagsDetailsRequest(data.AssignmentId));
        await Assert.That(async () => await Send(scope, Create(data.OtherEventId, data.SecondTagId)))
            .Throws<AuthorizationException>();
        await Assert.That(async () => await Send(scope, Move(original, data.OtherEventId, data.SecondTagId) with
        { EventId = data.ForeignEventId, TenantId = data.ForeignTenantId })).Throws<AuthorizationException>();
        await Assert.That(await Send(scope, new GetEventTagsDetailsRequest(original.Id))).IsEqualTo(original);
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothAuthorized_RelocationChangesOnlyRequestedFields(bool relational)
    {
        await using var factory = new NativeEventTagsFactory(relational);
        var data = await SeedAsync(factory);
        using (var seed = Scope(factory))
        {
            var db = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId,
                data.OtherEventId, data.OwnerId, (int)RoleEnum.EventManager,
                EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, data.OtherOwnerId));
            await db.SaveChangesAsync();
        }
        using var scope = Scope(factory, data.OwnerId);
        var original = await Send(scope, new GetEventTagsDetailsRequest(data.AssignmentId));
        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        var sourceKey = $"event:detail:{data.EventId}";
        var destinationKey = $"event:detail:{data.OtherEventId}";
        var listKey = $"tag-assignment-test:{data.EventId}";
        await cache.SetAsync(sourceKey, "original-source");
        await cache.SetAsync(destinationKey, "original-destination");
        await cache.SetAsync(listKey, "original-list", tags:
            [Explore.Application.Caching.CacheTags.EventListByTenant(PlatformDefaults.DefaultTenantId)]);
        var result = await Send(scope, Move(original, data.OtherEventId, data.SecondTagId));
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await cache.GetOrCreateAsync(sourceKey, _ => ValueTask.FromResult("refreshed"))).IsEqualTo("refreshed");
        await Assert.That(await cache.GetOrCreateAsync(destinationKey, _ => ValueTask.FromResult("refreshed"))).IsEqualTo("refreshed");
        await Assert.That(await cache.GetOrCreateAsync(listKey, _ => ValueTask.FromResult("refreshed"), tags:
            [Explore.Application.Caching.CacheTags.EventListByTenant(PlatformDefaults.DefaultTenantId)])).IsEqualTo("refreshed");
        var updated = await Send(scope, new GetEventTagsDetailsRequest(original.Id));
        await Assert.That(updated.EventId).IsEqualTo(data.OtherEventId);
        await Assert.That(updated.TagId).IsEqualTo(data.SecondTagId);
        await Assert.That(updated.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(updated.ConcurrencyStamp).IsNotEqualTo(original.ConcurrencyStamp);
        await Assert.That(original.EventId).IsEqualTo(data.EventId);
        await Assert.That((await ListAsync(factory)).Any(row => row.EventId == data.EventId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingForeignAndWrongOwnerAssignments_FailClosedWithoutMutation(bool relational)
    {
        await using var factory = new NativeEventTagsFactory(relational);
        var data = await SeedAsync(factory);
        var before = await ListAsync(factory);
        var foreignBefore = await ListAsync(factory, data.ForeignTenantId);
        using var scope = Scope(factory, data.OwnerId);
        foreach (var id in new[] { Guid.CreateVersion7(), data.ForeignAssignmentId, data.OtherAssignmentId })
        {
            await Assert.That(async () => await Send(scope, new DeleteEventTagsCommand { Id = id }))
                .Throws<AuthorizationException>();
            await Assert.That(async () => await Send(scope, new UpdateEventTagsCommand
            {
                EventTagId = id, EventId = data.EventId, TenantId = PlatformDefaults.DefaultTenantId,
                EventTagsDto = new() { Tag = new() { TagId = data.SecondTagId } }
            })).Throws<AuthorizationException>();
        }
        await Assert.That(await Send(scope, new GetEventTagsDetailsRequest(data.ForeignAssignmentId))).IsNull();
        using (var member = Scope(factory, data.MemberId))
        {
            await Assert.That(async () => await Send(member, Create(data.EventId, data.SecondTagId)))
                .Throws<AuthorizationException>();
        }
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
        await Assert.That(await ListAsync(factory, data.ForeignTenantId)).IsEquivalentTo(foreignBefore);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DestinationDenialAndOutage_PrecedeTrackedMutationAndCacheEviction(bool relational, bool unavailable)
    {
        Guid destinationId = Guid.Empty;
        AuthorizationRequest? observed = null;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<AuthorizationRequest>();
            ArgumentNullException.ThrowIfNull(request);
            if (request.ResourceId != destinationId.ToString())
                return AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos);
            observed = request;
            return AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos,
                unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied);
        });
        await using var factory = new NativeEventTagsFactory(relational) { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        destinationId = data.OtherEventId;
        var before = await ListAsync(factory);
        using var scope = Scope(factory, data.OwnerId);
        var original = await Send(scope, new GetEventTagsDetailsRequest(data.AssignmentId));
        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>();
        var key = $"event:detail:{data.EventId}";
        await cache.SetAsync(key, "original");
        Func<Task> move = async () => { await Send(scope, Move(original, destinationId, data.SecondTagId)); };
        if (unavailable)
            await Assert.That(move).Throws<AuthorizationProviderUnavailableException>();
        else
            await Assert.That(move).Throws<AuthorizationException>();
        await Assert.That(await Send(scope, new GetEventTagsDetailsRequest(original.Id))).IsEqualTo(original);
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
        await Assert.That(await cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("evicted"))).IsEqualTo("original");
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(observed.Action).IsEqualTo(AuthorizationActions.Update);
        var facts = (EventAuthorizationFacts)observed.Facts!;
        await Assert.That(facts.EventId).IsEqualTo(destinationId);
        await Assert.That(facts.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(facts.UserId).IsEqualTo(data.OtherOwnerId);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SourceDenialAndOutage_BlockEveryWriteWithUnchangedTrackedAndFreshState(bool relational, bool unavailable)
    {
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos,
                unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied));
        await using var factory = new NativeEventTagsFactory(relational) { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        var before = await ListAsync(factory);
        using var scope = Scope(factory, data.OwnerId);
        var original = await Send(scope, new GetEventTagsDetailsRequest(data.AssignmentId));
        Func<Task>[] writes =
        [
            async () => { await Send(scope, Create(data.EventId, data.SecondTagId)); },
            async () => { await Send(scope, Move(original, data.OtherEventId, data.SecondTagId)); },
            async () => { await Send(scope, new DeleteEventTagsCommand { Id = original.Id }); }
        ];
        foreach (var write in writes)
        {
            if (unavailable)
                await Assert.That(write).Throws<AuthorizationProviderUnavailableException>();
            else
                await Assert.That(write).Throws<AuthorizationException>();
            await Assert.That(await Send(scope, new GetEventTagsDetailsRequest(original.Id))).IsEqualTo(original);
        }
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidationConcurrencyTenantAndRelationshipReads_RemainIntact(bool relational)
    {
        await using var factory = new NativeEventTagsFactory(relational);
        var data = await SeedAsync(factory);
        using var scope = Scope(factory, data.OwnerId);
        var created = await Send(scope, Create(data.EventId, data.SecondTagId) with
        { EventTagsDto = new() { EventId = data.EventId, TagId = data.SecondTagId, TenantId = data.ForeignTenantId } });
        await Assert.That(created.IsSuccess).IsTrue();
        var original = await Send(scope, new GetEventTagsDetailsRequest(created.Id));
        await Assert.That(original.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That((await Send(scope, Create(data.EventId, data.SecondTagId))).IsSuccess).IsFalse();
        await Assert.That((await Send(scope, Create(data.EventId, data.ForeignTagId))).IsSuccess).IsFalse();
        var empty = Move(original, data.EventId, data.SecondTagId) with { EventTagsDto = new() };
        await Assert.That((await Send(scope, empty)).IsSuccess).IsFalse();
        await Assert.That((await Send(scope, Move(original, data.ForeignEventId, data.SecondTagId))).IsSuccess).IsFalse();
        await Assert.That((await Send(scope, Move(original, data.EventId, data.ForeignTagId))).IsSuccess).IsFalse();
        await Assert.That((await Send(scope, Move(original, data.EventId, Guid.CreateVersion7()))).IsSuccess).IsFalse();
        await Assert.That((await Send(scope, Move(original, data.EventId, data.TagId))).IsSuccess).IsFalse();
        await Assert.That(await Send(scope, new GetEventTagsDetailsRequest(original.Id))).IsEqualTo(original);
        await Assert.That(async () => await Send(scope, Move(original, data.EventId, data.SecondTagId) with
        { ExpectedConcurrencyStamp = Guid.CreateVersion7() })).Throws<ConcurrencyConflictException>();
        var changed = await Send(scope, Move(original, data.EventId, data.SecondTagId) with
        { EventId = data.ForeignEventId, TenantId = data.ForeignTenantId });
        await Assert.That(changed.IsSuccess).IsTrue();
        await Assert.That((await Send(scope, new GetEventTagsDetailsRequest(original.Id))).ConcurrencyStamp)
            .IsNotEqualTo(original.ConcurrencyStamp);

        using var readScope = Scope(factory);
        var detail = await Send(readScope, new GetEventTagsDetailsRequest(original.Id));
        await Assert.That(detail.EventTitle).IsNull();
        await Assert.That(detail.TagFullName).IsNull();
        await Assert.That(await Send(readScope, new GetEventTagsDetailsRequest(Guid.CreateVersion7()))).IsNull();
        var tags = await Send(readScope, new GetTagsByEventRequest(data.OtherEventId));
        await Assert.That(tags.Select(tag => tag.FullName)).IsEquivalentTo(new[] { "Community" });
        var events = await Send(readScope, new GetEventsByTagRequest(data.TagId));
        await Assert.That(events.Select(item => item.Id)).IsEquivalentTo(new[] { data.EventId, data.OtherEventId });
        await Assert.That(events.Single(item => item.Id == data.OtherEventId).Title).IsEqualTo("Other Event");
        await Assert.That(await Send(readScope, new GetTagsByEventRequest(data.ForeignEventId))).IsEmpty();
    }

    [Test]
    public async Task Sqlite_EnforcesUniquenessAndBothTenantCompositeForeignKeys()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        var data = await SeedAsync(factory);
        var before = await ListAsync(factory);
        (Guid EventId, Guid TagId)[] invalid =
        [
            (data.EventId, data.TagId),
            (data.EventId, data.ForeignTagId),
            (data.ForeignEventId, data.TagId)
        ];
        foreach (var pair in invalid)
        {
            using var scope = Scope(factory);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventTags.Add(Assignment(pair.EventId, pair.TagId, PlatformDefaults.DefaultTenantId));
            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        }
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
    }

    private static async Task<BaseCommandResponse<Guid>> Send(IServiceScope scope, CreateEventTagsCommand request)
    {
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventTagsCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(port).IsTypeOf<AuthorizationCommandHandlerDecorator<CreateEventTagsCommand, BaseCommandResponse<Guid>>>();
        return await port.ExecuteAsync(request, default);
    }

    private static async Task<BaseCommandResponse<Guid>> Send(IServiceScope scope, UpdateEventTagsCommand request)
    {
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventTagsCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(port).IsTypeOf<AuthorizationCommandHandlerDecorator<UpdateEventTagsCommand, BaseCommandResponse<Guid>>>();
        return await port.ExecuteAsync(request, default);
    }

    private static async Task<bool> Send(IServiceScope scope, DeleteEventTagsCommand request)
    {
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventTagsCommand, bool>>();
        await Assert.That(port).IsTypeOf<AuthorizationCommandHandlerDecorator<DeleteEventTagsCommand, bool>>();
        return await port.ExecuteAsync(request, default);
    }

    private static async Task<EventTagsDto> Send(IServiceScope scope, GetEventTagsDetailsRequest request)
    {
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventTagsDetailsRequest, EventTagsDto>>();
        await Assert.That(port).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventTagsDetailsRequest, EventTagsDto>>();
        return await port.QueryAsync(request, default);
    }

    private static async Task<List<EventTagsListDto>> Send(IServiceScope scope, GetEventTagsListRequest request)
    {
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventTagsListRequest, List<EventTagsListDto>>>();
        await Assert.That(port).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventTagsListRequest, List<EventTagsListDto>>>();
        return await port.QueryAsync(request, default);
    }

    private static async Task<List<TagListDto>> Send(IServiceScope scope, GetTagsByEventRequest request)
    {
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTagsByEventRequest, List<TagListDto>>>();
        await Assert.That(port).IsTypeOf<AuthorizationQueryHandlerDecorator<GetTagsByEventRequest, List<TagListDto>>>();
        return await port.QueryAsync(request, default);
    }

    private static async Task<List<EventListDto>> Send(IServiceScope scope, GetEventsByTagRequest request)
    {
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventsByTagRequest, List<EventListDto>>>();
        await Assert.That(port).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventsByTagRequest, List<EventListDto>>>();
        return await port.QueryAsync(request, default);
    }

    private static CreateEventTagsCommand Create(Guid eventId, Guid tagId) => new()
    { EventTagsDto = new() { EventId = eventId, TagId = tagId } };

    private static UpdateEventTagsCommand Move(EventTagsDto row, Guid eventId, Guid tagId) => new()
    {
        EventTagId = row.Id, EventId = row.EventId, TenantId = row.TenantId,
        ExpectedConcurrencyStamp = row.ConcurrencyStamp,
        EventTagsDto = new() { Event = new() { EventId = eventId }, Tag = new() { TagId = tagId } }
    };

    private static IServiceScope Scope(NativeEventTagsFactory factory, Guid? userId = null, Guid? tenantId = null)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId ?? PlatformDefaults.DefaultTenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = userId is { } id
                ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "Test"))
                : new ClaimsPrincipal(new ClaimsIdentity())
        };
        return scope;
    }

    private static async Task<List<EventTagsListDto>> ListAsync(NativeEventTagsFactory factory, Guid? tenantId = null)
    {
        using var scope = Scope(factory, tenantId: tenantId);
        return await Send(scope, new GetEventTagsListRequest());
    }

    private static EventTags Assignment(Guid eventId, Guid tagId, Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(), ConcurrencyStamp = Guid.CreateVersion7(),
        EventId = eventId, TagId = tagId, TenantId = tenantId, Event = null!, Tag = null!, Tenant = null!
    };

    private static async Task<SeedData> SeedAsync(NativeEventTagsFactory factory)
    {
        using var scope = Scope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).WithSlug("event-tags").Build();
        var foreignTenant = new TenantBuilder().WithSlug("foreign-event-tags").Build();
        var owner = new UserBuilder().WithEmail($"tag-owner-{Guid.CreateVersion7():N}@example.test").Build();
        var member = new UserBuilder().WithEmail($"tag-member-{Guid.CreateVersion7():N}@example.test").Build();
        var otherOwner = new UserBuilder().WithEmail($"tag-other-{Guid.CreateVersion7():N}@example.test").Build();
        var ownerActor = new ActorBuilder().WithUserId(owner.Id).WithDisplayName("Owner").Build();
        var otherActor = new ActorBuilder().WithUserId(otherOwner.Id).WithDisplayName("Other owner").Build();
        var local = new EventBuilder().WithTitle("Owned Event").WithTenantId(tenant.Id).WithActorId(ownerActor.Id).Build();
        local.Actor = ownerActor;
        local.Tenant = tenant;
        var other = new EventBuilder().WithTitle("Other Event").WithTenantId(tenant.Id).WithActorId(otherActor.Id).Build();
        other.Actor = otherActor;
        other.Tenant = tenant;
        var foreign = new EventBuilder().WithTitle("Foreign Event").WithTenantId(foreignTenant.Id).WithActorId(ownerActor.Id).Build();
        foreign.Actor = ownerActor;
        foreign.Tenant = foreignTenant;
        var tag = new Tag { Id = Guid.CreateVersion7(), FullName = "Community", MasterCode = "COMMUNITY", TenantId = tenant.Id, Tenant = tenant };
        var secondTag = new Tag { Id = Guid.CreateVersion7(), FullName = "Workshop", MasterCode = "WORKSHOP", TenantId = tenant.Id, Tenant = tenant };
        var foreignTag = new Tag { Id = Guid.CreateVersion7(), FullName = "Foreign", MasterCode = "FOREIGN", TenantId = foreignTenant.Id, Tenant = foreignTenant };
        db.AddRange(tenant, foreignTenant, owner, member, otherOwner, ownerActor, otherActor,
            local, other, foreign, tag, secondTag, foreignTag);
        var assignment = Assignment(local.Id, tag.Id, tenant.Id);
        var otherAssignment = Assignment(other.Id, tag.Id, tenant.Id);
        var foreignAssignment = Assignment(foreign.Id, foreignTag.Id, foreignTenant.Id);
        db.EventTags.AddRange(assignment, otherAssignment, foreignAssignment);
        db.EventRoleAssignments.Add(EventRoleAssignment.Create(tenant.Id, local.Id, owner.Id,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, owner.Id));
        await db.SaveChangesAsync();
        return new(owner.Id, member.Id, otherOwner.Id, local.Id, other.Id, foreign.Id, foreignTenant.Id,
            tag.Id, secondTag.Id, foreignTag.Id, assignment.Id, otherAssignment.Id, foreignAssignment.Id);
    }

    private sealed record SeedData(Guid OwnerId, Guid MemberId, Guid OtherOwnerId, Guid EventId, Guid OtherEventId,
        Guid ForeignEventId, Guid ForeignTenantId, Guid TagId, Guid SecondTagId, Guid ForeignTagId,
        Guid AssignmentId, Guid OtherAssignmentId, Guid ForeignAssignmentId);
}
