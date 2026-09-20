using System.Security.Claims;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventPublicAction;
using Explore.Application.Features.EventPublicActions.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    private static IServiceScope Scope(NativeEventSeriesFactory factory, Guid? userId = null, Guid? tenantId = null)
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

    private static HttpClient Client(NativeEventSeriesFactory factory, Guid? userId = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId is { } id)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(id));
        return client;
    }

    private static string Route(Guid eventId, Guid? actionId = null) =>
        $"/api/events/{eventId}/public-actions" + (actionId is { } id ? $"/{id}" : "");

    private static ManageEventPublicActionDto Input(Guid stamp = default) => new()
    {
        KindId = (int)EventPublicActionKindEnum.OriginalSource,
        Url = "https://example.test/updated",
        Label = "  Source  ",
        SortOrder = 12,
        ExpectedConcurrencyStamp = stamp
    };

    private static async Task<EventPublicActionDto?> Detail(NativeEventSeriesFactory factory, Guid eventId, Guid actionId)
    {
        using var scope = Scope(factory);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventPublicActionRequest, EventPublicActionDto?>>()
            .QueryAsync(new(eventId, actionId), default);
    }

    // Pending/hidden/deleted actions deliberately have no management query in this cohort.
    // The entity-returning repository is the retained persistence seam for their write receipts.
    private static async Task<ActionState?> State(NativeEventSeriesFactory factory, Guid id, Guid? tenantId = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId ?? PlatformDefaults.DefaultTenantId);
        var entity = await scope.ServiceProvider.GetRequiredService<IEventPublicActionRepository>()
            .GetDetailsAsync(id, false, default);
        return entity is null ? null : new(entity.EventId, entity.TenantId, entity.Url, entity.Label,
            entity.EventPublicActionKindId, entity.HealthStateId, entity.SortOrder, entity.IsPrimary, entity.ConcurrencyStamp);
    }

    private static async Task<Guid[]> ActionIds(NativeEventSeriesFactory factory, Guid eventId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var actions = await scope.ServiceProvider.GetRequiredService<IEventPublicActionRepository>()
            .ListByEventAsync(eventId, false, default);
        return actions.Select(action => action.Id).ToArray();
    }

    private static async Task<SeedData> Seed(NativeEventSeriesFactory factory)
    {
        using var scope = Scope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var outsider = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var suspended = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        (await db.Actors.FindAsync(suspended.ActorId))!.IsSuspended = true;

        Explore.Domain.Event Parent(TenantScenarioSeed.TenantScenarioResult person,
            EventStatusEnum status = EventStatusEnum.Published, VisibilityTypeEnum visibility = VisibilityTypeEnum.Public)
        {
            var parent = new EventBuilder().WithActorId(person.ActorId).WithTenantId(person.TenantId)
                .WithStatus(status).WithVisibility(visibility).Build();
            db.Events.Add(parent);
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(person.TenantId, parent.Id, person.UserId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, person.UserId));
            return parent;
        }

        EventPublicAction Action(Explore.Domain.Event parent, EventPublicActionKindEnum kind = EventPublicActionKindEnum.OriginalSource,
            EventPublicActionHealthStateEnum health = EventPublicActionHealthStateEnum.Active, int order = 0)
        {
            var action = new EventPublicAction
            {
                Id = Guid.CreateVersion7(),
                EventId = parent.Id,
                TenantId = parent.TenantId,
                EventPublicActionKindId = (int)kind,
                HealthStateId = (int)health,
                SortOrder = order,
                Label = "Reviewed source",
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            action.SetDestination(ExternalActionUrl.Create("https://example.test/original?ref=public"));
            db.EventPublicActions.Add(action);
            return action;
        }

        var publicEvent = Parent(owner);
        var active = Action(publicEvent, order: 2);
        var earlier = Action(publicEvent, EventPublicActionKindEnum.Livestream, order: 1);
        var hiddenActions = Enum.GetValues<EventPublicActionHealthStateEnum>()
            .Where(health => health != EventPublicActionHealthStateEnum.Active)
            .Select(health => Action(publicEvent, health: health)).ToList();
        hiddenActions.Add(Action(publicEvent, EventPublicActionKindEnum.ExternalRegistration));
        hiddenActions.Add(Action(publicEvent, EventPublicActionKindEnum.OptionalQuestionnaire));
        var deletedAction = Action(publicEvent);
        deletedAction.IsDeleted = true;
        hiddenActions.Add(deletedAction);
        var privateEvent = Parent(owner, visibility: VisibilityTypeEnum.Private);
        var privateAction = Action(privateEvent);
        var draft = Parent(owner, EventStatusEnum.Draft);
        var draftAction = Action(draft);
        var deletedEvent = Parent(owner);
        deletedEvent.IsDeleted = true;
        var missingConfig = Parent(owner);
        missingConfig.ParticipationConfiguration = null;
        var hiddenParents = new[] { privateAction, draftAction, Action(deletedEvent), Action(Parent(suspended)), Action(missingConfig) };
        var otherEvent = Parent(outsider);
        var otherAction = Action(otherEvent);
        var external = Parent(owner);
        external.ParticipationConfiguration = EventParticipationConfiguration.Create(external.Id, owner.TenantId,
            (int)ParticipationHandlingModeEnum.ExternalManaged, (int)AdvanceRegistrationObligationEnum.Optional,
            null, null, DateTime.UnixEpoch);
        var registration = Action(external, EventPublicActionKindEnum.ExternalRegistration);
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(foreign.TenantId);
        var foreignEvent = Parent(foreign);
        var foreignAction = Action(foreignEvent);
        await db.SaveChangesAsync();
        return new(owner.UserId, outsider.UserId, publicEvent.Id, active.Id, earlier.Id,
            privateEvent.Id, privateAction.Id, draft.Id, draftAction.Id, otherEvent.Id, otherAction.Id,
            foreign.TenantId, foreignEvent.Id, foreignAction.Id, external.Id, registration.Id,
            hiddenActions.Select(action => action.Id).ToArray(), hiddenParents.Select(action => (action.EventId, action.Id)).ToArray());
    }

    private sealed record ActionState(Guid EventId, Guid TenantId, string Url, string? Label, int KindId,
        int HealthStateId, int SortOrder, bool IsPrimary, Guid Stamp);
    private sealed record SeedData(Guid OwnerId, Guid OutsiderId, Guid PublicId, Guid ActionId, Guid EarlierId,
        Guid PrivateId, Guid PrivateActionId, Guid DraftId, Guid DraftActionId, Guid OtherId, Guid OtherActionId,
        Guid ForeignTenantId, Guid ForeignId, Guid ForeignActionId, Guid ExternalId, Guid RegistrationId,
        Guid[] HiddenActions, (Guid EventId, Guid ActionId)[] HiddenParents);
}
