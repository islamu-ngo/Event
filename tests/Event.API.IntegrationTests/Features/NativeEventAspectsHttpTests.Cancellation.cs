using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Application.Features.EventAspects.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAspectsHttpTests
{
    [Test]
    public async Task CancelledAuthority_ReachesAllEightProtectedOperationsWithoutMutation()
    {
        CancellationToken observed = default;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            observed = call.Arg<CancellationToken>();
            return observed.IsCancellationRequested
                ? Task.FromCanceled<AuthorizationDecision>(observed)
                : Task.FromResult(AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos));
        });
        await using var factory = new NativeEventAspectsFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        using var scope = factory.Scope(data.OwnerId);
        var services = scope.ServiceProvider;
        var islamic = services.GetRequiredService<IQueryHandler<GetManagedEventIslamicAspectRequest, EventIslamicAspectDto?>>();
        var tech = services.GetRequiredService<IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>>();
        var beforeIslamic = await islamic.QueryAsync(new() { EventId = data.PrivateId }, default);
        var beforeTech = await tech.QueryAsync(new() { EventId = data.PrivateId }, default);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;
        Func<Task>[] operations =
        [
            async () => { await services.GetRequiredService<ICommandHandler<CreateEventIslamicAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.EmptyId, AspectDto = new() }, token); },
            async () => { await services.GetRequiredService<ICommandHandler<CreateEventTechAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.EmptyId, AspectDto = new() }, token); },
            async () => { await services.GetRequiredService<ICommandHandler<UpdateEventIslamicAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.PrivateId, AspectDto = new() { Participation = new() { IncludesQuranRecitation = false } } }, token); },
            async () => { await services.GetRequiredService<ICommandHandler<UpdateEventTechAspectCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new() { EventId = data.PrivateId, AspectDto = new() { Participation = new() { RequiresLaptop = false } } }, token); },
            async () => { await services.GetRequiredService<ICommandHandler<DeleteEventIslamicAspectCommand, bool>>()
                .ExecuteAsync(new() { EventId = data.PrivateId }, token); },
            async () => { await services.GetRequiredService<ICommandHandler<DeleteEventTechAspectCommand, bool>>()
                .ExecuteAsync(new() { EventId = data.PrivateId }, token); },
            async () => { await islamic.QueryAsync(new() { EventId = data.PrivateId }, token); },
            async () => { await tech.QueryAsync(new() { EventId = data.PrivateId }, token); }
        ];
        foreach (var operation in operations)
        {
            observed = default;
            await Assert.That(operation).Throws<OperationCanceledException>();
            await Assert.That(observed).IsEqualTo(token);
        }
        using var fresh = factory.Scope(data.OwnerId);
        var freshIslamic = fresh.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventIslamicAspectRequest, EventIslamicAspectDto?>>();
        var freshTech = fresh.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventTechAspectRequest, EventTechAspectDto?>>();
        await Assert.That(await freshIslamic.QueryAsync(new() { EventId = data.PrivateId }, default)).IsEqualTo(beforeIslamic);
        await Assert.That(await freshTech.QueryAsync(new() { EventId = data.PrivateId }, default)).IsEqualTo(beforeTech);
        await Assert.That(await freshIslamic.QueryAsync(new() { EventId = data.EmptyId }, default)).IsNull();
        await Assert.That(await freshTech.QueryAsync(new() { EventId = data.EmptyId }, default)).IsNull();
    }

    [Test]
    [Arguments("islamic")]
    [Arguments("tech")]
    public async Task HttpAbort_CancelsPendingAuthorityBeforePatch(string kind)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var authorization = call.Arg<AuthorizationRequest>();
            ArgumentNullException.ThrowIfNull(authorization);
            if (authorization.Action != AuthorizationActions.Update)
                return AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos);
            var token = call.Arg<CancellationToken>();
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation signal did not carry request cancellation.");
        });
        await using var factory = new NativeEventAspectsFactory { AuthorizationProviderOverride = provider };
        var data = await SeedAsync(factory);
        using var client = factory.Client(data.OwnerId);
        var before = await GetAsync(client, Managed(data.PrivateId, kind));
        using var cancellation = new CancellationTokenSource();
        var request = client.PatchAsJsonAsync(Public(data.PrivateId, kind), PatchBody(kind), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(async () => { using var response = await request.WaitAsync(TimeSpan.FromSeconds(10)); })
            .Throws<OperationCanceledException>();
        await Assert.That(System.Text.Json.JsonElement.DeepEquals(before, await GetAsync(client, Managed(data.PrivateId, kind)))).IsTrue();
    }
}
