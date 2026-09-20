using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Localization.Handlers.Commands;
using Explore.Application.Features.Localization.Handlers.Queries;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLocalizationOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersThreeWritesAndFiveReadsWithExactResults()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(ExportFromTmsCommand), typeof(ExportFromTmsCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(ImportLocalizationBundleCommand), typeof(ImportLocalizationBundleCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateLocalizationGovernanceCommand), typeof(UpdateLocalizationGovernanceCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(TestTmsConnectionQuery), typeof(TestTmsConnectionQueryHandler),
                typeof(IQueryHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(ExportLocalizationBundleQuery), typeof(ExportLocalizationBundleQueryHandler),
                typeof(IQueryHandler<,>), typeof(IReadOnlyDictionary<string, string>)),
            (typeof(GetAvailableLanguagesQuery), typeof(GetAvailableLanguagesQueryHandler),
                typeof(IQueryHandler<,>), typeof(List<string>)),
            (typeof(GetLocalizationTmsApiKeyConfiguredQuery), typeof(GetLocalizationTmsApiKeyConfiguredQueryHandler),
                typeof(IQueryHandler<,>), typeof(bool)),
            (typeof(GetTranslationsQuery), typeof(GetTranslationsQueryHandler),
                typeof(IQueryHandler<,>), typeof(Dictionary<string, string>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(8);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor =>
                descriptor.ServiceType.GetGenericArguments()[0] == operation.Request).ServiceType;
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
            await Assert.That(operation.Request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        }
    }
}
