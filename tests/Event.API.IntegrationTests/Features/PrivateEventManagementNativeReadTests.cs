using Event.Api.IntegrationTests.Authentication;
using Event.Api.IntegrationTests.Builders;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Domain.Federation;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Text.Json;
using System.Security.Claims;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAddOns;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.EventAddOns.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
public sealed class PrivateEventManagementNativeReadTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task TeamReadRetainsOriginalManageTeamSourcesAndCompleteIdentityGraph(int custodyChange)
    {
        Guid tenant = await CreateTenantAsync();
        var clock = new ReadClock();
        DateTimeOffset expires = default;
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IPrivateEventManagementRepository));
            services.Remove(descriptor);
            services.AddScoped<IPrivateEventManagementRepository>(provider => new AfterRead(
                (IPrivateEventManagementRepository)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!),
                () =>
                {
                    if (custodyChange == 1) clock.Now = expires;
                    if (custodyChange == 2)
                        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext();
                }));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForRuntimeReadsAsync(
            postgres, tenant, clock, Decorate);
        await RemoveAdministratorGrantsAsync(request);
        Guid eventId = await SeedEventAsync(tenant, request.Subject.PrincipalId);
        Guid foreignTenant = await CreateTenantAsync();
        Guid foreignEvent = await SeedEventAsync(foreignTenant, request.Subject.PrincipalId);
        Guid memberId;
        Guid activeId;
        Guid inactiveId;
        Guid unownedEvent;
        await using (var setup = postgres.CreateDbContext())
        {
            var member = new UserBuilder().WithFirstName("Original").WithLastName("Team").Build();
            var deleted = new UserBuilder().Build();
            deleted.IsDeleted = true;
            memberId = member.Id;
            var actor = new ActorBuilder().WithUserId(memberId).Build();
            var unowned = new EventBuilder().WithActorId(actor.Id).WithTenantId(tenant)
                .WithVisibility(VisibilityTypeEnum.Private).Build();
            unownedEvent = unowned.Id;
            var active = EventRoleAssignment.Create(tenant, eventId, memberId, (int)RoleEnum.CheckInStaff,
                EventRoleAssignmentStatus.Active, clock.Now.AddDays(-1).UtcDateTime, null, request.Subject.PrincipalId);
            var inactive = EventRoleAssignment.Create(tenant, eventId, memberId, (int)RoleEnum.RegistrationManager,
                EventRoleAssignmentStatus.Pending, clock.Now.AddDays(-1).UtcDateTime, null, request.Subject.PrincipalId);
            activeId = active.Id;
            inactiveId = inactive.Id;
            var manager = EventRoleAssignment.Create(tenant, eventId, request.Subject.PrincipalId,
                (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
                clock.Now.AddDays(-1).UtcDateTime, null, request.Subject.PrincipalId);
            var hidden = EventRoleAssignment.Create(tenant, eventId, deleted.Id, (int)RoleEnum.CheckInStaff,
                EventRoleAssignmentStatus.Active, clock.Now.AddDays(-1).UtcDateTime, null, request.Subject.PrincipalId);
            var permissions = await setup.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.CheckInStaff)
                .Select(row => row.PermissionId).ToListAsync();
            permissions.Add(await setup.Permissions.Where(row => row.MasterCode == PermissionCodes.EventManageTeam)
                .Select(row => row.Id).SingleAsync());
            foreach (int permission in permissions.Distinct())
                if (!await setup.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.EventManager && row.PermissionId == permission))
                    setup.RolePermissions.Add(new RolePermission
                    {
                        RoleId = (int)RoleEnum.EventManager,
                        Role = null!,
                        PermissionId = permission,
                        Permission = null!
                    });
            await setup.Permissions.Where(row => permissions.Contains(row.Id))
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsFiltered, false).SetProperty(row => row.IsActive, true));
            setup.AddRange(member, deleted, actor, unowned, active, inactive, manager, hidden);
            await setup.SaveChangesAsync();
        }
        expires = request.Lifetime.ExpiresAt.Add(request.Lifetime.ClockSkew).AddTicks(1);
        await request.InOriginalRequestAsync(async () =>
        {
            var handler = request.Services.GetRequiredService<IQueryHandler<GetEventTeamListRequest, List<EventTeamMemberDto>>>();
            var query = new GetEventTeamListRequest { TenantId = tenant, EventId = eventId, IncludeInactive = true };
            if (custodyChange != 0)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.QueryAsync(query));
                await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
                return true;
            }
            var all = await handler.QueryAsync(query);
            await Assert.That(all.Count).IsEqualTo(3);
            await Assert.That(all.Select(row => row.RoleId).SequenceEqual(
                new[] { (int)RoleEnum.EventManager, (int)RoleEnum.RegistrationManager, (int)RoleEnum.CheckInStaff }.Order())).IsTrue();
            var active = all.Single(row => row.AssignmentId == activeId);
            await Assert.That(active.UserId).IsEqualTo(memberId);
            await Assert.That(active.UserFullName).IsEqualTo("Original Team");
            await Assert.That(active.UserEmail).IsNotNullOrEmpty();
            await Assert.That(active.RoleName).IsNotNullOrEmpty();
            await Assert.That(active.IsEffective).IsTrue();
            await Assert.That(all.Single(row => row.AssignmentId == inactiveId).IsEffective).IsFalse();
            var effective = await handler.QueryAsync(query with { IncludeInactive = false });
            await Assert.That(effective.Count).IsEqualTo(2);
            await Assert.That(effective.Any(row => row.AssignmentId == activeId)).IsTrue();
            var graph = await request.Services.GetRequiredService<IPrivateEventManagementReader>()
                .ReadTeamAsync(query, CancellationToken.None);
            var entityAssignment = graph.Team.Single(row => row.Id == activeId);
            await Assert.That(entityAssignment.Version).IsEqualTo(1L);
            await Assert.That(entityAssignment.CreatedBy).IsEqualTo((Guid?)request.Subject.PrincipalId);
            await Assert.That(entityAssignment.User.Pii.UserId).IsEqualTo(memberId);
            await Assert.That(entityAssignment.Role.RoleScopeId).IsEqualTo((int)RoleScopeEnum.Event);
            var current = await request.Services.GetRequiredService<
                IQueryHandler<GetCurrentUserEventPermissionsRequest, CurrentUserEventPermissionsDto>>()
                .QueryAsync(new() { TenantId = tenant, EventId = eventId, UserId = request.Subject.PrincipalId });
            await Assert.That(current.IsManager).IsTrue();
            await Assert.That(current.PermissionCodes.Contains(PermissionCodes.EventManageTeam)).IsTrue();
            var presets = await request.Services.GetRequiredService<
                IQueryHandler<GetAssignableEventRolePresetsRequest, List<EventRolePresetDto>>>()
                .QueryAsync(new() { TenantId = tenant, EventId = eventId, AssignerUserId = request.Subject.PrincipalId });
            await Assert.That(presets.Any(row => row.RoleId == (int)RoleEnum.CheckInStaff)).IsTrue();
            var combined = await request.Services.GetRequiredService<
                IQueryHandler<GetManagedEventTeamRequest, ManagedEventTeamDto>>()
                .QueryAsync(new(eventId, true), CancellationToken.None);
            await Assert.That(combined.Members.Count).IsEqualTo(3);
            await Assert.That(combined.Permissions.IsManager).IsTrue();
            await Assert.That(combined.Permissions.PermissionCodes.Contains(PermissionCodes.EventManageTeam)).IsTrue();
            await Assert.That(combined.AssignablePresets.Any(row => row.RoleId == (int)RoleEnum.CheckInStaff)).IsTrue();
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.QueryAsync(query with { EventId = unownedEvent }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.QueryAsync(query with { TenantId = foreignTenant }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.QueryAsync(query with { EventId = foreignEvent }));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IPrivateEventManagementRepository>()
                    .ReadAsync(new(PrivateEventManagementReadKind.Team, eventId), CancellationToken.None));
            await request.Owner.Database.OpenConnectionAsync();
            await using var command = request.Owner.Database.GetDbConnection().CreateCommand();
            var entity = request.Owner.Model.FindEntityType(typeof(EventRoleAssignment))!;
            command.CommandText = $"SELECT id FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" LIMIT 1";
            var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            return true;
        });
    }

    [Test]
    public async Task RegistrationAccountReadsRequireOriginalHumanAndRetainCompleteEntities()
    {
        Guid tenant = await CreateTenantAsync();
        var clock = new ReadClock();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant, clock);
        await RemoveAdministratorGrantsAsync(request);
        var seed = await SeedRegistrationReadAsync(tenant, request.Subject.PrincipalId, false, clock.Now.UtcDateTime);
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IRegistrationPrivateReader>();
            foreach (var selection in seed.Reads(guest: false))
            {
                var graph = await reader.ReadAsync(selection, null, CancellationToken.None);
                await Assert.That(graph).IsNotNull();
                await Assert.That(graph!.Order.Id).IsEqualTo(seed.Order);
                await Assert.That(graph.PrincipalId).IsEqualTo((Guid?)request.Subject.PrincipalId);
                if (selection.Kind == RegistrationPrivateReadKind.AddOns)
                {
                    await Assert.That(graph.Order.AddOnLines.Count).IsEqualTo(1);
                    await Assert.That(graph.AddOnCatalog!.Items.Count).IsEqualTo(1);
                }
                if (selection.Kind == RegistrationPrivateReadKind.Waitlist)
                {
                    await Assert.That(graph.Waitlist!.Line.Id).IsEqualTo(seed.Line);
                    await Assert.That(graph.TenantSaleControl!.IsStopped).IsFalse();
                    await Assert.That(graph.EventSaleControl!.IsStopped).IsTrue();
                }
            }
            var dto = await request.Services.GetRequiredService<
                IQueryHandler<GetRegistrationOrderAddOnsQuery, RegistrationOrderAddOnSummaryDto?>>()
                .QueryAsync(new(seed.Event, seed.Order, null), CancellationToken.None);
            await Assert.That(dto!.Lines.Count).IsEqualTo(1);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IRegistrationPrivateReadRepository>()
                    .ReadAsync(seed.Reads(false)[0], CancellationToken.None));
            await request.Owner.Database.OpenConnectionAsync();
            await using var command = request.Owner.Database.GetDbConnection().CreateCommand();
            var entity = request.Owner.Model.FindEntityType(typeof(RegistrationOrder))!;
            command.CommandText = $"SELECT id FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" LIMIT 1";
            var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
            await Assert.That(denied!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            return true;
        });
        await Assert.That(await ReadAnonymousRegistrationAsync(request, tenant, seed.Reads(false)[0], null,
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, request.Subject.PrincipalId.ToString("D"))], "Bearer")))).IsNull();
    }

    [Test]
    public async Task RegistrationGuestReadsRejectWrongUnknownCrossTenantAndExpiredCapabilities()
    {
        Guid tenant = await CreateTenantAsync();
        Guid otherTenant = await CreateTenantAsync();
        var clock = new ReadClock();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant, clock);
        var seed = await SeedRegistrationReadAsync(tenant, request.Subject.PrincipalId, true, clock.Now.UtcDateTime);
        var wrongCapability = new GuestCapabilityTokenService().Issue();
        string wrong = wrongCapability.RawToken;
        foreach (var selection in seed.Reads(guest: true))
        {
            string capability = selection.Kind == RegistrationPrivateReadKind.Transfer ? seed.TransferCapability.RawToken : seed.OrderCapability.RawToken;
            var graph = await ReadAnonymousRegistrationAsync(request, tenant, selection, capability);
            await Assert.That(graph).IsNotNull();
            await Assert.That(graph!.Order.Id).IsEqualTo(seed.Order);
            await Assert.That(graph.PrincipalId).IsNull();
            await Assert.That(await ReadAnonymousRegistrationAsync(request, tenant, selection, wrong)).IsNull();
            await Assert.That(await ReadAnonymousRegistrationAsync(request, otherTenant, selection, capability)).IsNull();
            await Assert.That(await ReadAnonymousRegistrationAsync(request, tenant,
                selection with { OrderId = Guid.CreateVersion7(), TransferId = Guid.CreateVersion7() }, capability)).IsNull();
        }
        clock.Now = clock.Now.AddHours(2);
        foreach (var selection in seed.Reads(guest: true))
        {
            string capability = selection.Kind == RegistrationPrivateReadKind.Transfer ? seed.TransferCapability.RawToken : seed.OrderCapability.RawToken;
            var graph = await ReadAnonymousRegistrationAsync(request, tenant, selection, capability);
            // These two existing reads intentionally have no order-expiry predicate.
            if (selection.Kind is RegistrationPrivateReadKind.AddOns or RegistrationPrivateReadKind.Waitlist)
                await Assert.That(graph).IsNotNull();
            else
                await Assert.That(graph).IsNull();
        }
        // Protected-challenge replay intentionally survives the order deadline. Only
        // its exact persisted digest/order/event/tenant tuple may recover the result.
        var recovered = await RecoverAsync(tenant, seed.Order, seed.OrderCapability.Hash);
        await Assert.That(recovered).IsNotNull();
        await Assert.That(recovered!.Id).IsEqualTo(seed.Order);
        await Assert.That(recovered.Lines.Count).IsEqualTo(1);
        await Assert.That(await RecoverAsync(tenant, seed.Order, wrongCapability.Hash)).IsNull();
        await Assert.That(await RecoverAsync(otherTenant, seed.Order, seed.OrderCapability.Hash)).IsNull();
        await Assert.That(await RecoverAsync(tenant, Guid.CreateVersion7(), seed.OrderCapability.Hash)).IsNull();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ReadAnonymousRegistrationAsync(
            request, tenant, new(RegistrationPrivateReadKind.GuestRecovery, seed.Event, seed.Order),
            seed.OrderCapability.RawToken));

        async Task<RegistrationOrder?> RecoverAsync(Guid selectedTenant, Guid order, CapabilityTokenHash digest)
        {
            await using var scope = request.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(selectedTenant);
            var accessor = services.GetRequiredService<IHttpContextAccessor>();
            var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity())
            };
            try
            {
                return await services.GetRequiredService<IGuestRegistrationCapabilityRepository>()
                    .GetExactGuestOrderAsync(order, selectedTenant, seed.Event, digest, CancellationToken.None);
            }
            finally { accessor.HttpContext = previous; }
        }
    }

    private static async Task<RegistrationPrivateGraph?> ReadAnonymousRegistrationAsync(
        TenantBrandingNativeAuthenticationFixture owner, Guid tenant, RegistrationPrivateRead read,
        string? capability, ClaimsPrincipal? principal = null)
    {
        await using var scope = owner.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenant);
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = services,
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
        try
        {
            return await services.GetRequiredService<IRegistrationPrivateReader>().ReadAsync(read, capability, CancellationToken.None);
        }
        finally { accessor.HttpContext = previous; }
    }

    [Test]
    public async Task RegistrationReadRejectsOriginalRequestReplacementBeforeDisclosure()
    {
        Guid tenant = await CreateTenantAsync();
        var clock = new ReadClock();
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IRegistrationPrivateReadRepository));
            services.Remove(descriptor);
            services.AddScoped<IRegistrationPrivateReadRepository>(provider => new RegistrationAfterRead(
                (IRegistrationPrivateReadRepository)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!),
                () => provider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext()));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(
            postgres, tenant, clock, Decorate);
        var seed = await SeedRegistrationReadAsync(tenant, request.Subject.PrincipalId, true, clock.Now.UtcDateTime);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ReadAnonymousRegistrationAsync(request, tenant, seed.Reads(true)[0], seed.OrderCapability.RawToken));
    }

    private sealed class RegistrationAfterRead(IRegistrationPrivateReadRepository inner, Action completed)
        : IRegistrationPrivateReadRepository
    {
        public async Task<RegistrationPrivateGraph?> ReadAsync(RegistrationPrivateRead request, CancellationToken token)
        {
            var graph = await inner.ReadAsync(request, token);
            completed();
            return graph;
        }
    }

    private async Task<RegistrationReadSeed> SeedRegistrationReadAsync(Guid tenant, Guid user, bool guest, DateTime now)
    {
        Guid eventId = await SeedEventAsync(tenant, user);
        await using var setup = postgres.CreateDbContext();
        var tokens = new GuestCapabilityTokenService();
        var orderCapability = tokens.Issue();
        var transferCapability = tokens.Issue();
        var catalog = EventTicketCatalogVersion.Create(tenant, eventId, "USD", 1);
        var type = EventTicketType.Create(Guid.CreateVersion7(), tenant, catalog.Id, "Registration read ticket", "USD",
            TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.PerTicketRequired,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(type, null);
        catalog.AddEntitlement(type, TicketTypeEntitlement.CreateForEvent(type.Id, tenant, eventId, 1));
        catalog.Publish();
        var order = RegistrationOrder.Create(tenant, eventId, guest ? null : user, null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(),
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)IdentityAccessModeEnum.CapabilityTokenAllowed, GuestRecoveryPolicyEnum.CapabilityLinkOnly),
            null, guest ? orderCapability.Hash : null, "USD", now, now.AddMinutes(30));
        var line = RegistrationOrderLine.Create(catalog, type, order.Id, 1, null, null);
        order.AddLine(line);
        var participant = RegistrationParticipant.Create(tenant, order.Id, user, ParticipantTypeEnum.Adult, null);
        var assignment = RegistrationTicketAssignment.CreateAssigned(Guid.CreateVersion7(), line.Id, 1, participant, now);
        order.AddParticipant(participant);
        order.AddAssignment(line, assignment, participant);
        var addOns = EventAddOnCatalogVersion.Create(tenant, eventId, "USD", 1);
        var item = EventAddOnCatalogItem.Create(Guid.CreateVersion7(), tenant, addOns.Id, "Included add-on", null,
            Money.Create(0, "USD"), 10, "Collect at event", "No charge");
        addOns.AddItem(item);
        addOns.Publish(now);
        order.PinAddOnCatalog(addOns);
        order.AddAddOnLine(RegistrationOrderAddOnLine.Create(Guid.CreateVersion7(), order, addOns, item, 1));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
        var eligibility = ParticipantAdmissionEligibility.Create(tenant, eventId, assignment, participant, false, false, now);
        eligibility.RecordSubjectCompletion(participant, user, null, now, Guid.CreateVersion7());
        var ticket = AdmissionTicket.Issue(order, line, assignment, participant, catalog, type,
            Guid.CreateVersion7(), $"T-{Guid.CreateVersion7():N}", Guid.CreateVersion7(), 1, 1, tokens.Issue().Hash.Value, now);
        var policy = TicketTransferPolicy.Create(Guid.CreateVersion7(), tenant, type, true, 3, 60, 0, now);
        var transfer = AdmissionTicketTransfer.Offer(Guid.CreateVersion7(), ticket, policy, Guid.CreateVersion7(),
            transferCapability.Hash.Value, now.AddMinutes(30), now);
        setup.AddRange(catalog, addOns, order, eligibility, ticket, policy, transfer,
            PaidCheckoutSaleControl.CreateActive(tenant, null, user, now),
            PaidCheckoutSaleControl.CreateStopped(tenant, eventId, user, "operator_stop", now));
        await setup.SaveChangesAsync();
        return new(eventId, order.Id, line.Id, participant.Id, assignment.Id, ticket.Id, transfer.Id,
            orderCapability, transferCapability);
    }

    private sealed record RegistrationReadSeed(Guid Event, Guid Order, Guid Line, Guid Participant, Guid Assignment,
        Guid Ticket, Guid Transfer, GuestCapabilityTokenIssue OrderCapability, GuestCapabilityTokenIssue TransferCapability)
    {
        public override string ToString() => nameof(RegistrationReadSeed);
        internal RegistrationPrivateRead[] Reads(bool guest) =>
        [
            new(RegistrationPrivateReadKind.AddOns, Event, Order),
            new(RegistrationPrivateReadKind.Waitlist, Event, Order, Line),
            new(RegistrationPrivateReadKind.Readiness, Event, Order, ParticipantId: Participant, AssignmentId: Assignment),
            new(guest ? RegistrationPrivateReadKind.PurchaseOrder : RegistrationPrivateReadKind.AccountOrder, Event, Order),
            new(RegistrationPrivateReadKind.Transfer, Event, TicketId: Ticket, TransferId: Transfer)
        ];
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MyEventsIncludesOrdinaryMembershipWithoutCreatePermissionAndRevokesItOnTheNextRead(bool group)
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await RemoveAdministratorGrantsAsync(request);
        Guid personalEvent = await SeedEventAsync(tenant, request.Subject.PrincipalId);
        Guid memberEvent;
        Guid membership;
        Guid delivery;
        await using (var setup = postgres.CreateDbContext())
        {
            var role = new Role
            {
                Id = await setup.Roles.MaxAsync(row => row.Id) + 1,
                MasterCode = "private.read.member." + Guid.CreateVersion7().ToString("N"),
                FullName = "Membership without permissions",
                RoleScopeId = (int)(group ? RoleScopeEnum.Group : RoleScopeEnum.Organization)
            };
            var actor = new ActorBuilder().WithActorType(group ? ActorTypeEnum.Group : ActorTypeEnum.Organization)
                .WithDisplayName("Ordinary member publisher").Build();
            membership = Guid.CreateVersion7();
            if (group)
            {
                var owner = new Group { Id = Guid.CreateVersion7(), FullName = "Membership group" };
                var placement = new GroupTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    GroupId = owner.Id,
                    Group = owner,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                    ApprovalStatus = null!
                };
                actor.GroupId = owner.Id;
                setup.AddRange(owner, placement, new GroupMember
                {
                    Id = membership,
                    TenantId = tenant,
                    Tenant = null!,
                    GroupTenantId = placement.Id,
                    GroupTenant = placement,
                    UserId = request.Subject.PrincipalId,
                    User = null!,
                    RoleId = role.Id,
                    Role = role
                });
            }
            else
            {
                var owner = new Organization
                {
                    Id = Guid.CreateVersion7(),
                    Pii = new OrganizationPii { FullName = "Membership organization" }
                };
                var placement = new OrganizationTenant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    OrganizationId = owner.Id,
                    Organization = owner,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                    ApprovalStatus = null!
                };
                actor.OrganizationId = owner.Id;
                setup.AddRange(owner, placement, new OrganizationMember
                {
                    Id = membership,
                    TenantId = tenant,
                    Tenant = null!,
                    OrganizationTenantId = placement.Id,
                    OrganizationTenant = placement,
                    UserId = request.Subject.PrincipalId,
                    User = null!,
                    RoleId = role.Id,
                    Role = role
                });
            }
            setup.AddRange(role, actor);
            var target = new EventBuilder().WithActorId(actor.Id).WithTenantId(tenant)
                .WithVisibility(VisibilityTypeEnum.Private).WithStatus(EventStatusEnum.Draft).Build();
            setup.Events.Add(target);
            memberEvent = target.Id;
            delivery = Guid.CreateVersion7();
            setup.PdsSyncOutbox.Add(new PdsSyncOutbox
            {
                Id = delivery,
                TenantId = tenant,
                UserId = request.Subject.PrincipalId,
                Did = "did:plc:private-management",
                Collection = "community.lexicon.calendar.event",
                RecordKey = memberEvent.ToString("N"),
                Operation = PdsSyncOperation.Create,
                Payload = "{\"title\":\"Private draft\"}",
                PayloadHash = new string('a', 64),
                IdempotencyKey = delivery.ToString("N"),
                PdsHost = "https://pds.example.test",
                SourceEntityType = "Event",
                SourceEntityId = memberEvent,
                SourceVersion = target.ConcurrencyStamp,
                Status = PdsSyncStatus.Failed,
                LastError = "delivery_denied",
                MaxRetries = 3,
                CreatedAt = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
            await Assert.That(await setup.RolePermissions.AnyAsync(row => row.RoleId == role.Id)).IsFalse();
        }
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IPrivateEventManagementReader>();
            var first = await reader.ReadAsync(new(PrivateEventManagementReadKind.MyEvents,
                ExpectedUserId: request.Subject.PrincipalId), CancellationToken.None);
            await Assert.That(first.Events.Select(row => row.Id).Order().ToArray())
                .IsEquivalentTo(new[] { personalEvent, memberEvent });
            await Assert.That(first.TotalCount).IsEqualTo(2);
            await Assert.That(first.Events.All(row => row.VisibilityTypeId == (int)VisibilityTypeEnum.Private
                && row.EventStatusId == (int)EventStatusEnum.Draft)).IsTrue();
            await Assert.That(first.DeliveryStates.Single().Id).IsEqualTo(delivery);
            await Assert.That(first.DeliveryStates.Single().LastError).IsEqualTo("delivery_denied");
            using var payload = JsonDocument.Parse(first.DeliveryStates.Single().Payload!);
            await Assert.That(payload.RootElement.GetProperty("title").GetString()).IsEqualTo("Private draft");
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                reader.ReadAsync(new(PrivateEventManagementReadKind.Details, memberEvent), CancellationToken.None));
            var memberTarget = first.Events.Single(row => row.Id == memberEvent);
            var memberFacts = new EventAuthorizationFacts(tenant, memberEvent, memberTarget.ActorId,
                memberTarget.Actor.UserId, memberTarget.Actor.OrganizationId, memberTarget.Actor.GroupId,
                memberTarget.OrganizerActorId, memberTarget.OrganizerActor?.UserId,
                memberTarget.OrganizerActor?.OrganizationId, memberTarget.OrganizerActor?.GroupId,
                memberTarget.EventProvenanceType?.MasterCode, memberTarget.SubmittedByUserId);
            var decisions = await request.Services.GetRequiredService<IAuthorizationProvider>().AuthorizeBatchAsync(
                new[] { AuthorizationActions.Events.ViewManagement, AuthorizationActions.Update, AuthorizationActions.Delete }
                    .Select(action => new AuthorizationRequest(ResourceKinds.Event, memberEvent.ToString("D"),
                        action, Facts: memberFacts)).ToArray());
            await Assert.That(decisions.All(decision => !decision.IsAllowed)).IsTrue();
            await using (var writer = postgres.CreateDbContext())
            {
                if (group)
                    await writer.GroupMembers.Where(row => row.Id == membership)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                else
                    await writer.OrganizationMembers.Where(row => row.Id == membership)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            }
            var second = await reader.ReadAsync(new(PrivateEventManagementReadKind.MyEvents,
                ExpectedUserId: request.Subject.PrincipalId), CancellationToken.None);
            await Assert.That(second.Events.Single().Id).IsEqualTo(personalEvent);
            await Assert.That(second.TotalCount).IsEqualTo(1);
            await Assert.That(second.DeliveryStates.Count).IsEqualTo(0);
            return true;
        });
    }

    [Test]
    public async Task ManagementRetainsPrivateDraftGraphAndDisabledActionsWhileRawRuntimeReadIsDenied()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await RemoveAdministratorGrantsAsync(request);
        Guid eventId = await SeedEventAsync(tenant, request.Subject.PrincipalId);
        Guid actionId = Guid.CreateVersion7();
        Guid groupId = Guid.CreateVersion7();
        await using (var setup = postgres.CreateDbContext())
        {
            var action = new EventPublicAction
            {
                Id = actionId,
                TenantId = tenant,
                EventId = eventId,
                EventPublicActionKindId = (int)EventPublicActionKindEnum.OriginalSource,
                HealthStateId = (int)EventPublicActionHealthStateEnum.Disabled
            };
            action.SetDestination(ExternalActionUrl.Create("https://event.example.test/private"));
            var group = new EventSessionGroup
            {
                Id = groupId,
                EventId = eventId,
                Event = null!,
                TenantId = tenant,
                Tenant = null!,
                Name = "Unpublished track",
                IsPublished = false
            };
            var session = new EventSession
            {
                Id = Guid.CreateVersion7(),
                EventId = eventId,
                Event = null!,
                TenantId = tenant,
                Tenant = null!,
                Title = "Private session",
                EventSessionKindId = (int)EventSessionKindEnum.Talk,
                RegistrationModeId = 1
            };
            var start = new DateTimeOffset(2035, 1, 10, 10, 0, 0, TimeSpan.Zero);
            session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
            setup.AddRange(action, group, session);
            await setup.SaveChangesAsync();
        }
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IPrivateEventManagementReader>();
            var detail = await reader.ReadAsync(new(PrivateEventManagementReadKind.Details, eventId), CancellationToken.None);
            var target = detail.Events.Single();
            await Assert.That(target.VisibilityTypeId).IsEqualTo((int)VisibilityTypeEnum.Private);
            await Assert.That(target.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
            await Assert.That(target.ParticipationConfiguration!.ParticipationHandlingMode).IsNotNull();
            await Assert.That(target.Actor.Pii.DisplayName).IsNotNull();
            await Assert.That(target.PublicActions.Single().Id).IsEqualTo(actionId);
            await Assert.That(target.PublicActions.Single().HealthStateId).IsEqualTo((int)EventPublicActionHealthStateEnum.Disabled);
            var program = await reader.ReadAsync(new(PrivateEventManagementReadKind.Program, eventId), CancellationToken.None);
            await Assert.That(program.SessionGroups.Single().Id).IsEqualTo(groupId);
            await Assert.That(program.SessionGroups.Single().IsPublished).IsFalse();
            await Assert.That(program.Sessions.Single().Event!.Id).IsEqualTo(eventId);
            await Assert.That(program.Sessions.Single().EventSessionKind).IsNotNull();
            Guid sessionId = program.Sessions.Single().Id;
            var history = await reader.ReadAsync(new(PrivateEventManagementReadKind.SessionSyncContext,
                EventId: eventId, SessionId: sessionId), CancellationToken.None);
            await Assert.That(history.Sessions.Single().EventId).IsEqualTo(eventId);
            var unknown = await reader.ReadAsync(new(PrivateEventManagementReadKind.SessionSyncContext,
                EventId: eventId, SessionId: Guid.CreateVersion7()), CancellationToken.None);
            await Assert.That(unknown.Sessions.Count).IsEqualTo(0);
            await Assert.That(unknown.History.Count).IsEqualTo(0);
            Guid otherEventId = await SeedEventAsync(tenant, request.Subject.PrincipalId);
            var wrongParent = await reader.ReadAsync(new(PrivateEventManagementReadKind.SessionSyncContext,
                EventId: otherEventId, SessionId: sessionId), CancellationToken.None);
            await Assert.That(wrongParent.Sessions.Count).IsEqualTo(0);
            await Assert.That(wrongParent.History.Count).IsEqualTo(0);
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            await request.Owner.Database.OpenConnectionAsync();
            await using var command = request.Owner.Database.GetDbConnection().CreateCommand();
            var entity = request.Owner.Model.FindEntityType(typeof(Explore.Domain.Event))!;
            command.CommandText = $"SELECT title FROM \"{entity.GetSchema()}\".\"{entity.GetTableName()}\" LIMIT 1";
            var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
            await Assert.That(denied?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            return true;
        });
    }

    [Test]
    public async Task PreparedHumanCannotSubstituteAnotherPrincipalOrInvokeAnUnadmittedRepositoryRead()
    {
        Guid tenant = await CreateTenantAsync();
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant);
        await RemoveAdministratorGrantsAsync(request);
        Guid eventId = await SeedEventAsync(tenant, request.Subject.PrincipalId);
        Guid foreignTenant = await CreateTenantAsync();
        Guid foreignEvent = await SeedEventAsync(foreignTenant, request.Subject.PrincipalId);
        Guid otherEvent;
        await using (var setup = postgres.CreateDbContext())
        {
            var user = new UserBuilder().Build();
            var actor = new ActorBuilder().WithUserId(user.Id).WithDisplayName("Another event owner").Build();
            setup.AddRange(user, actor);
            var target = new EventBuilder().WithActorId(actor.Id).WithTenantId(tenant)
                .WithVisibility(VisibilityTypeEnum.Private).Build();
            setup.Events.Add(target);
            await setup.SaveChangesAsync();
            otherEvent = target.Id;
        }
        await request.InOriginalRequestAsync(async () =>
        {
            var reader = request.Services.GetRequiredService<IPrivateEventManagementReader>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ReadAsync(
                new(PrivateEventManagementReadKind.MyEvents, ExpectedUserId: Guid.CreateVersion7()), CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IPrivateEventManagementRepository>()
                    .ReadAsync(new(PrivateEventManagementReadKind.Details, eventId), CancellationToken.None));
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                reader.ReadAsync(new(PrivateEventManagementReadKind.Details, otherEvent), CancellationToken.None));
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                reader.ReadAsync(new(PrivateEventManagementReadKind.Details, foreignEvent), CancellationToken.None));
            var registrations = request.Services.GetRequiredService<
                IQueryHandler<GetEventRegistrationOrdersQuery, IReadOnlyList<RegistrationOrderDto>>>();
            // Publisher ownership alone does not confer registration management.
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                registrations.QueryAsync(new(eventId), CancellationToken.None));
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                registrations.QueryAsync(new(otherEvent), CancellationToken.None));
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                registrations.QueryAsync(new(foreignEvent), CancellationToken.None));
            var owned = await reader.ReadAsync(new(PrivateEventManagementReadKind.MyEvents,
                ExpectedUserId: request.Subject.PrincipalId), CancellationToken.None);
            await Assert.That(owned.Events.Single().Id).IsEqualTo(eventId);
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    public async Task CredentialExpiryAfterNativeGraphReadPreventsResultRelease()
    {
        Guid tenant = await CreateTenantAsync();
        var clock = new ReadClock();
        DateTimeOffset expires = default;
        void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IPrivateEventManagementRepository));
            services.Remove(descriptor);
            services.AddScoped<IPrivateEventManagementRepository>(provider => new AfterRead(
                (IPrivateEventManagementRepository)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!),
                () => clock.Now = expires));
        }
        await using var request = await TenantBrandingNativeAuthenticationFixture.CreateForActorReadsAsync(postgres, tenant, clock, Decorate);
        await RemoveAdministratorGrantsAsync(request);
        Guid eventId = await SeedEventAsync(tenant, request.Subject.PrincipalId);
        expires = request.Lifetime.ExpiresAt.Add(request.Lifetime.ClockSkew).AddTicks(1);
        await request.InOriginalRequestAsync(async () =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IPrivateEventManagementReader>()
                    .ReadAsync(new(PrivateEventManagementReadKind.Details, eventId), CancellationToken.None));
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    private async Task<Guid> CreateTenantAsync()
    {
        await using var setup = postgres.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Private management native reads",
            Slug = Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        setup.Tenants.Add(tenant);
        await setup.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task RemoveAdministratorGrantsAsync(TenantBrandingNativeAuthenticationFixture request)
    {
        await using var setup = postgres.CreateDbContext();
        await setup.PlatformUserRoles.Where(row => row.Id == request.PlatformGrantId).ExecuteDeleteAsync();
        await setup.TenantUserRoleGrants.Where(row => row.Id == request.TenantGrantId).ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedEventAsync(Guid tenant, Guid user)
    {
        await using var setup = postgres.CreateDbContext();
        Guid actor = await setup.Actors.Where(row => row.UserId == user).Select(row => row.Id).SingleAsync();
        var target = new EventBuilder().WithActorId(actor).WithTenantId(tenant)
            .WithVisibility(VisibilityTypeEnum.Private).WithStatus(EventStatusEnum.Draft).Build();
        setup.Events.Add(target);
        await setup.SaveChangesAsync();
        return target.Id;
    }

    private sealed class ReadClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AfterRead(IPrivateEventManagementRepository inner, Action completed) : IPrivateEventManagementRepository
    {
        public async Task<PrivateEventManagementGraph> ReadAsync(PrivateEventManagementRead request, CancellationToken cancellationToken)
        {
            var result = await inner.ReadAsync(request, cancellationToken);
            completed();
            return result;
        }
    }
}
