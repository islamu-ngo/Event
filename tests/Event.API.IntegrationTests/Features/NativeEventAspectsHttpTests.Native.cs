using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Application.Features.EventAspects.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Event.Api.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAspectsHttpTests
{
    [Test]
    public async Task TenControllerPorts_AreProtectedScopedAndExecuteAcrossScopes()
    {
        await using var factory = new NativeEventAspectsFactory();
        var data = await SeedAsync(factory);
        using var scope = factory.Scope(data.OwnerId);
        using var otherScope = factory.Scope(data.OwnerId);
        var contracts = typeof(EventAspectController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(contracts.Length).IsEqualTo(10);
        foreach (var contract in contracts)
        {
            var port = scope.ServiceProvider.GetRequiredService(contract);
            await Assert.That(ReferenceEquals(port, scope.ServiceProvider.GetRequiredService(contract))).IsTrue();
            await Assert.That(ReferenceEquals(port, otherScope.ServiceProvider.GetRequiredService(contract))).IsFalse();
            var wrapper = port.GetType().GetGenericTypeDefinition();
            await Assert.That(wrapper == typeof(AuthorizationCommandHandlerDecorator<,>) || wrapper == typeof(AuthorizationQueryHandlerDecorator<,>)).IsTrue();
        }
        var services = scope.ServiceProvider;
        var islamic = services.GetRequiredService<IQueryHandler<GetManagedEventIslamicAspectRequest, EventIslamicAspectDto?>>();
        var tech = services.GetRequiredService<IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>>();
        await Assert.That(await islamic.QueryAsync(new() { EventId = data.EmptyId }, default)).IsNull();
        await Assert.That(await tech.QueryAsync(new() { EventId = data.EmptyId }, default)).IsNull();
        var createdIslamic = await services.GetRequiredService<ICommandHandler<CreateEventIslamicAspectCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { EventId = data.EmptyId, AspectDto = new() { IncludesQuranRecitation = true } }, default);
        var createdTech = await services.GetRequiredService<ICommandHandler<CreateEventTechAspectCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { EventId = data.EmptyId, AspectDto = new() { RequiresLaptop = true } }, default);
        await Assert.That(createdIslamic.IsSuccess && createdTech.IsSuccess).IsTrue();
        await Assert.That((await islamic.QueryAsync(new() { EventId = data.EmptyId }, default))!.IncludesQuranRecitation).IsTrue();
        await Assert.That((await tech.QueryAsync(new() { EventId = data.EmptyId }, default))!.RequiresLaptop).IsTrue();
        await Assert.That((await services.GetRequiredService<ICommandHandler<UpdateEventIslamicAspectCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { EventId = data.EmptyId, AspectDto = new() { Participation = new() { IncludesQuranRecitation = false } } }, default)).IsSuccess).IsTrue();
        await Assert.That((await services.GetRequiredService<ICommandHandler<UpdateEventTechAspectCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { EventId = data.EmptyId, AspectDto = new() { Participation = new() { RequiresLaptop = false } } }, default)).IsSuccess).IsTrue();
        await Assert.That((await otherScope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventIslamicAspectRequest, EventIslamicAspectDto?>>()
            .QueryAsync(new(data.EmptyId), default))!.IncludesQuranRecitation).IsFalse();
        await Assert.That((await otherScope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventTechAspectRequest, EventTechAspectDto?>>()
            .QueryAsync(new(data.EmptyId), default))!.RequiresLaptop).IsFalse();
        await Assert.That(await services.GetRequiredService<ICommandHandler<DeleteEventIslamicAspectCommand, bool>>()
            .ExecuteAsync(new() { EventId = data.EmptyId }, default)).IsTrue();
        await Assert.That(await services.GetRequiredService<ICommandHandler<DeleteEventTechAspectCommand, bool>>()
            .ExecuteAsync(new() { EventId = data.EmptyId }, default)).IsTrue();
        await Assert.That(await islamic.QueryAsync(new() { EventId = data.EmptyId }, default)).IsNull();
        await Assert.That(await tech.QueryAsync(new() { EventId = data.EmptyId }, default)).IsNull();
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    public async Task CrossTenantCommands_DenyEvenTheForeignOwnerWithoutMutation()
    {
        await using var factory = new NativeEventAspectsFactory();
        var data = await SeedAsync(factory);
        using var scope = factory.Scope(data.OwnerId);
        var services = scope.ServiceProvider;
        Func<Task>[] writes =
        [
            async () => { await services.GetRequiredService<ICommandHandler<CreateEventIslamicAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.ForeignId, AspectDto = new() }, default); },
            async () => { await services.GetRequiredService<ICommandHandler<CreateEventTechAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.ForeignId, AspectDto = new() }, default); },
            async () => { await services.GetRequiredService<ICommandHandler<UpdateEventIslamicAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.ForeignId, AspectDto = new() { Participation = new() { IncludesQuranRecitation = false } } }, default); },
            async () => { await services.GetRequiredService<ICommandHandler<UpdateEventTechAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.ForeignId, AspectDto = new() { Participation = new() { RequiresLaptop = false } } }, default); },
            async () => { await services.GetRequiredService<ICommandHandler<DeleteEventIslamicAspectCommand, bool>>()
                .ExecuteAsync(new() { EventId = data.ForeignId }, default); },
            async () => { await services.GetRequiredService<ICommandHandler<DeleteEventTechAspectCommand, bool>>()
                .ExecuteAsync(new() { EventId = data.ForeignId }, default); }
        ];
        foreach (var write in writes)
            await Assert.That(write).Throws<AuthorizationException>();
        using var foreignScope = factory.Scope(data.OwnerId, data.ForeignTenantId);
        await Assert.That((await foreignScope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventIslamicAspectRequest, EventIslamicAspectDto?>>()
            .QueryAsync(new() { EventId = data.ForeignId }, default))!.IncludesQuranRecitation).IsTrue();
        await Assert.That((await foreignScope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>>()
            .QueryAsync(new() { EventId = data.ForeignId }, default))!.RequiresLaptop).IsTrue();
    }

    [Test]
    public async Task PersistedParentAuthority_OverridesTrackedForgeryAndObservesCrossScopeChange()
    {
        Guid ownerId = Guid.Empty;
        AuthorizationRequest? observed = null;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<AuthorizationRequest>();
            ArgumentNullException.ThrowIfNull(request);
            observed = request;
            return request.Facts is EventAuthorizationFacts facts &&
                (request.Action == AuthorizationActions.Events.ViewManagement || facts.UserId == ownerId)
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.Denied);
        });
        await using var factory = new NativeEventAspectsFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        ownerId = data.OwnerId;
        using var scope = factory.Scope(data.OwnerId);
        var services = scope.ServiceProvider;
        var read = services.GetRequiredService<IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>>();
        var before = await read.QueryAsync(new() { EventId = data.PrivateId }, default);
        var tracked = await services.GetRequiredService<ExploreDbContext>().Events.SingleAsync(row => row.Id == data.PrivateId);
        tracked.ActorId = data.OtherActorId; // Unsaved attacker-controlled identity-map state is not authority.
        var update = services.GetRequiredService<ICommandHandler<UpdateEventTechAspectCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(await read.QueryAsync(new() { EventId = data.PrivateId }, default)).IsEqualTo(before);
        await Assert.That(((EventAuthorizationFacts)observed!.Facts!).UserId).IsEqualTo(data.OwnerId);
        using (var writer = factory.Scope(data.OwnerId))
        {
            await writer.ServiceProvider.GetRequiredService<ExploreDbContext>().Events.Where(row => row.Id == data.PrivateId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorId, data.OtherActorId));
        }
        await Assert.That(async () => await update.ExecuteAsync(new()
        {
            EventId = data.PrivateId,
            AspectDto = new() { Participation = new() { RequiresLaptop = false } }
        }, default)).Throws<AuthorizationException>();
        var facts = (EventAuthorizationFacts)observed!.Facts!;
        await Assert.That(facts.EventId).IsEqualTo(data.PrivateId);
        await Assert.That(facts.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(facts.UserId).IsEqualTo(data.OutsiderId);
        await Assert.That(observed.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(await read.QueryAsync(new() { EventId = data.PrivateId }, default)).IsEqualTo(before);
        using var fresh = factory.Scope(data.OwnerId);
        await Assert.That(await fresh.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>>()
            .QueryAsync(new() { EventId = data.PrivateId }, default)).IsEqualTo(before);
    }
}
