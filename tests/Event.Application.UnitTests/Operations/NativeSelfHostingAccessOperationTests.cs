using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Application.Features.ContactShareConsents.Requests.Commands;
using Explore.Application.Features.ContactShareConsents.Requests.Queries;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Commands;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeSelfHostingAccessOperationTests
{
    private static readonly Type[] Requests =
    [
        // ExternalApiKeys Commands (3) & Queries (3)
        typeof(CreateExternalApiKeyCommand),
        typeof(RevokeExternalApiKeyCommand),
        typeof(UpdateExternalApiKeyPolicyCommand),
        typeof(GetExternalApiKeyDetailsRequest),
        typeof(GetExternalApiKeyListRequest),
        typeof(GetExternalApiKeyUsageReportRequest),

        // SupportAccess Commands (3) & Queries (3)
        typeof(StartSupportAccessSessionCommand),
        typeof(StopSupportAccessSessionCommand),
        typeof(ForceStopSupportAccessSessionCommand),
        typeof(GetCurrentSupportAccessSessionQuery),
        typeof(GetSupportAccessAuditEventsQuery),
        typeof(ListSupportAccessSessionsQuery),

        // UserAuthenticationTokens Command (1) & Queries (2)
        typeof(DeleteUserAuthenticationTokenCommand),
        typeof(GetUserAuthenticationTokenDetailsRequest),
        typeof(GetUserAuthenticationTokenListRequest),

        // ContactShareConsents Commands (2) & Queries (2)
        typeof(ExportSharedContactsCommand),
        typeof(WithdrawContactShareConsentCommand),
        typeof(GetOrganizationSharedContactsQuery),
        typeof(GetUserContactShareConsentsQuery)
    ];

    [Test]
    [Arguments(typeof(CreateExternalApiKeyCommand), typeof(ICommand<CreateExternalApiKeyCommandResponse>))]
    [Arguments(typeof(RevokeExternalApiKeyCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(UpdateExternalApiKeyPolicyCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetExternalApiKeyDetailsRequest), typeof(IQuery<ExternalApiKeyListDto?>))]
    [Arguments(typeof(GetExternalApiKeyListRequest), typeof(IQuery<List<ExternalApiKeyListDto>>))]
    [Arguments(typeof(GetExternalApiKeyUsageReportRequest), typeof(IQuery<List<ExternalApiKeyUsageReportDto>>))]
    [Arguments(typeof(StartSupportAccessSessionCommand), typeof(ICommand<SupportAccessSessionCommandResponseDto>))]
    [Arguments(typeof(StopSupportAccessSessionCommand), typeof(ICommand<SupportAccessSessionCommandResponseDto>))]
    [Arguments(typeof(ForceStopSupportAccessSessionCommand), typeof(ICommand<SupportAccessSessionCommandResponseDto>))]
    [Arguments(typeof(GetCurrentSupportAccessSessionQuery), typeof(IQuery<CurrentSupportAccessSessionDto>))]
    [Arguments(typeof(GetSupportAccessAuditEventsQuery), typeof(IQuery<PaginatedResult<SupportAccessAuditEventDto>>))]
    [Arguments(typeof(ListSupportAccessSessionsQuery), typeof(IQuery<PaginatedResult<SupportAccessSessionDto>>))]
    [Arguments(typeof(DeleteUserAuthenticationTokenCommand), typeof(ICommand))]
    [Arguments(typeof(GetUserAuthenticationTokenDetailsRequest), typeof(IQuery<UserAuthenticationTokenDto?>))]
    [Arguments(typeof(GetUserAuthenticationTokenListRequest), typeof(IQuery<List<UserAuthenticationTokenListDto>>))]
    [Arguments(typeof(ExportSharedContactsCommand), typeof(ICommand<BaseCommandResponse<SharedContactExportResultDto>>))]
    [Arguments(typeof(WithdrawContactShareConsentCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetOrganizationSharedContactsQuery), typeof(IQuery<PaginatedResult<SharedContactDto>>))]
    [Arguments(typeof(GetUserContactShareConsentsQuery), typeof(IQuery<List<UserContactShareConsentDto>>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        if (port == typeof(ICommand))
        {
            await Assert.That(request.GetInterfaces().Any(type => type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                    || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsFalse();
        }
        else
        {
            await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
                && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                    || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(19);

        foreach (var request in Requests)
        {
            var genericShapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            var nonGenericShapes = request.GetInterfaces().Where(type => type == typeof(ICommand)).ToArray();
            await Assert.That(genericShapes.Length + nonGenericShapes.Length).IsEqualTo(1);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEverySelfHostingAccessOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CreateExternalApiKeyCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(19);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
