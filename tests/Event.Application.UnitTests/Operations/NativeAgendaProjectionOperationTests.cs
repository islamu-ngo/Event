using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Agenda;
using Explore.Application.Features.Agenda.Handlers.Queries;
using Explore.Application.Features.Agenda.Requests.Queries;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAgendaProjectionOperationTests
{
    [Test]
    public async Task Projection_IsOneNativeQueryWithoutMediatorContract()
    {
        var interfaces = typeof(GetEventAgendaProjectionRequest).GetInterfaces();
        await Assert.That(interfaces).Contains(typeof(IQuery<EventAgendaProjectionDto?>));
        await Assert.That(interfaces.Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(interfaces.Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(IQuery<>)
                || type.GetGenericTypeDefinition() == typeof(ICommand<>)))).IsEqualTo(1);
    }

    [Test]
    public async Task ProjectionHandler_ExposesTaskBasedNullableQuery()
    {
        var method = typeof(GetEventAgendaProjectionRequestHandler).GetMethod("QueryAsync");
        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task<EventAgendaProjectionDto?>));
        var nullability = new System.Reflection.NullabilityInfoContext().Create(method.ReturnParameter);
        await Assert.That(nullability.GenericTypeArguments.Single().ReadState)
            .IsEqualTo(System.Reflection.NullabilityState.Nullable);
        await Assert.That(typeof(GetEventAgendaProjectionRequestHandler).GetInterfaces())
            .Contains(typeof(IQueryHandler<,>).MakeGenericType(typeof(GetEventAgendaProjectionRequest), typeof(EventAgendaProjectionDto)));
    }
}
