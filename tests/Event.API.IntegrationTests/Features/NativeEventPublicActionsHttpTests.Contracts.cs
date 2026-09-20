using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventPublicAction;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Features.EventPublicActions.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task TwoLoadedWriters_CannotOverwriteTheCommittedAction()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var first = Scope(factory, data.OwnerId);
        using var second = Scope(factory, data.OwnerId);
        var firstSnapshot = (await first.ServiceProvider.GetRequiredService<IEventPublicActionRepository>()
            .GetForUpdateAsync(data.ActionId, default))!;
        var secondSnapshot = (await second.ServiceProvider.GetRequiredService<IEventPublicActionRepository>()
            .GetForUpdateAsync(data.ActionId, default))!;
        await Assert.That(firstSnapshot.ConcurrencyStamp).IsEqualTo(secondSnapshot.ConcurrencyStamp);
        var command = new UpdateEventPublicActionCommand
        {
            EventId = data.PublicId,
            ActionId = data.ActionId,
            Action = Input(firstSnapshot.ConcurrencyStamp)
        };
        var winner = await first.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(command, default);
        await Assert.That(winner.IsSuccess).IsTrue();
        var committed = await State(factory, data.ActionId);
        await Assert.That(async () => await second.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(command with { Action = command.Action with { Url = "https://example.test/loser" } }, default))
            .Throws<DbUpdateConcurrencyException>();
        await Assert.That(await State(factory, data.ActionId)).IsEqualTo(committed);
    }

    [Test]
    public async Task ActualHost_ClosesControllerOverSixProtectedScopedPortsAndPreservesOpenApi()
    {
        await using var factory = new NativeEventSeriesFactory();
        using var client = Client(factory);
        using var first = Scope(factory);
        using var second = Scope(factory);
        Type[] ports =
        [
            typeof(IQueryHandler<GetEventPublicActionsRequest, IReadOnlyList<EventPublicActionDto>>),
            typeof(IQueryHandler<GetEventPublicActionRequest, EventPublicActionDto?>),
            typeof(ICommandHandler<CreateEventPublicActionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<DeleteEventPublicActionCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<RecordEventPublicActionEngagementCommand>)
        ];
        await Assert.That(typeof(EventPublicActionController).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType))
            .IsEquivalentTo(ports.Append(typeof(IResourceAssembler<EventPublicActionDto, EventPublicActionDto>)));
        foreach (var port in ports)
        {
            var service = first.ServiceProvider.GetRequiredService(port);
            var expected = port.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) ? typeof(AuthorizationQueryHandlerDecorator<,>)
                : port.GetGenericTypeDefinition() == typeof(ICommandHandler<>) ? typeof(AuthorizationCommandHandlerDecorator<>)
                : typeof(AuthorizationCommandHandlerDecorator<,>);
            await Assert.That(service.GetType().GetGenericTypeDefinition()).IsEqualTo(expected);
            await Assert.That(ReferenceEquals(service, first.ServiceProvider.GetRequiredService(port))).IsTrue();
            await Assert.That(ReferenceEquals(service, second.ServiceProvider.GetRequiredService(port))).IsFalse();
        }
        await Assert.That(ActivatorUtilities.CreateInstance<EventPublicActionController>(first.ServiceProvider)).IsNotNull();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Explore.slnx"))) root = root.Parent;
        using var shipped = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "schemas/openapi_islamu-event.json")));
        foreach (var path in new[]
        {
            "/api/events/{eventId}/public-actions", "/api/events/{eventId}/public-actions/{actionId}",
            "/api/events/{eventId}/public-actions/{actionId}/redirect"
        })
            await Assert.That(JsonElement.DeepEquals(actual.RootElement.GetProperty("paths").GetProperty(path),
                shipped.RootElement.GetProperty("paths").GetProperty(path))).IsTrue();
        foreach (var schema in new[] { "EventPublicActionDto", "ManageEventPublicActionDto" })
            await Assert.That(JsonElement.DeepEquals(actual.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema),
                shipped.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema))).IsTrue();
    }

    [Test]
    public async Task ProtectedWrites_EnforceManualValidationParticipationPrimaryUniquenessAndCancellation()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        var original = (await State(factory, data.ActionId))!;
        var actionIds = await ActionIds(factory, data.PublicId);
        using var scope = Scope(factory, data.OwnerId);
        var create = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventPublicActionCommand, BaseCommandResponse<Guid>>>();
        var update = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>>();
        ManageEventPublicActionDto[] invalid =
        [
            Input(original.Stamp) with { KindId = 999 },
            Input(original.Stamp) with { Url = "" },
            Input(original.Stamp) with { Url = "http://example.test/action" },
            Input(original.Stamp) with { Url = "https://user@example.test/action" },
            Input(original.Stamp) with { Url = "https://example.test/action#fragment" },
            Input(original.Stamp) with { Url = "https://example.test/" + new string('x', 2048) },
            Input(original.Stamp) with { Label = new string('x', 121) },
            Input(original.Stamp) with { SortOrder = -1 },
            Input(original.Stamp) with { SortOrder = 1001 },
            Input(original.Stamp) with { KindId = (int)EventPublicActionKindEnum.ExternalRegistration },
            Input(original.Stamp) with { KindId = (int)EventPublicActionKindEnum.OptionalQuestionnaire }
        ];
        foreach (var action in invalid)
        {
            await Assert.That((await create.ExecuteAsync(new() { EventId = data.PublicId, Action = action }, default)).IsSuccess).IsFalse();
            await Assert.That((await update.ExecuteAsync(new() { EventId = data.PublicId, ActionId = data.ActionId, Action = action }, default)).IsSuccess).IsFalse();
            await Assert.That(await State(factory, data.ActionId)).IsEqualTo(original);
        }
        await Assert.That((await update.ExecuteAsync(new() { EventId = data.PublicId, ActionId = data.ActionId, Action = Input() }, default)).IsSuccess).IsFalse();
        await Assert.That(await ActionIds(factory, data.PublicId)).IsEquivalentTo(actionIds);
        var primary = await create.ExecuteAsync(new() { EventId = data.PublicId, Action = Input() with { IsPrimary = true } }, default);
        await Assert.That(primary.IsSuccess).IsTrue();
        await Assert.That((await create.ExecuteAsync(new() { EventId = data.PublicId, Action = Input() with { IsPrimary = true } }, default)).IsSuccess).IsFalse();
        await Assert.That((await update.ExecuteAsync(new()
        {
            EventId = data.PublicId,
            ActionId = data.ActionId,
            Action = Input(original.Stamp) with { IsPrimary = true }
        }, default)).IsSuccess).IsFalse();
        await Assert.That(await State(factory, data.ActionId)).IsEqualTo(original);
        var primaryState = (await State(factory, primary.Id))!;
        await Assert.That((await update.ExecuteAsync(new()
        {
            EventId = data.PublicId,
            ActionId = primary.Id,
            Action = Input(primaryState.Stamp) with { IsPrimary = true }
        }, default)).IsSuccess).IsTrue();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.That(async () => await create.ExecuteAsync(new() { EventId = data.PublicId, Action = Input() }, cancelled.Token))
            .Throws<OperationCanceledException>();
        var detail = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventPublicActionRequest, EventPublicActionDto?>>();
        await Assert.That(async () => await detail.QueryAsync(new(data.PublicId, data.ActionId), cancelled.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(await State(factory, data.ActionId)).IsEqualTo(original);
    }
}
