using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.LegalDocuments.Handlers.Queries;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLegalDocumentOperationTests
{
    [Test]
    public async Task RequestAndHandler_ExposeOnlyTheNativeQueryContract()
    {
        await Assert.That(typeof(IQuery<PublicLegalDocumentQueryResult>)
            .IsAssignableFrom(typeof(GetPublicLegalDocumentQuery))).IsTrue();
        await Assert.That(typeof(GetPublicLegalDocumentQuery).GetInterfaces()
            .Any(type => type.Namespace == "MediatR")).IsFalse();
        var contracts = typeof(GetPublicLegalDocumentQueryHandler).GetInterfaces();
        await Assert.That(contracts.Length).IsEqualTo(1);
        await Assert.That(contracts[0].GetGenericTypeDefinition()).IsEqualTo(typeof(IQueryHandler<,>));
        await Assert.That(contracts[0].GenericTypeArguments.SequenceEqual(
            [typeof(GetPublicLegalDocumentQuery), typeof(PublicLegalDocumentQueryResult)])).IsTrue();
        await Assert.That(typeof(GetPublicLegalDocumentQueryHandler).GetMethod("Handle")).IsNull();
    }

    [Test]
    public async Task Discovery_RegistersExactlyOneScopedQueryWithoutLegacyDispatch()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetPublicLegalDocumentQuery), typeof(GetPublicLegalDocumentQueryHandler)]);
        var ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericTypeDefinition()).IsEqualTo(typeof(IQueryHandler<,>));
        await Assert.That(ports[0].ServiceType.GenericTypeArguments.SequenceEqual(
            [typeof(GetPublicLegalDocumentQuery), typeof(PublicLegalDocumentQueryResult)])).IsTrue();
    }
}
