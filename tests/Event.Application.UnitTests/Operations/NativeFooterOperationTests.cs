using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Footer;
using Explore.Application.Features.Footer.Handlers.Commands;
using Explore.Application.Features.Footer.Handlers.Queries;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeFooterOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersNineCommandsAndFiveQueriesWithExclusiveScopedPorts()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateFooterLinkCommand), typeof(CreateFooterLinkCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(CreateFooterLinkGroupCommand), typeof(CreateFooterLinkGroupCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteFooterLinkCommand), typeof(DeleteFooterLinkCommandHandler), typeof(ICommandHandler<,>), typeof(bool)),
            (typeof(DeleteFooterLinkGroupCommand), typeof(DeleteFooterLinkGroupCommandHandler), typeof(ICommandHandler<,>), typeof(bool)),
            (typeof(PatchTenantFooterSettingsCommand), typeof(PatchTenantFooterSettingsCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(ReorderFooterLinkGroupsCommand), typeof(ReorderFooterLinkGroupsCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateFooterGovernanceSettingsCommand), typeof(UpdateFooterGovernanceSettingsCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateFooterLinkCommand), typeof(UpdateFooterLinkCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateFooterLinkGroupCommand), typeof(UpdateFooterLinkGroupCommandHandler), typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetFooterConfigQuery), typeof(GetFooterConfigQueryHandler), typeof(IQueryHandler<,>), typeof(FooterConfigDto)),
            (typeof(GetFooterGovernanceSettingsQuery), typeof(GetFooterGovernanceSettingsQueryHandler), typeof(IQueryHandler<,>), typeof(FooterGovernanceSettingsDto)),
            (typeof(GetFooterLinkGroupDetailsQuery), typeof(GetFooterLinkGroupDetailsQueryHandler), typeof(IQueryHandler<,>), typeof(FooterLinkGroupDetailsDto)),
            (typeof(GetFooterLinkGroupListQuery), typeof(GetFooterLinkGroupListQueryHandler), typeof(IQueryHandler<,>), typeof(List<FooterLinkGroupListDto>)),
            (typeof(GetTenantFooterSettingsQuery), typeof(GetTenantFooterSettingsQueryHandler), typeof(IQueryHandler<,>), typeof(TenantFooterSettingsDto))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(14);
        foreach (var operation in operations)
        {
            var descriptor = ports.Single(port => port.ServiceType.GetGenericArguments()[0] == operation.Request);
            await Assert.That(descriptor.ServiceType).IsEqualTo(operation.Port.MakeGenericType(operation.Request, operation.Result));
            await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            await Assert.That(operation.Request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
            var method = operation.Handler.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Single(method => method.ReturnType == typeof(Task<>).MakeGenericType(operation.Result));
            var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();
            await Assert.That(result.ReadState).IsEqualTo(NullabilityState.NotNull);
        }
    }
}
