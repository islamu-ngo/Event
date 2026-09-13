using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSeries;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSeries.Requests.Commands;
using Explore.Application.Features.EventSeries.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventSeriesDisclosureHttpTests
{
    [Test]
    public async Task NativePorts_ProtectAllSixOperations_AndControllerHasOnlyExactOperationDependencies()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await SeedAsync(factory);
        using var scope = Scope(factory, data.AdminId);
        var services = scope.ServiceProvider;
        await Assert.That(services.GetRequiredService<ICommandHandler<CreateEventSeriesCommand, BaseCommandResponse<Guid>>>())
            .IsTypeOf<AuthorizationCommandHandlerDecorator<CreateEventSeriesCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(services.GetRequiredService<ICommandHandler<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>>())
            .IsTypeOf<AuthorizationCommandHandlerDecorator<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(services.GetRequiredService<ICommandHandler<DeleteEventSeriesCommand, BaseCommandResponse<bool>>>())
            .IsTypeOf<AuthorizationCommandHandlerDecorator<DeleteEventSeriesCommand, BaseCommandResponse<bool>>>();
        var detail = services.GetRequiredService<IQueryHandler<GetEventSeriesDetailRequest, EventSeriesDto?>>();
        await Assert.That(detail).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSeriesDetailRequest, EventSeriesDto?>>();
        var list = services.GetRequiredService<IQueryHandler<GetEventSeriesListRequest, PaginatedResult<EventSeriesListDto>>>();
        await Assert.That(list).IsTypeOf<AuthorizationQueryHandlerDecorator<GetEventSeriesListRequest, PaginatedResult<EventSeriesListDto>>>();
        var top = services.GetRequiredService<IQueryHandler<GetTopEventSeriesRequest, EventSeriesDto?>>();
        await Assert.That(top).IsTypeOf<AuthorizationQueryHandlerDecorator<GetTopEventSeriesRequest, EventSeriesDto?>>();
        await Assert.That(await detail.QueryAsync(new(data.ForeignId), default)).IsNull();
        await Assert.That(await detail.QueryAsync(new(Guid.CreateVersion7()), default)).IsNull();
        await Assert.That((await list.QueryAsync(new(), default)).Items.Select(item => item.Id)).IsEquivalentTo(new[] { data.PublicId });
        await Assert.That((await top.QueryAsync(new(), default))!.Id).IsEqualTo(data.PublicId);
        var enriched = await services.GetRequiredService<IAuthorizationContextEnricher<UpdateEventSeriesCommand>>()
            .ResolveAsync(new() { EventSeriesId = data.DraftId, EventSeriesDto = new() }, default);
        await Assert.That(enriched.ResourceId).IsEqualTo(data.ActorId.ToString());
        await Assert.That(enriched.Facts).IsEqualTo(new ActorAuthorizationFacts(PlatformDefaults.DefaultTenantId, data.ActorId));
        Type[] expected =
        [
            typeof(ICommandHandler<CreateEventSeriesCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<DeleteEventSeriesCommand, BaseCommandResponse<bool>>),
            typeof(IQueryHandler<GetEventSeriesDetailRequest, EventSeriesDto?>),
            typeof(IQueryHandler<GetEventSeriesListRequest, PaginatedResult<EventSeriesListDto>>),
            typeof(IQueryHandler<GetTopEventSeriesRequest, EventSeriesDto?>),
            typeof(Explore.API.Hateoas.IResourceAssembler<EventSeriesDto, EventSeriesListDto>)
        ];
        await Assert.That(typeof(EventSeriesController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType))
            .IsEquivalentTo(expected);
        foreach (var request in expected.Take(6).Select(port => port.GetGenericArguments()[0]))
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
    }

    [Test]
    public async Task PolicyReceivesPersistedActorFacts_AndDenialAndOutageNeverMutateTheSeries()
    {
        Guid actorId = default;
        AuthorizationRequest? observed = null;
        var unavailable = false;
        var deny = false;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            observed = call.Arg<AuthorizationRequest>();
            ArgumentNullException.ThrowIfNull(observed);
            return !deny && !unavailable && observed.ResourceKind == ResourceKinds.Actor
                && observed.Action == AuthorizationActions.Update && observed.ResourceId == actorId.ToString()
                && observed.Facts is ActorAuthorizationFacts facts
                && facts == new ActorAuthorizationFacts(PlatformDefaults.DefaultTenantId, actorId)
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, unavailable
                    ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied);
        });
        await using var factory = new NativeEventSeriesFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        actorId = data.ActorId;
        using var scope = Scope(factory, data.AdminId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventSeriesRepository>();
        var original = (await repository.GetForUpdateAsync(data.DraftId, PlatformDefaults.DefaultTenantId, default))!;
        var command = new UpdateEventSeriesCommand
        {
            EventSeriesId = data.DraftId, ExpectedConcurrencyStamp = original.ConcurrencyStamp,
            EventSeriesDto = new() { Title = new() { Value = "Provider-authorized draft" } }
        };
        var port = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventSeriesCommand, BaseCommandResponse<Guid>>>();
        await Assert.That((await port.ExecuteAsync(command, default)).IsSuccess).IsTrue();
        await Assert.That(observed!.Facts).IsEqualTo(new ActorAuthorizationFacts(PlatformDefaults.DefaultTenantId, actorId));
        var changed = (await repository.GetForUpdateAsync(data.DraftId, PlatformDefaults.DefaultTenantId, default))!;
        command = command with { ExpectedConcurrencyStamp = changed.ConcurrencyStamp, EventSeriesDto = new() { Title = new() { Value = "Denied" } } };
        deny = true;
        await Assert.That(async () => await port.ExecuteAsync(command, default)).Throws<AuthorizationException>();
        unavailable = true;
        await Assert.That(async () => await port.ExecuteAsync(command, default)).Throws<AuthorizationProviderUnavailableException>();
        using var admin = Client(factory, data.AdminId);
        using var outage = await PatchAsync(admin, data.DraftId, new { title = new { value = "Denied" } }, changed.ConcurrencyStamp);
        await ProblemAsync(outage, HttpStatusCode.ServiceUnavailable);
        var unchanged = (await repository.GetForUpdateAsync(data.DraftId, PlatformDefaults.DefaultTenantId, default))!;
        await Assert.That(unchanged.Title).IsEqualTo("Provider-authorized draft");
        await Assert.That(unchanged.ConcurrencyStamp).IsEqualTo(changed.ConcurrencyStamp);
        await Assert.That(async () => await port.ExecuteAsync(command with { EventSeriesId = data.ForeignId }, default))
            .Throws<AuthorizationException>();
    }
}
