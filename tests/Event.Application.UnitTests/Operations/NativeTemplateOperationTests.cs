using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Features.EventSessionTemplateSync.Commands.ApplyEventSessionTemplateSync;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Features.EventTemplateSync.Commands.ApplyEventTemplateSync;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;
using EventSessionTemplateDiffDto = Explore.Application.DTOs.EventSessionTemplateSync.TemplateDiffDto;
using EventSessionTemplateOutcomeDto = Explore.Application.DTOs.EventSessionTemplateSync.TemplateSyncOutcomeDto;
using EventSessionTemplateSyncHistoryItemDto = Explore.Application.DTOs.EventSessionTemplateSync.EventSessionTemplateSyncHistoryItemDto;
using EventTemplateDiffDto = Explore.Application.DTOs.EventTemplateSync.TemplateDiffDto;
using EventTemplateOutcomeDto = Explore.Application.DTOs.EventTemplateSync.TemplateSyncOutcomeDto;
using EventTemplateSyncHistoryItemDto = Explore.Application.DTOs.EventTemplateSync.EventTemplateSyncHistoryItemDto;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeTemplateOperationTests
{
    private static readonly Type[] Requests =
    [
        // EventTemplate Commands (3) & Queries (2)
        typeof(CreateEventTemplateCommand), typeof(UpdateEventTemplateCommand), typeof(DeleteEventTemplateCommand),
        typeof(GetEventTemplateDetailsRequest), typeof(GetEventTemplateListRequest),

        // EventSessionTemplate Commands (3) & Queries (2)
        typeof(CreateEventSessionTemplateCommand), typeof(UpdateEventSessionTemplateCommand), typeof(DeleteEventSessionTemplateCommand),
        typeof(GetEventSessionTemplateDetailsRequest), typeof(GetEventSessionTemplateListRequest),

        // EventTemplateSync Command (1) & Queries (2)
        typeof(ApplyEventTemplateSyncCommand), typeof(GetEventTemplateDiffQuery), typeof(GetEventTemplateSyncHistoryQuery),

        // EventSessionTemplateSync Command (1) & Queries (2)
        typeof(ApplyEventSessionTemplateSyncCommand), typeof(GetEventSessionTemplateDiffQuery), typeof(GetEventSessionTemplateSyncHistoryQuery)
    ];

    [Test]
    [Arguments(typeof(CreateEventTemplateCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventTemplateCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventTemplateCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(GetEventTemplateDetailsRequest), typeof(IQuery<EventTemplateDto>))]
    [Arguments(typeof(GetEventTemplateListRequest), typeof(IQuery<PaginatedResult<EventTemplateListDto>>))]
    [Arguments(typeof(CreateEventSessionTemplateCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventSessionTemplateCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventSessionTemplateCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(GetEventSessionTemplateDetailsRequest), typeof(IQuery<EventSessionTemplateDto>))]
    [Arguments(typeof(GetEventSessionTemplateListRequest), typeof(IQuery<PaginatedResult<EventSessionTemplateListDto>>))]
    [Arguments(typeof(ApplyEventTemplateSyncCommand), typeof(ICommand<BaseCommandResponse<EventTemplateOutcomeDto>>))]
    [Arguments(typeof(GetEventTemplateDiffQuery), typeof(IQuery<BaseCommandResponse<EventTemplateDiffDto>>))]
    [Arguments(typeof(GetEventTemplateSyncHistoryQuery), typeof(IQuery<PaginatedResult<EventTemplateSyncHistoryItemDto>>))]
    [Arguments(typeof(ApplyEventSessionTemplateSyncCommand), typeof(ICommand<BaseCommandResponse<EventSessionTemplateOutcomeDto>>))]
    [Arguments(typeof(GetEventSessionTemplateDiffQuery), typeof(IQuery<BaseCommandResponse<EventSessionTemplateDiffDto>>))]
    [Arguments(typeof(GetEventSessionTemplateSyncHistoryQuery), typeof(IQuery<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>))]
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
        await Assert.That(Requests.Length).IsEqualTo(16);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryTemplateOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CreateEventTemplateCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(16);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
