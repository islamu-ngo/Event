using System.Reflection;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Handlers.Commands;
using Explore.Application.Features.EventOrganizerClaims.Handlers.Queries;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventOrganizerClaimOperationTests
{
    [Test]
    [Arguments(typeof(ReviewEventOrganizerClaimCommand), typeof(ReviewEventOrganizerClaimCommandHandler), typeof(BaseCommandResponse<Guid>), false, AuthorizationActions.Events.ReviewOrganizerClaim)]
    [Arguments(typeof(SubmitEventOrganizerClaimCommand), typeof(SubmitEventOrganizerClaimCommandHandler), typeof(BaseCommandResponse<Guid>), false, AuthorizationActions.Events.ClaimOrganizer)]
    [Arguments(typeof(WithdrawEventOrganizerClaimCommand), typeof(WithdrawEventOrganizerClaimCommandHandler), typeof(BaseCommandResponse<Guid>), false, AuthorizationActions.Events.WithdrawOrganizerClaim)]
    [Arguments(typeof(GetClaimantOrganizerClaimsRequest), typeof(GetClaimantOrganizerClaimsRequestHandler), typeof(IReadOnlyList<EventOrganizerClaimDto>), true, null)]
    [Arguments(typeof(GetEventOrganizerClaimRequest), typeof(GetEventOrganizerClaimRequestHandler), typeof(EventOrganizerClaimDto), true, AuthorizationActions.Events.ViewOrganizerClaims)]
    [Arguments(typeof(GetEventOrganizerClaimsRequest), typeof(GetEventOrganizerClaimsRequestHandler), typeof(IReadOnlyList<EventOrganizerClaimDto>), true, AuthorizationActions.Events.ViewOrganizerClaims)]
    public async Task Operation_HasOneNativeContractAndPreservesProtection(Type request, Type handler, Type result, bool query, string? expectedAction)
    {
        Type expectedContractType = query
            ? typeof(IQuery<>).MakeGenericType(result)
            : typeof(ICommand<>).MakeGenericType(result);

        await Assert.That(expectedContractType.IsAssignableFrom(request)).IsTrue();
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();

        Type expectedPortType = query
            ? typeof(IQueryHandler<,>).MakeGenericType(request, result)
            : typeof(ICommandHandler<,>).MakeGenericType(request, result);

        await Assert.That(handler.GetInterfaces()).IsEquivalentTo(new[] { expectedPortType });
        await Assert.That(handler.GetMethod("Handle")).IsNull();

        MethodInfo method = handler.GetMethod(query ? "QueryAsync" : "ExecuteAsync")!;
        await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<>).MakeGenericType(result));
        await Assert.That(method.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { request, typeof(CancellationToken) });

        if (expectedAction is not null)
        {
            AuthorizeResourceAttribute protection = request.GetCustomAttribute<AuthorizeResourceAttribute>()!;
            await Assert.That(protection).IsNotNull();
            await Assert.That(protection.Resource).IsEqualTo(ResourceKinds.EventOrganizerClaim);
            await Assert.That(protection.Action).IsEqualTo(expectedAction);
            await Assert.That(typeof(ISecureRequest).IsAssignableFrom(request)).IsTrue();
        }
    }

    [Test]
    public async Task Discovery_RegistersThreeScopedCommandsAndThreeScopedQueries()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(ReviewEventOrganizerClaimCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.EventOrganizerClaims.", StringComparison.Ordinal) == true));

        ServiceDescriptor[] ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(6);
        await Assert.That(ports.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(3);
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(3);
    }
}
