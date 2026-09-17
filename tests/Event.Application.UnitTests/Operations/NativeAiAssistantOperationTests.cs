using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAiAssistantOperationTests
{
    private static readonly Type[] Requests =
    [
        typeof(CancelAiRunCommand),
        typeof(ConfirmAiProposedActionCommand),
        typeof(CreateAiConversationCommand),
        typeof(GrantAiConsentCommand),
        typeof(ProcessAiRunCommand),
        typeof(ProposeAiToolActionCommand),
        typeof(RejectAiProposedActionCommand),
        typeof(RevokeAiConsentCommand),
        typeof(RunAiRetentionCleanupCommand),
        typeof(SendAiMessageCommand),
        typeof(GetAiAssistantBootstrapQuery),
        typeof(GetAiConversationDetailQuery),
        typeof(GetAiConversationListQuery),
        typeof(GetAiRunStatusQuery),
        typeof(SearchAiReferencesQuery)
    ];

    [Test]
    [Arguments(typeof(CancelAiRunCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ConfirmAiProposedActionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CreateAiConversationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GrantAiConsentCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ProcessAiRunCommand), typeof(ICommand))]
    [Arguments(typeof(ProposeAiToolActionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RejectAiProposedActionCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RevokeAiConsentCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RunAiRetentionCleanupCommand), typeof(ICommand<AiRetentionCleanupResult>))]
    [Arguments(typeof(SendAiMessageCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetAiAssistantBootstrapQuery), typeof(IQuery<AiAssistantBootstrapDto>))]
    [Arguments(typeof(GetAiConversationDetailQuery), typeof(IQuery<AiConversationDto?>))]
    [Arguments(typeof(GetAiConversationListQuery), typeof(IQuery<IReadOnlyList<AiConversationSummaryDto>>))]
    [Arguments(typeof(GetAiRunStatusQuery), typeof(IQuery<AiRunDto?>))]
    [Arguments(typeof(SearchAiReferencesQuery), typeof(IQuery<IReadOnlyList<AiReferenceSearchResultDto>>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type =>
            type == typeof(ICommand) ||
            (type.IsGenericType &&
             (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
              type.GetGenericTypeDefinition() == typeof(IQuery<>))))).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(15);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type =>
                type == typeof(ICommand) ||
                (type.IsGenericType &&
                 (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                  type.GetGenericTypeDefinition() == typeof(IQuery<>)))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryAiAssistantOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CancelAiRunCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(15);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
