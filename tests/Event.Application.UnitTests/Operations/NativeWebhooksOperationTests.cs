using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeWebhooksOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (18)
        typeof(AbandonWebhookProviderPublicationCommand),
        typeof(ArchiveWebhookEndpointCommand),
        typeof(CancelWebhookBulkReplayCommand),
        typeof(CreateWebhookConsumerCommand),
        typeof(CreateWebhookEndpointCommand),
        typeof(OpenSvixAppPortalCommand),
        typeof(PauseWebhookEndpointCommand),
        typeof(ReconcileWebhookProviderPublicationCommand),
        typeof(RedriveIncomingWebhookCommand),
        typeof(RedriveIncomingWebhookEffectCommand),
        typeof(RepairWebhookProviderBindingCommand),
        typeof(ResumeWebhookEndpointCommand),
        typeof(RetryWebhookDeliveryAttemptCommand),
        typeof(RotateWebhookEndpointSecretCommand),
        typeof(ScheduleWebhookBulkReplayCommand),
        typeof(TestWebhookEndpointCommand),
        typeof(UpdateWebhookConsumerProviderModeCommand),
        typeof(UpdateWebhookEndpointCommand),

        // Queries (16)
        typeof(GetIncomingWebhookEffectStatusQuery),
        typeof(GetWebhookBulkReplayOperationQuery),
        typeof(GetWebhookBulkReplayOperationsQuery),
        typeof(GetWebhookConsumerByIdQuery),
        typeof(GetWebhookConsumersQuery),
        typeof(GetWebhookDeliveryAttemptByIdQuery),
        typeof(GetWebhookDeliveryAttemptsQuery),
        typeof(GetWebhookEndpointByIdQuery),
        typeof(GetWebhookEndpointsQuery),
        typeof(GetWebhookEventTypesQuery),
        typeof(GetWebhookMessageByIdQuery),
        typeof(GetWebhookMessagePayloadQuery),
        typeof(GetWebhookMessagesQuery),
        typeof(GetWebhookProviderPublicationByIdQuery),
        typeof(GetWebhookProviderPublicationsQuery),
        typeof(PreviewWebhookBulkReplayQuery)
    ];

    [Test]
    [Arguments(typeof(AbandonWebhookProviderPublicationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ArchiveWebhookEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CancelWebhookBulkReplayCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CreateWebhookConsumerCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CreateWebhookEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(OpenSvixAppPortalCommand), typeof(ICommand<WebhookProviderPortalAccessCommandResponse>))]
    [Arguments(typeof(PauseWebhookEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ReconcileWebhookProviderPublicationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RedriveIncomingWebhookCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RedriveIncomingWebhookEffectCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RepairWebhookProviderBindingCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ResumeWebhookEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RetryWebhookDeliveryAttemptCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RotateWebhookEndpointSecretCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ScheduleWebhookBulkReplayCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(TestWebhookEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateWebhookConsumerProviderModeCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateWebhookEndpointCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetIncomingWebhookEffectStatusQuery), typeof(IQuery<BaseCommandResponse<IReadOnlyList<IncomingWebhookEffectStatusDto>>>))]
    [Arguments(typeof(GetWebhookBulkReplayOperationQuery), typeof(IQuery<WebhookBulkReplayOperationDto?>))]
    [Arguments(typeof(GetWebhookBulkReplayOperationsQuery), typeof(IQuery<IReadOnlyList<WebhookBulkReplayOperationDto>>))]
    [Arguments(typeof(GetWebhookConsumerByIdQuery), typeof(IQuery<WebhookConsumerDto?>))]
    [Arguments(typeof(GetWebhookConsumersQuery), typeof(IQuery<IReadOnlyList<WebhookConsumerDto>>))]
    [Arguments(typeof(GetWebhookDeliveryAttemptByIdQuery), typeof(IQuery<WebhookDeliveryAttemptDto?>))]
    [Arguments(typeof(GetWebhookDeliveryAttemptsQuery), typeof(IQuery<IReadOnlyList<WebhookDeliveryAttemptDto>>))]
    [Arguments(typeof(GetWebhookEndpointByIdQuery), typeof(IQuery<WebhookEndpointDto?>))]
    [Arguments(typeof(GetWebhookEndpointsQuery), typeof(IQuery<IReadOnlyList<WebhookEndpointDto>>))]
    [Arguments(typeof(GetWebhookEventTypesQuery), typeof(IQuery<IReadOnlyList<WebhookEventTypeDto>>))]
    [Arguments(typeof(GetWebhookMessageByIdQuery), typeof(IQuery<WebhookMessageDto?>))]
    [Arguments(typeof(GetWebhookMessagePayloadQuery), typeof(IQuery<WebhookMessagePayloadReadResult>))]
    [Arguments(typeof(GetWebhookMessagesQuery), typeof(IQuery<IReadOnlyList<WebhookMessageDto>>))]
    [Arguments(typeof(GetWebhookProviderPublicationByIdQuery), typeof(IQuery<WebhookProviderPublicationDto?>))]
    [Arguments(typeof(GetWebhookProviderPublicationsQuery), typeof(IQuery<IReadOnlyList<WebhookProviderPublicationDto>>))]
    [Arguments(typeof(PreviewWebhookBulkReplayQuery), typeof(IQuery<WebhookBulkReplayPreviewResult>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(34);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryWebhookOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CreateWebhookConsumerCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(34);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
