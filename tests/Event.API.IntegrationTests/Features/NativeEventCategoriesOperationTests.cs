using System.Security.Claims;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCategories.Requests.Commands;
using Explore.Application.Features.EventCategories.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEventCategoriesOperationTests
{
    [Test]
    public async Task Owner_CreateAndUpdatePreserveValidationConcurrencyMappingAndTenantAuthority()
    {
        await using var factory = new NativeEventCategoriesFactory();
        var data = await SeedAsync(factory);
        var created = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.CategoryId, data.ForeignTenantId));
        await Assert.That(created.IsSuccess).IsTrue();
        var original = await DetailAsync(factory, created.Id);
        await Assert.That(original.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(original.EventId).IsEqualTo(data.EventId);
        await Assert.That(original.CategoryId).IsEqualTo(data.CategoryId);
        // Generic relationship reads do not hydrate navigation labels.
        await Assert.That(original.EventTitle).IsNull();
        await Assert.That(original.CategoryFullName).IsNull();

        var duplicate = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.CategoryId));
        await Assert.That(duplicate.IsSuccess).IsFalse();
        await Assert.That(duplicate.Errors).IsNotEmpty();
        var foreignCategory = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.ForeignCategoryId));
        await Assert.That(foreignCategory.IsSuccess).IsFalse();
        var empty = await UpdateAsync(factory, data.OwnerId, Update(original, new()));
        await Assert.That(empty.IsSuccess).IsFalse();

        var changed = await UpdateAsync(factory, data.OwnerId, Update(original,
            new() { Category = new() { CategoryId = data.SecondCategoryId } }) with
        {
            EventId = data.ForeignEventId,
            TenantId = data.ForeignTenantId
        });
        await Assert.That(changed.IsSuccess).IsTrue();
        var current = await DetailAsync(factory, created.Id);
        await Assert.That(current.CategoryId).IsEqualTo(data.SecondCategoryId);
        await Assert.That(current.ConcurrencyStamp).IsNotEqualTo(original.ConcurrencyStamp);
        await Assert.That(original.CategoryId).IsEqualTo(data.CategoryId);
        await Assert.That(async () => await UpdateAsync(factory, data.OwnerId, Update(original,
            new() { Category = new() { CategoryId = data.CategoryId } }))).Throws<ConcurrencyConflictException>();

        var foreignMove = await UpdateAsync(factory, data.OwnerId, Update(current,
            new() { Event = new() { EventId = data.ForeignEventId } }));
        await Assert.That(foreignMove.IsSuccess).IsFalse();
        var foreignReclassify = await UpdateAsync(factory, data.OwnerId, Update(current,
            new() { Category = new() { CategoryId = data.ForeignCategoryId } }));
        await Assert.That(foreignReclassify.IsSuccess).IsFalse();
        var missingCategory = await UpdateAsync(factory, data.OwnerId, Update(current,
            new() { Category = new() { CategoryId = Guid.CreateVersion7() } }));
        await Assert.That(missingCategory.IsSuccess).IsFalse();
        await Assert.That(await DetailAsync(factory, created.Id)).IsEqualTo(current);

        var second = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.CategoryId));
        await Assert.That(second.IsSuccess).IsTrue();
        var duplicateUpdate = await UpdateAsync(factory, data.OwnerId, Update(current,
            new() { Category = new() { CategoryId = data.CategoryId } }));
        await Assert.That(duplicateUpdate.IsSuccess).IsFalse();
        await Assert.That(await DetailAsync(factory, created.Id)).IsEqualTo(current);
    }

    [Test]
    public async Task LocalPolicy_DeniesWrongParentForeignTenantAndUnprivilegedWritesBeforeMutation()
    {
        await using var factory = new NativeEventCategoriesFactory();
        var data = await SeedAsync(factory);
        var original = await DetailAsync(factory, data.OtherAssignmentId);
        await Assert.That(async () => await CreateAsync(factory, data.MemberId,
            Create(data.EventId, data.CategoryId))).Throws<AuthorizationException>();
        await Assert.That(async () => await CreateAsync(factory, data.OwnerId,
            Create(data.OtherEventId, data.SecondCategoryId))).Throws<AuthorizationException>();
        await Assert.That(async () => await CreateAsync(factory, data.OwnerId,
            Create(data.ForeignEventId, data.CategoryId))).Throws<AuthorizationException>();
        await Assert.That(async () => await UpdateAsync(factory, data.OwnerId,
            Update(original, new() { Category = new() { CategoryId = data.SecondCategoryId } }) with
            { EventId = data.EventId, TenantId = PlatformDefaults.DefaultTenantId })).Throws<AuthorizationException>();
        await Assert.That(async () => await DeleteAsync(factory, data.OwnerId, data.OtherAssignmentId))
            .Throws<AuthorizationException>();
        await Assert.That(async () => await DeleteAsync(factory, data.OwnerId, data.ForeignAssignmentId))
            .Throws<AuthorizationException>();
        await Assert.That(async () => await UpdateAsync(factory, data.OwnerId, new()
        {
            EventCategoryId = data.ForeignAssignmentId,
            EventId = data.EventId,
            EventCategoriesDto = new() { Category = new() { CategoryId = data.CategoryId } }
        })).Throws<AuthorizationException>();
        await Assert.That(await DetailAsync(factory, data.OtherAssignmentId)).IsEqualTo(original);
        await Assert.That((await ListAsync(factory)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task LocalPolicy_EventOwnerDeletesAssignmentThroughPersistedParentAuthority()
    {
        await using var factory = new NativeEventCategoriesFactory();
        var data = await SeedAsync(factory);
        var created = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.SecondCategoryId));
        await Assert.That(created.IsSuccess).IsTrue();
        await Assert.That(await DeleteAsync(factory, data.OwnerId, created.Id)).IsTrue();
        await Assert.That(await DetailAsync(factory, created.Id)).IsNull();
        await Assert.That((await ListAsync(factory)).Select(row => row.Id))
            .IsEquivalentTo(new[] { data.OtherAssignmentId });
    }

    [Test]
    public async Task LocalPolicy_SourceAuthorityCannotWriteIntoAnotherOwnersSameTenantEvent()
    {
        await using var factory = new NativeEventCategoriesFactory();
        var data = await SeedAsync(factory);
        var created = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.SecondCategoryId));
        await Assert.That(created.IsSuccess).IsTrue();
        var original = await DetailAsync(factory, created.Id);
        var other = await DetailAsync(factory, data.OtherAssignmentId);
        var before = await ListAsync(factory);
        await Assert.That(async () => await CreateAsync(factory, data.OwnerId,
            Create(data.OtherEventId, data.SecondCategoryId))).Throws<AuthorizationException>();
        await Assert.That(async () => await UpdateAsync(factory, data.OwnerId, Update(original,
            new() { Event = new() { EventId = data.OtherEventId } }))).Throws<AuthorizationException>();
        await Assert.That(await DetailAsync(factory, created.Id)).IsEqualTo(original);
        await Assert.That(await DetailAsync(factory, data.OtherAssignmentId)).IsEqualTo(other);
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
    }

    [Test]
    public async Task LocalPolicy_ExplicitDestinationEventGrantAllowsRelocation()
    {
        await using var factory = new NativeEventCategoriesFactory();
        var data = await SeedAsync(factory);
        var created = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.SecondCategoryId));
        await Assert.That(created.IsSuccess).IsTrue();
        var original = await DetailAsync(factory, created.Id);
        using (var scope = Scope(factory))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId,
                data.OtherEventId, data.OwnerId, (int)RoleEnum.EventManager,
                EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, data.OtherOwnerId));
            await db.SaveChangesAsync();
        }
        var moved = await UpdateAsync(factory, data.OwnerId, Update(original,
            new() { Event = new() { EventId = data.OtherEventId } }));
        await Assert.That(moved.IsSuccess).IsTrue();
        var result = await DetailAsync(factory, created.Id);
        await Assert.That(result.EventId).IsEqualTo(data.OtherEventId);
        await Assert.That(result.CategoryId).IsEqualTo(data.SecondCategoryId);
        await Assert.That(result.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(result.ConcurrencyStamp).IsNotEqualTo(original.ConcurrencyStamp);
        await Assert.That((await ListAsync(factory)).Any(row => row.EventId == data.EventId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DestinationDenialOrOutage_PrecedesAllMutationAndPreservesBothEvents(bool unavailable)
    {
        Guid destinationId = Guid.Empty;
        AuthorizationRequest? destinationCheck = null;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<AuthorizationRequest>();
                ArgumentNullException.ThrowIfNull(request);
                if (request.ResourceId != destinationId.ToString())
                    return AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos);
                destinationCheck = request;
                return AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos,
                    unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied);
            });
        await using var factory = new NativeEventCategoriesFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        destinationId = data.OtherEventId;
        var created = await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.CategoryId));
        await Assert.That(created.IsSuccess).IsTrue();
        var original = await DetailAsync(factory, created.Id);
        var before = await ListAsync(factory);
        using (var scope = Scope(factory, data.OwnerId))
        {
            var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventCategoriesCommand, BaseCommandResponse<Guid>>>();
            var command = Update(original, new()
            {
                Event = new() { EventId = destinationId },
                Category = new() { CategoryId = data.SecondCategoryId }
            }) with
            { EventId = data.ForeignEventId, TenantId = data.ForeignTenantId };
            Func<Task> move = async () => { await port.ExecuteAsync(command, default); };
            if (unavailable)
                await Assert.That(move).Throws<AuthorizationProviderUnavailableException>();
            else
                await Assert.That(move).Throws<AuthorizationException>();

            // Even the tracked assignment must remain unchanged before the scope is discarded.
            var trackedRead = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCategoriesDetailsRequest, EventCategoriesDto>>()
                .QueryAsync(new(original.Id), default);
            await Assert.That(trackedRead.EventId).IsEqualTo(original.EventId);
            await Assert.That(trackedRead.CategoryId).IsEqualTo(original.CategoryId);
            await Assert.That(trackedRead.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        }
        await Assert.That(await DetailAsync(factory, created.Id)).IsEqualTo(original);
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(before);
        await Assert.That(destinationCheck).IsNotNull();
        await Assert.That(destinationCheck!.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(destinationCheck.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(destinationCheck.Facts).IsTypeOf<EventAuthorizationFacts>();
        var facts = (EventAuthorizationFacts)destinationCheck.Facts!;
        await Assert.That(facts.EventId).IsEqualTo(destinationId);
        await Assert.That(facts.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(facts.UserId).IsEqualTo(data.OtherOwnerId);
    }

    [Test]
    public async Task QueriesAndAllowedDelete_PreserveNullMissingResultsTenantIsolationAndRelationshipProjection()
    {
        await using var factory = new NativeEventCategoriesFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        var data = await SeedAsync(factory);
        var rows = await ListAsync(factory);
        await Assert.That(rows.Select(row => row.Id)).IsEquivalentTo(new[] { data.OtherAssignmentId });
        await Assert.That(await DetailAsync(factory, data.ForeignAssignmentId)).IsNull();
        await Assert.That(await DetailAsync(factory, Guid.CreateVersion7())).IsNull();
        using (var scope = Scope(factory))
        {
            var categoriesPort = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCategoriesByEventRequest, List<CategoryListDto>>>();
            await Assert.That(categoriesPort).IsTypeOf<AuthorizationQueryHandlerDecorator<GetCategoriesByEventRequest, List<CategoryListDto>>>();
            var categories = await categoriesPort.QueryAsync(new GetCategoriesByEventRequest(data.OtherEventId), default);
            await Assert.That(categories.Select(category => category.FullName)).IsEquivalentTo(new[] { "Lecture" });
            var eventsPort = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventsByCategoryRequest, List<EventListDto>>>();
            await Assert.That(eventsPort).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventsByCategoryRequest, List<EventListDto>>>();
            var events = await eventsPort.QueryAsync(new GetEventsByCategoryRequest(data.CategoryId), default);
            await Assert.That(events.Select(item => item.Id)).IsEquivalentTo(new[] { data.OtherEventId });
            await Assert.That(events.Single().Title).IsEqualTo("Other Event");
            await Assert.That(await categoriesPort.QueryAsync(new GetCategoriesByEventRequest(data.ForeignEventId), default)).IsEmpty();
        }
        List<EventCategoriesListDto> foreignBefore;
        using (var foreignScope = Scope(factory, tenantId: data.ForeignTenantId))
        {
            foreignBefore = await foreignScope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCategoriesListRequest, List<EventCategoriesListDto>>>()
                .QueryAsync(new GetEventCategoriesListRequest(), default);
            await Assert.That(foreignBefore.Select(row => row.Id)).IsEquivalentTo(new[] { data.ForeignAssignmentId });
        }
        await Assert.That(async () => await DeleteAsync(factory, data.OwnerId, data.ForeignAssignmentId))
            .Throws<AuthorizationException>();
        await Assert.That(async () => await DeleteAsync(factory, data.OwnerId, Guid.CreateVersion7()))
            .Throws<AuthorizationException>();
        await Assert.That(await ListAsync(factory)).IsEquivalentTo(rows);
        using (var foreignScope = Scope(factory, tenantId: data.ForeignTenantId))
        {
            var foreignAfter = await foreignScope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCategoriesListRequest, List<EventCategoriesListDto>>>()
                .QueryAsync(new GetEventCategoriesListRequest(), default);
            await Assert.That(foreignAfter).IsEquivalentTo(foreignBefore);
        }
        await Assert.That(await DeleteAsync(factory, data.OwnerId, data.OtherAssignmentId)).IsTrue();
        await Assert.That(async () => await DeleteAsync(factory, data.OwnerId, data.OtherAssignmentId))
            .Throws<AuthorizationException>();
        await Assert.That(await ListAsync(factory)).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderDenialOrOutage_LeavesAllWritesUnchanged(bool unavailable)
    {
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos,
                unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied));
        await using var factory = new NativeEventCategoriesFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        var original = await DetailAsync(factory, data.OtherAssignmentId);
        Func<Task>[] writes =
        [
            async () => { await CreateAsync(factory, data.OwnerId, Create(data.EventId, data.CategoryId)); },
            async () => { await UpdateAsync(factory, data.OwnerId, Update(original,
                new() { Category = new() { CategoryId = data.SecondCategoryId } })); },
            async () => { await DeleteAsync(factory, data.OwnerId, data.OtherAssignmentId); }
        ];
        foreach (var write in writes)
        {
            if (unavailable)
                await Assert.That(write).Throws<AuthorizationProviderUnavailableException>();
            else
                await Assert.That(write).Throws<AuthorizationException>();
        }
        await Assert.That(await DetailAsync(factory, data.OtherAssignmentId)).IsEqualTo(original);
        await Assert.That((await ListAsync(factory)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task RelationalConstraints_RejectDuplicateAndBothCrossTenantForeignKeys()
    {
        await using var factory = new NativeEventCategoriesFactory();
        var data = await SeedAsync(factory);
        (Guid Event, Guid Category)[] invalid =
        [
            (data.OtherEventId, data.CategoryId),
            (data.EventId, data.ForeignCategoryId),
            (data.ForeignEventId, data.CategoryId)
        ];
        foreach (var pair in invalid)
        {
            using var scope = Scope(factory);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.EventCategories.Add(Assignment(pair.Event, pair.Category, PlatformDefaults.DefaultTenantId));
            await Assert.That(async () => await db.SaveChangesAsync()).Throws<DbUpdateException>();
        }
        await Assert.That((await ListAsync(factory)).Select(row => row.Id))
            .IsEquivalentTo(new[] { data.OtherAssignmentId });
    }

    private static CreateEventCategoriesCommand Create(Guid eventId, Guid categoryId, Guid tenantId = default) => new()
    {
        EventCategoriesDto = new() { EventId = eventId, CategoryId = categoryId, TenantId = tenantId }
    };

    private static UpdateEventCategoriesCommand Update(EventCategoriesDto row, UpdateEventCategoriesDto change) => new()
    {
        EventCategoryId = row.Id,
        ExpectedConcurrencyStamp = row.ConcurrencyStamp,
        EventCategoriesDto = change,
        EventId = row.EventId,
        TenantId = row.TenantId
    };

    private static IServiceScope Scope(NativeEventCategoriesFactory factory, Guid? userId = null, Guid? tenantId = null)
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

    private static async Task<BaseCommandResponse<Guid>> CreateAsync(NativeEventCategoriesFactory factory, Guid userId, CreateEventCategoriesCommand command)
    {
        using var scope = Scope(factory, userId);
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCategoriesCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(port).IsTypeOf<AuthorizationCommandHandlerDecorator<CreateEventCategoriesCommand, BaseCommandResponse<Guid>>>();
        return await port.ExecuteAsync(command, default);
    }

    private static async Task<BaseCommandResponse<Guid>> UpdateAsync(NativeEventCategoriesFactory factory, Guid userId, UpdateEventCategoriesCommand command)
    {
        using var scope = Scope(factory, userId);
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventCategoriesCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(port).IsTypeOf<AuthorizationCommandHandlerDecorator<UpdateEventCategoriesCommand, BaseCommandResponse<Guid>>>();
        return await port.ExecuteAsync(command, default);
    }

    private static async Task<bool> DeleteAsync(NativeEventCategoriesFactory factory, Guid userId, Guid id)
    {
        using var scope = Scope(factory, userId);
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventCategoriesCommand, bool>>();
        await Assert.That(port).IsTypeOf<AuthorizationCommandHandlerDecorator<DeleteEventCategoriesCommand, bool>>();
        return await port.ExecuteAsync(new DeleteEventCategoriesCommand { Id = id }, default);
    }

    private static async Task<EventCategoriesDto> DetailAsync(NativeEventCategoriesFactory factory, Guid id)
    {
        using var scope = Scope(factory);
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCategoriesDetailsRequest, EventCategoriesDto>>();
        await Assert.That(port).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventCategoriesDetailsRequest, EventCategoriesDto>>();
        return await port.QueryAsync(new GetEventCategoriesDetailsRequest(id), default);
    }

    private static async Task<List<EventCategoriesListDto>> ListAsync(NativeEventCategoriesFactory factory)
    {
        using var scope = Scope(factory);
        var port = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventCategoriesListRequest, List<EventCategoriesListDto>>>();
        await Assert.That(port).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventCategoriesListRequest, List<EventCategoriesListDto>>>();
        return await port.QueryAsync(new GetEventCategoriesListRequest(), default);
    }

    private static EventCategories Assignment(Guid eventId, Guid categoryId, Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        EventId = eventId,
        CategoryId = categoryId,
        TenantId = tenantId,
        Event = null!,
        Category = null!,
        Tenant = null!
    };

    private static async Task<SeedData> SeedAsync(NativeEventCategoriesFactory factory)
    {
        using var scope = Scope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).WithSlug("event-categories").Build();
        var foreignTenant = new TenantBuilder().WithSlug("foreign-event-categories").Build();
        var owner = new UserBuilder().WithEmail($"category-owner-{Guid.CreateVersion7():N}@example.test").Build();
        var member = new UserBuilder().WithEmail($"category-member-{Guid.CreateVersion7():N}@example.test").Build();
        var otherOwner = new UserBuilder().WithEmail($"category-other-{Guid.CreateVersion7():N}@example.test").Build();
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
        var category = new Category { Id = Guid.CreateVersion7(), FullName = "Lecture", MasterCode = "LECTURE", TenantId = tenant.Id, Tenant = tenant };
        var secondCategory = new Category { Id = Guid.CreateVersion7(), FullName = "Workshop", MasterCode = "WORKSHOP", TenantId = tenant.Id, Tenant = tenant };
        var foreignCategory = new Category { Id = Guid.CreateVersion7(), FullName = "Foreign", MasterCode = "FOREIGN", TenantId = foreignTenant.Id, Tenant = foreignTenant };
        db.AddRange(tenant, foreignTenant, owner, member, otherOwner, ownerActor, otherActor,
            local, other, foreign, category, secondCategory, foreignCategory);
        var otherAssignment = Assignment(other.Id, category.Id, tenant.Id);
        var foreignAssignment = Assignment(foreign.Id, foreignCategory.Id, foreignTenant.Id);
        db.EventCategories.AddRange(otherAssignment, foreignAssignment);
        db.EventRoleAssignments.Add(EventRoleAssignment.Create(tenant.Id, local.Id, owner.Id,
            (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, owner.Id));
        await db.SaveChangesAsync();
        return new(owner.Id, member.Id, otherOwner.Id, local.Id, other.Id, foreign.Id, foreignTenant.Id,
            category.Id, secondCategory.Id, foreignCategory.Id, otherAssignment.Id, foreignAssignment.Id);
    }

    private sealed record SeedData(Guid OwnerId, Guid MemberId, Guid OtherOwnerId, Guid EventId, Guid OtherEventId,
        Guid ForeignEventId, Guid ForeignTenantId, Guid CategoryId, Guid SecondCategoryId,
        Guid ForeignCategoryId, Guid OtherAssignmentId, Guid ForeignAssignmentId);
}
