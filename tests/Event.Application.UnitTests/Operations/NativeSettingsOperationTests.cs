using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Handlers.Queries;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeSettingsOperationTests
{
    [Test]
    public async Task SharedSettingsOperations_RegisterFiveCommandsAndOneQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(UpdateSettingCommand), typeof(UpdateSettingCommandHandler),
            typeof(UpdateSettingBatchCommand), typeof(UpdateSettingBatchCommandHandler),
            typeof(ResetSettingCommand), typeof(ResetSettingCommandHandler),
            typeof(LockSettingCommand), typeof(LockSettingCommandHandler),
            typeof(UnlockSettingCommand), typeof(UnlockSettingCommandHandler),
            typeof(ResolveSettingGroupQuery), typeof(ResolveSettingGroupQueryHandler)
        ]);

        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(6);
        await Assert.That(ports.Count(type =>
            type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(5);
        var query = ports.Single(type => type.GetGenericArguments()[0] == typeof(ResolveSettingGroupQuery));
        await Assert.That(query.GetGenericTypeDefinition()).IsEqualTo(typeof(IQueryHandler<,>));
    }
}
