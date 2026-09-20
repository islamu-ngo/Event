using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Features.Appearance.Handlers.Commands;
using Explore.Application.Features.Appearance.Handlers.Queries;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Features.Appearance.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAppearanceOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersFourCommandsAndFourQueries()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateUiThemeCommand), typeof(CreateUiThemeCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateUiThemeCommand), typeof(UpdateUiThemeCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteUiThemeCommand), typeof(DeleteUiThemeCommandHandler),
                typeof(ICommandHandler<,>), typeof(bool)),
            (typeof(UpdateCurrentUserAppearancePreferencesCommand), typeof(UpdateCurrentUserAppearancePreferencesCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetAvailableThemesQuery), typeof(GetAvailableThemesQueryHandler),
                typeof(IQueryHandler<,>), typeof(IReadOnlyList<AvailableThemeDto>)),
            (typeof(GetCurrentUserAppearancePreferencesQuery), typeof(GetCurrentUserAppearancePreferencesQueryHandler),
                typeof(IQueryHandler<,>), typeof(UserAppearancePreferencesDto)),
            (typeof(GetUiThemeCatalogQuery), typeof(GetUiThemeCatalogQueryHandler),
                typeof(IQueryHandler<,>), typeof(IReadOnlyList<UiThemeListItemDto>)),
            (typeof(GetUiThemeDetailsQuery), typeof(GetUiThemeDetailsQueryHandler),
                typeof(IQueryHandler<,>), typeof(UiThemeDetailsDto))
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
        }
    }

    [Test]
    public async Task MissingOrUnauthorizedTheme_DeclaresNullableResult()
    {
        var method = typeof(GetUiThemeDetailsQueryHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<UiThemeDetailsDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
