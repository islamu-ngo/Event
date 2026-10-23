using System.Data.Common;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessions.Handlers.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(SessionEndTimeType.Fixed)]
    [Arguments(SessionEndTimeType.OpenEnded)]
    [Arguments(SessionEndTimeType.RelativeToPrayer)]
    public async Task NativeSessionCreationPreservesDraftAuthorityAndDomainSchedule(SessionEndTimeType kind)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        var input = SessionInput(data.PublicId) with
        {
            EndTimeType = kind,
            EndTime = kind == SessionEndTimeType.Fixed ? SessionStart.AddHours(1) : null
        };

        var result = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = input }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        var stored = await db.EventSessions.AsNoTracking().SingleAsync(session => session.Id == result.Id);
        await Assert.That(stored.TenantId).IsEqualTo(db.TenantContext!.TenantId);
        await Assert.That(stored.EventId).IsEqualTo(data.PublicId);
        await Assert.That(stored.EventSessionStatusId).IsEqualTo((int)EventSessionStatusEnum.Draft);
        await Assert.That(stored.EndTimeType).IsEqualTo(kind);
        await Assert.That(stored.StartTime).IsEqualTo(SessionStart);
        await Assert.That(stored.EndTime).IsEqualTo(input.EndTime);
        await Assert.That(stored.LocalStartDate).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(stored.LocalStartTime).IsEqualTo(new TimeOnly(10, 0));
        await Assert.That(stored.CurrentAudienceAttendees).IsEqualTo(0);
        await Assert.That(stored.MaxAudienceAttendees).IsEqualTo(40);
        await Assert.That(stored.SortOrder).IsEqualTo(5);
        await Assert.That(stored.Description).IsEqualTo("Description");
        await Assert.That(stored.Slug).IsEqualTo("new-session");
        await Assert.That(stored.SourceTemplateId).IsNull();
        await Assert.That(stored.CreatedBy).IsEqualTo(data.OwnerId);
        await Assert.That(stored.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        await Assert.That((await db.EventLocations.AsNoTracking().SingleAsync(row => row.Id == stored.EventLocationId))
            .IsToBeAnnounced).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionOwnerOrInstanceAdministratorCanUpdateAndDelete(bool instanceAdministrator)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, instanceAdministrator ? data.OutsiderId : data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        if (instanceAdministrator)
            await GrantTemplateAdministrator(db, data.OutsiderId);
        var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        db.ChangeTracker.Clear();

        var updated = await ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new()
            {
                EventSessionId = session.Id,
                ExpectedConcurrencyStamp = session.ConcurrencyStamp,
                EventSessionDto = new() { Title = new() { Value = OptionalUpdate<string?>.Set("Updated session") } }
            }, default);
        await Assert.That(updated.IsSuccess).IsTrue();
        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).Title)
            .IsEqualTo("Updated session");
        db.ChangeTracker.Clear();
        var deleted = await ActivatorUtilities.CreateInstance<DeleteEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { Id = session.Id }, default);
        await Assert.That(deleted.IsSuccess).IsTrue();
        await Assert.That(await db.EventSessions.AnyAsync(row => row.Id == session.Id)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task DirectSessionMutationsRejectSameTenantNonowners(int operation)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        EventSession session;
        using (var owner = Scope(factory, data.OwnerId))
        {
            var db = owner.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await PrepareSessionParent(db, data.PublicId);
            var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(owner.ServiceProvider)
                .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
            await Assert.That(created.IsSuccess).IsTrue();
            session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        }
        using var outsider = Scope(factory, data.OutsiderId);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            RunSessionMutation(outsider.ServiceProvider, operation, session, default));

        var current = await outsider.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        await Assert.That(current.Title).IsEqualTo(session.Title);
        await Assert.That(current.ConcurrencyStamp).IsEqualTo(session.ConcurrencyStamp);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionAndIslamicAspectShareOneCommitOwner(bool retry)
    {
        var boundary = new SessionCommitBoundary(retry);
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        boundary.Arm(db);
        var handler = ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider);
        var input = SessionInput(data.PublicId) with { IslamicAspect = new() { RequiresWudu = true } };

        if (retry)
        {
            var result = await handler.ExecuteAsync(new() { EventSessionDto = input }, default);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Id).IsEqualTo(boundary.FirstSessionId);
            await Assert.That(await db.Set<EventSessionIslamicAspect>().CountAsync(row => row.EventSessionId == result.Id)).IsEqualTo(1);
        }
        else
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                handler.ExecuteAsync(new() { EventSessionDto = input }, default));
            await Assert.That(await db.EventSessions.AnyAsync(row => row.Id == boundary.FirstSessionId)).IsFalse();
            await Assert.That(await db.Set<EventSessionIslamicAspect>().AnyAsync(row => row.EventSessionId == boundary.FirstSessionId)).IsFalse();
        }
        await Assert.That(boundary.FirstSessionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(boundary.AspectsAtFirstCommit).IsEqualTo(1);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SessionSelectedCerbosOutageDeniesEveryDirectMutation(int operation)
    {
        var transport = new UnavailableInstanceKeyPolicyTransport();
        var client = CerbosClientBuilder.ForTarget("http://session-policy.test").WithPlaintext()
            .WithGrpcChannelOptions(new GrpcChannelOptions { HttpHandler = transport, DisposeHttpClient = true }).Build();
        await using var factory = new NativeEventSeriesFactory { InstanceCerbosClient = client };
        factory.AdditionalConfiguration["Authorization:Provider"] = "cerbos";
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        Guid id = await PrepareDefinitionParent(db, data.PublicId, 2);
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == id);
        db.ChangeTracker.Clear();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
            RunSessionMutation(scope.ServiceProvider, operation, session, deadline.Token));

        await Assert.That(transport.Requests).IsGreaterThan(0);
        var current = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == id);
        await Assert.That(current.Title).IsEqualTo(session.Title);
        await Assert.That(current.ConcurrencyStamp).IsEqualTo(session.ConcurrencyStamp);
    }

    [Test]
    [Arguments("create")]
    [Arguments("update")]
    [Arguments("delete")]
    public async Task SessionNativeAuthorityRejectsCommittedAdministratorRevocation(string action)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid grantId = await GrantTemplateAdministrator(db, data.OutsiderId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventSessionRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var before = await unit.ExecuteSerializableAsync(token => repository.GetAuthorizedParentForMutationAsync(
            db.TenantContext!.TenantId, data.OutsiderId, data.PublicId, action, token));
        await Assert.That(before).IsNotNull();
        await db.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();

        var after = await unit.ExecuteSerializableAsync(token => repository.GetAuthorizedParentForMutationAsync(
            db.TenantContext!.TenantId, data.OutsiderId, data.PublicId, action, token));

        await Assert.That(after).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrackedSessionParentOrTenantSubstitutionCannotReachTheRawMove(bool tenant)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var session = await db.EventSessions.SingleAsync(row => row.Id == created.Id);
        var placement = await db.EventLocations.SingleAsync(row => row.Id == session.EventLocationId);
        if (tenant)
            session.TenantId = data.ForeignTenantId;
        else
            session.EventId = data.OtherId;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
            {
                await scope.ServiceProvider.GetRequiredService<IEventSessionRepository>()
                    .MoveToEventAsync(session, data.PublicId, placement, null, token);
                return true;
            }));

        var current = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        await Assert.That(current.EventId).IsEqualTo(data.PublicId);
        await Assert.That(current.TenantId).IsEqualTo(db.TenantContext!.TenantId);
    }

    [Test]
    public async Task SessionRelocationRequiresBothParentsAndPreservesPublishedMoveRejection()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        await PrepareSessionParent(db, data.PrivateId);
        var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        db.ChangeTracker.Clear();
        var update = ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider);
        var moved = await update.ExecuteAsync(new()
        {
            EventSessionId = session.Id,
            ExpectedConcurrencyStamp = session.ConcurrencyStamp,
            EventSessionDto = new() { Event = new() { EventId = data.PrivateId } }
        }, default);
        await Assert.That(moved.IsSuccess).IsTrue();
        session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        await Assert.That(session.EventId).IsEqualTo(data.PrivateId);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<AuthorizationException>(() => update.ExecuteAsync(new()
        {
            EventSessionId = session.Id,
            ExpectedConcurrencyStamp = session.ConcurrencyStamp,
            EventSessionDto = new() { Event = new() { EventId = data.OtherId } }
        }, default));
        var foreign = await update.ExecuteAsync(new()
        {
            EventSessionId = session.Id,
            ExpectedConcurrencyStamp = session.ConcurrencyStamp,
            EventSessionDto = new() { Event = new() { EventId = data.ForeignId } }
        }, default);
        await Assert.That(foreign.IsSuccess).IsFalse();
        await db.EventSessions.Where(row => row.Id == session.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EventSessionStatusId, (int)EventSessionStatusEnum.Published));
        var published = await update.ExecuteAsync(new()
        {
            EventSessionId = session.Id,
            ExpectedConcurrencyStamp = session.ConcurrencyStamp,
            EventSessionDto = new() { Event = new() { EventId = data.PublicId } }
        }, default);
        await Assert.That(published.IsSuccess).IsFalse();
        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).EventId)
            .IsEqualTo(data.PrivateId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionTemplateDefinitionsDefaultsAndProjectionsShareTheSessionCommit(bool retry)
    {
        var boundary = new SessionCommitBoundary(retry);
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        await GrantTemplateAdministrator(db, data.OwnerId);
        Guid parentTemplateId = await SeedDeletionTemplate(db, false);
        // Template administration has its own genuine credential controls. This
        // session-commit control seeds its dependency rather than borrowing them.
        var template = new EventSessionTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            EventTemplateId = parentTemplateId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            SessionTemplateKey = $"session_{Guid.CreateVersion7():N}",
            DisplayName = "Session commit dependency",
            Version = 1,
            IsActive = true,
            IsPublished = true
        };
        var templateDefinition = new EventSessionTemplateCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            TenantId = template.TenantId,
            EventSessionTemplateId = template.Id,
            ConcurrencyStamp = Guid.CreateVersion7(),
            Namespace = "tenant",
            Key = "choice",
            DisplayName = "Choice",
            PropertyType = PropertyType.Option,
            ExposureLevel = ExposureLevel.Public,
            IsActive = true
        };
        var templateOption = new EventSessionTemplateCustomPropertyOption
        {
            Id = Guid.CreateVersion7(),
            EventSessionTemplateCustomPropertyDefinitionId = templateDefinition.Id,
            ConcurrencyStamp = Guid.CreateVersion7(),
            Namespace = "tenant",
            Key = "selected",
            DisplayName = "Selected",
            Value = "selected",
            IsDefault = true,
            IsActive = true
        };
        db.EventSessionTemplates.Add(template);
        db.EventSessionTemplateCustomPropertyDefinitions.Add(templateDefinition);
        db.EventSessionTemplateCustomPropertyOptions.Add(templateOption);
        await db.SaveChangesAsync();
        templateDefinition.DefaultOptionId = templateOption.Id;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        boundary.Arm(db);
        var handler = ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider);
        var input = SessionInput(data.PublicId) with { SessionTemplateId = template.Id };

        if (retry)
        {
            var result = await handler.ExecuteAsync(new() { EventSessionDto = input }, default);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Id).IsEqualTo(boundary.FirstSessionId);
            var definition = await db.EventSessionCustomPropertyDefinitions.AsNoTracking()
                .SingleAsync(row => row.EventSessionId == result.Id);
            var value = await db.EventSessionCustomPropertyValues.AsNoTracking().SingleAsync(row => row.EventSessionId == result.Id);
            await Assert.That(value.OptionId).IsEqualTo(definition.DefaultOptionId);
            await Assert.That(definition.SourceTemplateId).IsEqualTo(template.Id);
            await Assert.That((await db.EventSessionCustomPropertyProjections.AsNoTracking()
                .SingleAsync(row => row.EventSessionId == result.Id)).NormalizedValue).IsEqualTo("selected");
        }
        else
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                handler.ExecuteAsync(new() { EventSessionDto = input }, default));
            await Assert.That(await db.EventSessions.AnyAsync(row => row.Id == boundary.FirstSessionId)).IsFalse();
            await Assert.That(await db.EventSessionCustomPropertyDefinitions.AnyAsync(row => row.EventSessionId == boundary.FirstSessionId)).IsFalse();
            await Assert.That(await db.EventSessionCustomPropertyValues.AnyAsync(row => row.EventSessionId == boundary.FirstSessionId)).IsFalse();
            await Assert.That(await db.EventSessionCustomPropertyProjections.AnyAsync(row => row.EventSessionId == boundary.FirstSessionId)).IsFalse();
        }
        await Assert.That(boundary.DefinitionsAtFirstCommit).IsEqualTo(1);
        await Assert.That(boundary.ValuesAtFirstCommit).IsEqualTo(1);
        await Assert.That(boundary.ProjectionsAtFirstCommit).IsEqualTo(1);
    }

    [Test]
    [Arguments(RoleEnum.EventOwner)]
    [Arguments(RoleEnum.EventManager)]
    public async Task RelocationAcceptsDestinationSessionOwnerOrManager(RoleEnum role)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        await PrepareSessionParent(db, data.OtherId);
        db.EventRoleAssignments.Add(EventRoleAssignment.Create(db.TenantContext!.TenantId, data.OtherId,
            data.OwnerId, (int)role, EventRoleAssignmentStatus.Active, DateTime.UnixEpoch, null, data.OutsiderId));
        await db.SaveChangesAsync();
        var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        db.ChangeTracker.Clear();

        var result = await ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new()
            {
                EventSessionId = session.Id,
                ExpectedConcurrencyStamp = session.ConcurrencyStamp,
                EventSessionDto = new() { Event = new() { EventId = data.OtherId } }
            }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).EventId)
            .IsEqualTo(data.OtherId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelocationPreservesTenantAndInstanceSessionAdministration(bool instance)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        Guid sessionId;
        using (var owner = Scope(factory, data.OwnerId))
        {
            var seed = owner.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await PrepareSessionParent(seed, data.PublicId);
            await PrepareSessionParent(seed, data.PrivateId);
            var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(owner.ServiceProvider)
                .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
            await Assert.That(created.IsSuccess).IsTrue();
            sessionId = created.Id;
        }
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (instance)
            await GrantTemplateAdministrator(db, data.OutsiderId);
        else
        {
            var member = await db.TenantUsers.SingleAsync(row => row.UserId == data.OutsiderId);
            db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId);
        db.ChangeTracker.Clear();

        var result = await ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new()
            {
                EventSessionId = session.Id,
                ExpectedConcurrencyStamp = session.ConcurrencyStamp,
                EventSessionDto = new() { Event = new() { EventId = data.PrivateId } }
            }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId)).EventId)
            .IsEqualTo(data.PrivateId);
    }

    [Test]
    public async Task RelocationHonorsDestinationSessionCreateRefusalFromSelectedProvider()
    {
        var calls = new System.Collections.Concurrent.ConcurrentQueue<(string Kind, string Id, string Action)>();
        var grpc = Substitute.For<ICerbosClient>();
        grpc.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = (call.Arg<CheckResourcesRequest>() ?? throw new InvalidOperationException("Missing PDP request.")).ToCheckResourcesRequest();
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var resource in request.Resources)
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = resource.Resource.Id, Kind = resource.Resource.Kind }
                };
                foreach (string action in resource.Actions)
                {
                    calls.Enqueue((resource.Resource.Kind, resource.Resource.Id, action));
                    result.Actions.Add(action, action == AuthorizationActions.Update
                        ? Cerbos.Api.V1.Effect.Effect.Allow : Cerbos.Api.V1.Effect.Effect.Deny);
                }
                response.Results.Add(result);
            }
            return Task.FromResult(new CheckResourcesResponse(response));
        });
        await using var factory = new NativeEventSeriesFactory { InstanceCerbosClient = grpc };
        factory.AdditionalConfiguration["Authorization:Provider"] = "cerbos";
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        await PrepareSessionParent(db, data.PrivateId);
        Guid sessionId = await PrepareDefinitionParent(db, data.PublicId, 2);
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId);
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider).ExecuteAsync(new()
            {
                EventSessionId = session.Id,
                ExpectedConcurrencyStamp = session.ConcurrencyStamp,
                EventSessionDto = new() { Event = new() { EventId = data.PrivateId } }
            }, default));

        await Assert.That(calls.Contains((ResourceKinds.EventSession, sessionId.ToString("D"), AuthorizationActions.Update))).IsTrue();
        await Assert.That(calls.Contains((ResourceKinds.EventSession, data.PrivateId.ToString("D"), AuthorizationActions.Create))).IsTrue();
        await Assert.That(calls.Any(call => call.Kind == ResourceKinds.Event && call.Action == AuthorizationActions.Update)).IsFalse();
        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId)).EventId)
            .IsEqualTo(data.PublicId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RelocationRetryRechecksCommittedParentOrDestinationGrantDrift(bool parentDrift)
    {
        var boundary = new SessionCommitBoundary(retry: true);
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareSessionParent(db, data.PublicId);
        await PrepareSessionParent(db, data.PrivateId);
        await PrepareSessionParent(db, data.OtherId);
        var grant = EventRoleAssignment.Create(db.TenantContext!.TenantId, data.OtherId,
            data.OwnerId, (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
            DateTime.UnixEpoch, null, data.OutsiderId);
        db.EventRoleAssignments.Add(grant);
        await db.SaveChangesAsync();
        var created = await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(new() { EventSessionDto = SessionInput(data.PublicId) }, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var session = await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        db.ChangeTracker.Clear();
        boundary.Arm(db, async token =>
        {
            await using var changed = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                .CreateDbContextAsync(token);
            changed.TenantContext = new AuthorityTenant(db.TenantContext!.TenantId);
            if (!parentDrift)
            {
                await changed.EventRoleAssignments.Where(row => row.Id == grant.Id).ExecuteDeleteAsync(token);
                return;
            }
            await new EfCoreUnitOfWork(changed).ExecuteSerializableAsync(async inner =>
            {
                var placement = EventLocation.CreateToBeAnnounced(db.TenantContext!.TenantId, data.PrivateId,
                    data.OwnerId, DateTime.UtcNow);
                changed.EventLocations.Add(placement);
                await changed.SaveChangesAsync(inner);
                var current = await changed.EventSessions.SingleAsync(row => row.Id == session.Id, inner);
                var repository = new EventSessionRepository(changed, new HttpContextAccessor());
                await repository.MoveToEventAsync(current, data.PrivateId, placement, null, inner);
                await repository.Update(current);
                return true;
            }, token);
        });
        Task Change() => ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(scope.ServiceProvider).ExecuteAsync(new()
        {
            EventSessionId = session.Id,
            ExpectedConcurrencyStamp = session.ConcurrencyStamp,
            EventSessionDto = new() { Event = new() { EventId = data.OtherId } }
        }, default);

        if (parentDrift)
            await Assert.ThrowsAsync<ConcurrencyConflictException>(Change);
        else
            await Assert.ThrowsAsync<AuthorizationException>(Change);

        await Assert.That(boundary.RetryMutationApplied).IsTrue();
        await Assert.That((await db.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id)).EventId)
            .IsEqualTo(parentDrift ? data.PrivateId : data.PublicId);
    }

    private static readonly DateTimeOffset SessionStart = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private static CreateEventSessionDto SessionInput(Guid eventId) => new()
    {
        EventId = eventId,
        StartTime = SessionStart,
        EndTime = SessionStart.AddHours(1),
        Title = "New session",
        Description = "Description",
        Slug = "new-session",
        SortOrder = 5,
        MaxAudienceAttendees = 40
    };

    private static async Task PrepareSessionParent(ExploreDbContext db, Guid eventId)
    {
        var parent = await db.Events.SingleAsync(row => row.Id == eventId);
        parent.EventTimeZoneId = "UTC";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task RunSessionMutation(IServiceProvider services, int operation, EventSession session, CancellationToken token)
    {
        switch (operation)
        {
            case 0:
                await ActivatorUtilities.CreateInstance<CreateEventSessionCommandHandler>(services)
                    .ExecuteAsync(new() { EventSessionDto = SessionInput(session.EventId) }, token);
                break;
            case 1:
                await ActivatorUtilities.CreateInstance<UpdateEventSessionCommandHandler>(services)
                    .ExecuteAsync(new()
                    {
                        EventSessionId = session.Id,
                        ExpectedConcurrencyStamp = session.ConcurrencyStamp,
                        EventSessionDto = new() { Title = new() { Value = OptionalUpdate<string?>.Set("Unauthorized") } }
                    }, token);
                break;
            case 2:
                await ActivatorUtilities.CreateInstance<DeleteEventSessionCommandHandler>(services)
                    .ExecuteAsync(new() { Id = session.Id }, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private sealed class SessionCommitBoundary(bool retry) : DbTransactionInterceptor
    {
        private ExploreDbContext? _target;
        private Func<CancellationToken, Task>? _beforeRetry;
        public Guid FirstSessionId { get; private set; }
        public int AspectsAtFirstCommit { get; private set; }
        public int DefinitionsAtFirstCommit { get; private set; }
        public int ValuesAtFirstCommit { get; private set; }
        public int ProjectionsAtFirstCommit { get; private set; }
        public bool RetryMutationApplied { get; private set; }
        public void Arm(ExploreDbContext target, Func<CancellationToken, Task>? beforeRetry = null)
        {
            _target = target;
            _beforeRetry = beforeRetry;
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target) && FirstSessionId != Guid.Empty
                && Interlocked.Exchange(ref _beforeRetry, null) is { } mutation)
            {
                await mutation(cancellationToken);
                RetryMutationApplied = true;
            }
            return result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, _target) || FirstSessionId != Guid.Empty)
                return ValueTask.FromResult(result);
            FirstSessionId = _target!.ChangeTracker.Entries<EventSession>().Single().Entity.Id;
            AspectsAtFirstCommit = _target.ChangeTracker.Entries<EventSessionIslamicAspect>().Count();
            DefinitionsAtFirstCommit = _target.ChangeTracker.Entries<EventSessionCustomPropertyDefinition>().Count();
            ValuesAtFirstCommit = _target.ChangeTracker.Entries<EventSessionCustomPropertyValue>().Count();
            ProjectionsAtFirstCommit = _target.ChangeTracker.Entries<EventSessionCustomPropertyProjection>().Count();
            if (retry)
                throw new SqliteException("Native rollback-known session retry.", 5);
            throw new OperationCanceledException("Abort the shared session commit.");
        }
    }
}
