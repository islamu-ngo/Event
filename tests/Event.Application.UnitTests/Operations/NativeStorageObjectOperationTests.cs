using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeStorageObjectOperationTests
{
    [Test]
    public async Task Discovery_ExposesSixCommandsAndFourQueriesWithoutLegacyRequests()
    {
        Type[] cohort = typeof(CreateStorageUploadSessionCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "Explore.Application.Features.StorageObjects.", StringComparison.Ordinal) == true).ToArray();
        var services = new ServiceCollection();
        services.AddNativeOperations(cohort);
        Type[] ports = services.Where(descriptor => !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                 type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).ToArray();
        await Assert.That(ports.Select(port => port.GetGenericArguments()[0].Name)).IsEquivalentTo(new[]
        {
            "CreateStorageUploadSessionCommand", "FinalizeStorageUploadSessionCommand",
            "CancelStorageUploadSessionCommand", "DeleteStorageObjectCommand", "UpdateStorageObjectCommand",
            "IssuePresignedDownloadUrlCommand", "GetPublicImageRequest", "GetStorageObjectContentRequest",
            "GetStorageObjectDetailsRequest", "GetStorageObjectListRequest"
        });
        foreach (Type port in ports)
        {
            Type request = port.GetGenericArguments()[0];
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(
                request.Name.EndsWith("Command", StringComparison.Ordinal)
                    ? typeof(ICommandHandler<,>) : typeof(IQueryHandler<,>));
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == port).Lifetime)
                .IsEqualTo(ServiceLifetime.Scoped);
        }
    }
}
