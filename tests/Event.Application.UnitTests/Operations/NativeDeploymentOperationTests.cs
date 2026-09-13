using Explore.Application;
using Explore.Application.Contracts.Deployment;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Deployment;
using Explore.Application.Features.Deployment;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeDeploymentOperationTests
{
    [Test]
    public async Task Snapshot_PreservesValuesOrderAndIndependentCollectionOwnership()
    {
        string[] gates = ["security", "restore-takeover"];
        TicketingDeploymentCapability[] capabilities =
        [
            new("ticketing-recovery", "test-only", "production_restore_evidence_open", gates),
            new("purchase-governance", "disabled", "external_launch_gates_open", [])
        ];
        var catalog = new SnapshotCatalog(new(1, "deployment-revision", "split-postgresql-quartz-cluster", capabilities));
        IQueryHandler<GetTicketingDeploymentCapabilitiesQuery, TicketingDeploymentCapabilityMatrixDto> handler =
            new GetTicketingDeploymentCapabilitiesQueryHandler(catalog);

        var first = await handler.QueryAsync(new(), default);
        var second = await handler.QueryAsync(new(), default);
        await Assert.That(first.SchemaVersion).IsEqualTo(1);
        await Assert.That(first.Revision).IsEqualTo("deployment-revision");
        await Assert.That(first.ReferenceTopology).IsEqualTo("split-postgresql-quartz-cluster");
        await Assert.That(first.Capabilities.Select(item => item.Code).ToArray())
            .IsEquivalentTo(new[] { "ticketing-recovery", "purchase-governance" });
        await Assert.That(first.Capabilities[0].Status).IsEqualTo("test-only");
        await Assert.That(first.Capabilities[0].ReasonCode).IsEqualTo("production_restore_evidence_open");
        await Assert.That(first.Capabilities[0].RequiredExternalGates.SequenceEqual(new[] { "security", "restore-takeover" })).IsTrue();
        await Assert.That(first.Capabilities[1].RequiredExternalGates).IsEmpty();

        // Catalog input and each returned matrix own both levels of array storage.
        gates[0] = "catalog-changed";
        capabilities[0] = capabilities[0] with { Code = "catalog-replaced" };
        await Assert.That(first.Capabilities[0].Code).IsEqualTo("ticketing-recovery");
        await Assert.That(first.Capabilities[0].RequiredExternalGates[0]).IsEqualTo("security");
        ((string[])first.Capabilities[0].RequiredExternalGates)[0] = "caller-changed";
        ((TicketingDeploymentCapabilityDto[])first.Capabilities)[1] = first.Capabilities[0];
        await Assert.That(second.Capabilities[0].RequiredExternalGates[0]).IsEqualTo("security");
        await Assert.That(second.Capabilities[1].Code).IsEqualTo("purchase-governance");
        var current = await handler.QueryAsync(new(), default);
        await Assert.That(current.Capabilities[0].Code).IsEqualTo("catalog-replaced");
        await Assert.That(current.Capabilities[0].RequiredExternalGates[0]).IsEqualTo("catalog-changed");
        await Assert.That(current.Capabilities[1].Code).IsEqualTo("purchase-governance");
    }

    [Test]
    public async Task Cancellation_ThrowsWithOriginalTokenBeforeCatalogAccess()
    {
        IQueryHandler<GetTicketingDeploymentCapabilitiesQuery, TicketingDeploymentCapabilityMatrixDto> handler =
            new GetTicketingDeploymentCapabilitiesQueryHandler(new UnavailableCatalog());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.That(async () => await handler.QueryAsync(new(), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
    }

    [Test]
    public async Task CatalogFailure_IsNotReplacedWithAnEmptyMatrix()
    {
        IQueryHandler<GetTicketingDeploymentCapabilitiesQuery, TicketingDeploymentCapabilityMatrixDto> handler =
            new GetTicketingDeploymentCapabilitiesQueryHandler(new UnavailableCatalog());
        await Assert.That(async () => await handler.QueryAsync(new(), default)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NullQuery_IsRejectedBeforeCatalogAccess()
    {
        IQueryHandler<GetTicketingDeploymentCapabilitiesQuery, TicketingDeploymentCapabilityMatrixDto> handler =
            new GetTicketingDeploymentCapabilitiesQueryHandler(new UnavailableCatalog());
        await Assert.That(async () => await handler.QueryAsync(null!, default)).Throws<ArgumentNullException>();
    }

    private sealed class SnapshotCatalog(TicketingDeploymentCapabilitySnapshot snapshot) : ITicketingDeploymentCapabilityCatalog
    {
        public TicketingDeploymentCapabilitySnapshot GetSnapshot() => snapshot;
    }

    private sealed class UnavailableCatalog : ITicketingDeploymentCapabilityCatalog
    {
        public TicketingDeploymentCapabilitySnapshot GetSnapshot() => throw new InvalidOperationException("Catalog unavailable.");
    }

    [Test]
    public async Task Discovery_RegistersOneExclusiveScopedQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([
            typeof(GetTicketingDeploymentCapabilitiesQuery),
            typeof(GetTicketingDeploymentCapabilitiesQueryHandler)
        ]);
        var ports = services.Where(descriptor =>
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericTypeDefinition()).IsEqualTo(typeof(IQueryHandler<,>));
        await Assert.That(ports[0].ServiceType.GetGenericArguments()).IsEquivalentTo(new[]
        {
            typeof(GetTicketingDeploymentCapabilitiesQuery), typeof(TicketingDeploymentCapabilityMatrixDto)
        });
        await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(typeof(GetTicketingDeploymentCapabilitiesQuery))).IsFalse();
        await Assert.That(typeof(GetTicketingDeploymentCapabilitiesQueryHandler).GetInterfaces()
            .Any(contract => contract.Namespace == "MediatR")).IsFalse();
    }
}
