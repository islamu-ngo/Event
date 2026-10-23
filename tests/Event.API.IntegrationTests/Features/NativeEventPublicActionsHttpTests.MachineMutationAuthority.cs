using Event.Api.IntegrationTests.Fixtures;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Handlers.Commands;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Features.Footer.Handlers.Commands;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments("local", ExternalApiKeyScopes.EventsWrite, true)]
    [Arguments("local", ExternalApiKeyScopes.AdminTenant, true)]
    [Arguments("local", ExternalApiKeyScopes.EventsRead, false)]
    [Arguments("cerbos", ExternalApiKeyScopes.EventsWrite, true)]
    [Arguments("cerbos", ExternalApiKeyScopes.AdminTenant, true)]
    [Arguments("cerbos", ExternalApiKeyScopes.EventsRead, false)]
    public async Task TenantMachineSessionUpdateIntersectsSelectedPolicyAndKeyScopes(string provider, string scopes, bool allowed)
    {
        await using var factory = new NativeEventSeriesFactory
        {
            InstanceCerbosClient = provider == "cerbos" ? AllowingMachinePolicyTransport() : null
        };
        factory.AdditionalConfiguration["Authorization:Provider"] = provider;
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var session = await SeedMachineSession(scope, db, data.PublicId);
        await AuthenticateNativeMachine(scope, db, db.TenantContext!.TenantId, ExternalApiKeyOwnerType.Tenant, scopes: scopes);
        var handler = ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider);
        var command = MachineSessionUpdate(session);

        if (allowed)
            await Assert.That((await handler.ExecuteAsync(command, default)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(command, default));

        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).Title)
            .IsEqualTo(allowed ? "Machine update" : session.Title);
    }

    [Test]
    public async Task TenantMachineCanCreateAndDeleteWhileExistingPlacementHasAnotherCarrier()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var original = await SeedMachineSession(scope, db, data.PublicId);
        await AuthenticateNativeMachine(scope, db, db.TenantContext!.TenantId, ExternalApiKeyOwnerType.Tenant);

        var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) with { Slug = "machine-session" } }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var stored = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        await Assert.That(stored.CreatedBy).IsNull();
        await Assert.That(stored.EventLocationId).IsEqualTo(original.EventLocationId);
        var deleted = await ActivatorUtilities.CreateInstance<DeleteEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { Id = created.Id }, default);

        await Assert.That(deleted.IsSuccess).IsTrue();
        await Assert.That(await db.EventSessions.AsNoTracking().AnyAsync(row => row.Id == created.Id)).IsFalse();
        await Assert.That(await db.EventSessions.AsNoTracking().AnyAsync(row => row.Id == original.Id)).IsTrue();
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "placement")]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "published-material")]
    [Arguments(ExternalApiKeyOwnerType.Tenant, "final-carrier")]
    [Arguments(ExternalApiKeyOwnerType.User, "placement")]
    [Arguments(ExternalApiKeyOwnerType.User, "published-material")]
    [Arguments(ExternalApiKeyOwnerType.User, "final-carrier")]
    public async Task ApiKeyDoesNotInventHumanAttribution(ExternalApiKeyOwnerType owner, string operation)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var original = await SeedMachineSession(scope, db, data.PublicId);
        await PrepareSessionParent(db, data.PrivateId);
        if (operation == "published-material")
            await db.EventSessions.Where(row => row.Id == original.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.EventSessionStatusId, (int)EventSessionStatusEnum.Published));
        if (owner == ExternalApiKeyOwnerType.User)
            await GrantFooterAdministrator(db, data.OwnerId, 2);
        await AuthenticateNativeMachine(scope, db, owner == ExternalApiKeyOwnerType.Tenant ? db.TenantContext!.TenantId : data.OwnerId, owner);

        if (operation == "placement")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PrivateId) }, default));
        else if (operation == "final-carrier")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ActivatorUtilities.CreateInstance<DeleteEventSessionCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new() { Id = original.Id }, default));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new()
                    {
                        EventSessionId = original.Id,
                        ExpectedConcurrencyStamp = original.ConcurrencyStamp,
                        EventSessionDto = new()
                        {
                            Schedule = new()
                            {
                                StartTime = OptionalUpdate<DateTimeOffset?>.Set(SessionStart.AddHours(2)),
                                EndTime = OptionalUpdate<DateTimeOffset?>.Set(SessionStart.AddHours(3)),
                                EndTimeType = OptionalUpdate<SessionEndTimeType>.Set(SessionEndTimeType.Fixed)
                            }
                        }
                    }, default));

        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == original.Id)).StartTime)
            .IsEqualTo(original.StartTime);
        await Assert.That(await db.EventSessions.AsNoTracking().AnyAsync(row => row.EventId == data.PrivateId)).IsFalse();
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("scope")]
    [Arguments("owner")]
    [Arguments("tenant")]
    [Arguments("expired")]
    public async Task UserKeyCannotEvadeCurrentKeyAuthorityThroughCanonicalUserIdentity(string drift)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var session = await SeedMachineSession(scope, db, data.PublicId);
        await GrantFooterAdministrator(db, data.OwnerId, 2);
        var key = await AuthenticateNativeMachine(scope, db, data.OwnerId, ExternalApiKeyOwnerType.User);
        var handler = ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider);
        var baseline = await handler.ExecuteAsync(MachineSessionUpdate(session) with
        {
            EventSessionDto = new() { Title = new() { Value = OptionalUpdate<string?>.Set("Before key drift") } }
        }, default);
        await Assert.That(baseline.IsSuccess).IsTrue();
        session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        db.ChangeTracker.Clear();
        var query = db.ExternalApiKeys.Where(row => row.Id == key.Id);
        switch (drift)
        {
            case "revoked":
                await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked));
                break;
            case "scope":
                await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Scopes, ExternalApiKeyScopes.EventsRead));
                break;
            case "owner":
                await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OwnerId, data.OutsiderId));
                break;
            case "tenant":
                await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, (Guid?)data.ForeignTenantId));
                break;
            case "expired":
                await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ExpiresAt, (DateTime?)DateTime.UtcNow.AddDays(-1)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(drift));
        }

        await Assert.That(scope.ServiceProvider.GetRequiredService<IAdminContext>().UserId).IsNull();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IPersistedApiKeyProofAccessor>().CurrentProof?.OwnerId).IsEqualTo(data.OwnerId);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(MachineSessionUpdate(session), default));

        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).Title).IsEqualTo(session.Title);
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User, false)]
    [Arguments(ExternalApiKeyOwnerType.User, true)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, false)]
    [Arguments(ExternalApiKeyOwnerType.Tenant, true)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, false)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin, true)]
    public async Task PersistedMachineFooterPatchRetainsOnlySelectedOwnerCells(ExternalApiKeyOwnerType owner, bool linkPatch)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (owner is ExternalApiKeyOwnerType.User or ExternalApiKeyOwnerType.InstanceAdmin)
            await GrantTemplateAdministrator(db, data.OwnerId);
        Guid tenantId = db.TenantContext!.TenantId;
        var group = await SeedFooterGroup(db, tenantId);
        Guid linkId = await db.TenantFooterLinks.Where(row => row.FooterLinkGroupId == group.Id).Select(row => row.Id).SingleAsync();
        await AuthenticateNativeMachine(scope, db, owner == ExternalApiKeyOwnerType.Tenant ? tenantId : data.OwnerId,
            owner, scopes: owner == ExternalApiKeyOwnerType.InstanceAdmin ? ExternalApiKeyScopes.AdminInstance : ExternalApiKeyScopes.AdminTenant);

        var result = linkPatch
            ? await scope.ServiceProvider.GetRequiredService<UpdateFooterLinkCommandHandler>()
                .ExecuteAsync(new()
                {
                    TenantId = data.ForeignTenantId,
                    LinkId = linkId,
                    Update = new() { Label = new() { Value = "Machine link" } }
                }, default)
            : await scope.ServiceProvider.GetRequiredService<UpdateFooterLinkGroupCommandHandler>()
                .ExecuteAsync(new()
                {
                    TenantId = data.ForeignTenantId,
                    GroupId = group.Id,
                    Update = new() { Title = new() { Value = "Machine group" } }
                }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        if (linkPatch)
        {
            var stored = await db.TenantFooterLinks.AsNoTracking().SingleAsync(row => row.Id == linkId);
            await Assert.That(stored.Label).IsEqualTo("Machine link");
            await Assert.That(stored.UpdatedBy).IsNull();
        }
        else
        {
            var stored = await db.TenantFooterLinkGroups.AsNoTracking().SingleAsync(row => row.Id == group.Id);
            await Assert.That(stored.Title).IsEqualTo("Machine group");
            await Assert.That(stored.UpdatedBy).IsNull();
        }
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    public async Task SettingsPatchRetainsItsActualHumanIdentityBoundary(ExternalApiKeyOwnerType owner)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OwnerId);
        Guid tenantId = db.TenantContext!.TenantId;
        await AuthenticateNativeMachine(scope, db, owner == ExternalApiKeyOwnerType.Tenant ? tenantId : data.OwnerId,
            owner, scopes: ExternalApiKeyScopes.AdminInstance);

        var result = await scope.ServiceProvider.GetRequiredService<PatchTenantFooterSettingsCommandHandler>()
            .ExecuteAsync(MachineFooterPatch(tenantId), default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IAdminContext>().UserId).IsNull();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MachineFooterPatchCannotTargetForeignOrInstanceDefaultGroups(bool linkPatch, bool instanceDefault)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid tenantId = db.TenantContext!.TenantId;
        var group = await SeedFooterGroup(db, tenantId);
        Guid linkId = await db.TenantFooterLinks.Where(row => row.FooterLinkGroupId == group.Id).Select(row => row.Id).SingleAsync();
        await AuthenticateNativeMachine(scope, db, tenantId, ExternalApiKeyOwnerType.Tenant, scopes: ExternalApiKeyScopes.AdminTenant);
        Guid? destination = instanceDefault ? null : data.ForeignTenantId;
        await db.TenantFooterLinkGroups.Where(row => row.Id == group.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, destination));

        if (linkPatch)
            await Assert.ThrowsAsync<NotFoundException>(() =>
                scope.ServiceProvider.GetRequiredService<UpdateFooterLinkCommandHandler>().ExecuteAsync(new()
                {
                    LinkId = linkId,
                    Update = new() { Label = new() { Value = "Forbidden" } }
                }, default));
        else
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                scope.ServiceProvider.GetRequiredService<UpdateFooterLinkGroupCommandHandler>().ExecuteAsync(new()
                {
                    GroupId = group.Id,
                    Update = new() { Title = new() { Value = "Forbidden" } }
                }, default));

        await Assert.That((await db.TenantFooterLinks.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == linkId)).Label)
            .IsEqualTo("Owned link");
    }

    [Test]
    public async Task LocalInstanceKeyDoesNotAcquireSessionPermissionFromNewFooterCapability()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var session = await SeedMachineSession(scope, db, data.PublicId);
        await GrantTemplateAdministrator(db, data.OwnerId);
        await AuthenticateNativeMachine(scope, db, data.OwnerId, ExternalApiKeyOwnerType.InstanceAdmin,
            scopes: ExternalApiKeyScopes.AdminInstance);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(MachineSessionUpdate(session), default));

        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).Title).IsEqualTo(session.Title);
    }

    private static async Task<EventSession> SeedMachineSession(IServiceScope scope, ExploreDbContext db, Guid eventId)
    {
        await PrepareSessionParent(db, eventId);
        var result = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(eventId) }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == result.Id);
        db.ChangeTracker.Clear();
        return session;
    }

    private static UpdateEventSessionCommand MachineSessionUpdate(EventSession session) => new()
    {
        EventSessionId = session.Id,
        ExpectedConcurrencyStamp = session.ConcurrencyStamp,
        EventSessionDto = new() { Title = new() { Value = OptionalUpdate<string?>.Set("Machine update") } }
    };

    private static PatchTenantFooterSettingsCommand MachineFooterPatch(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Patch = new() { General = new() { Enabled = OptionalUpdate<bool>.Set(false) } }
    };

    private static ICerbosClient AllowingMachinePolicyTransport()
    {
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = call.Arg<CheckResourcesRequest>()!.ToCheckResourcesRequest();
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var resource in request.Resources)
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                };
                foreach (string action in resource.Actions)
                    result.Actions.Add(action, Cerbos.Api.V1.Effect.Effect.Allow);
                response.Results.Add(result);
            }
            return Task.FromResult(new CheckResourcesResponse(response));
        });
        return client;
    }
}
