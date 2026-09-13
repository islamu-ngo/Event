using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Handlers.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeConfigurationManifestOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersImportEffectsAndSnapshotReadsWithScopedPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(ApplyConfigurationManifestCommand),
            typeof(ApplyConfigurationManifestCommandHandler),
            typeof(ApplyInstanceConfigurationImportCommand),
            typeof(ApplyInstanceConfigurationImportCommandHandler),
            typeof(ApplyTenantConfigurationImportCommand),
            typeof(ApplyTenantConfigurationImportCommandHandler),
            typeof(CancelInstanceConfigurationImportSessionCommand),
            typeof(CancelInstanceConfigurationImportSessionCommandHandler),
            typeof(CancelTenantConfigurationImportSessionCommand),
            typeof(CancelTenantConfigurationImportSessionCommandHandler),
            typeof(CreateInstanceConfigurationImportSessionCommand),
            typeof(CreateInstanceConfigurationImportSessionCommandHandler),
            typeof(CreateInstanceConfigurationRollbackSessionCommand),
            typeof(CreateInstanceConfigurationRollbackSessionCommandHandler),
            typeof(CreateTenantConfigurationImportSessionCommand),
            typeof(CreateTenantConfigurationImportSessionCommandHandler),
            typeof(CreateTenantConfigurationRollbackSessionCommand),
            typeof(CreateTenantConfigurationRollbackSessionCommandHandler),
            typeof(ExportConfigurationManifestQuery),
            typeof(ExportConfigurationManifestQueryHandler),
            typeof(ExportTenantConfigurationPackageQuery),
            typeof(ExportTenantConfigurationPackageQueryHandler),
            typeof(GetInstanceConfigurationImportReceiptQuery),
            typeof(GetInstanceConfigurationImportReceiptQueryHandler),
            typeof(GetTenantConfigurationImportReceiptQuery),
            typeof(GetTenantConfigurationImportReceiptQueryHandler),
            typeof(ListInstanceConfigurationImportHistoryQuery),
            typeof(ListInstanceConfigurationImportHistoryQueryHandler),
            typeof(ListTenantConfigurationImportHistoryQuery),
            typeof(ListTenantConfigurationImportHistoryQueryHandler),
            typeof(PreviewInstanceConfigurationImportSessionCommand),
            typeof(PreviewInstanceConfigurationImportSessionCommandHandler),
            typeof(PreviewTenantConfigurationImportSessionCommand),
            typeof(PreviewTenantConfigurationImportSessionCommandHandler)
        ]);
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor =>
                descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(17);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Count(port =>
            port.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<>))).IsEqualTo(2);
        await Assert.That(ports.Count(port =>
            port.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(9);
        await Assert.That(ports.Count(port =>
            port.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(6);
    }
}
