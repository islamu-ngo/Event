using Explore.Application;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAuthenticationOperationTests
{
    private static readonly Type[] Requests =
    [
        // Atproto Operations (8)
        typeof(BootstrapAtprotoSessionCommand),
        typeof(ConsumeAtprotoTransientCommand),
        typeof(CreateAtprotoTransientCommand),
        typeof(ProbeAtprotoTransientCommand),
        typeof(RefreshAtprotoSessionCommand),
        typeof(RevokeAtprotoSessionCommand),
        typeof(GetCurrentAtprotoOAuthSessionQuery),
        typeof(ReadAtprotoTransientQuery),

        // Local Identity Operations (13)
        typeof(RequestLocalEmailVerificationCommand),
        typeof(ConfirmLocalEmailCommand),
        typeof(RequestLocalPasswordRecoveryCommand),
        typeof(CompleteLocalPasswordRecoveryCommand),
        typeof(ChangeLocalPasswordCommand),
        typeof(CompleteLocalCredentialReplacementCommand),
        typeof(CreateLocalIdentityCommand),
        typeof(LocalLoginCommand),
        typeof(ReconcileLocalCredentialOperationCommand),
        typeof(ReconcileLocalIdentityLifecycleMirrorCommand),
        typeof(ResetLocalCredentialCommand),
        typeof(GetLocalCredentialOperationQuery),
        typeof(ListLocalIdentitiesQuery)
    ];

    [Test]
    [Arguments(typeof(BootstrapAtprotoSessionCommand), typeof(ICommand<AtprotoSessionBootstrapResult>))]
    [Arguments(typeof(ConsumeAtprotoTransientCommand), typeof(ICommand<AtprotoTransientCommandResult>))]
    [Arguments(typeof(CreateAtprotoTransientCommand), typeof(ICommand<AtprotoTransientCommandResult>))]
    [Arguments(typeof(ProbeAtprotoTransientCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RefreshAtprotoSessionCommand), typeof(ICommand<AtprotoSessionRefreshResult>))]
    [Arguments(typeof(RevokeAtprotoSessionCommand), typeof(ICommand<AtprotoSessionRevocationResult>))]
    [Arguments(typeof(GetCurrentAtprotoOAuthSessionQuery), typeof(IQuery<AtprotoCurrentOAuthSession?>))]
    [Arguments(typeof(ReadAtprotoTransientQuery), typeof(IQuery<AtprotoTransientValue?>))]
    [Arguments(typeof(RequestLocalEmailVerificationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ConfirmLocalEmailCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RequestLocalPasswordRecoveryCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CompleteLocalPasswordRecoveryCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ChangeLocalPasswordCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CompleteLocalCredentialReplacementCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CreateLocalIdentityCommand), typeof(ICommand<LocalCredentialIssueCommandResponse>))]
    [Arguments(typeof(LocalLoginCommand), typeof(ICommand<LocalAuthResponseDto>))]
    [Arguments(typeof(ReconcileLocalCredentialOperationCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ReconcileLocalIdentityLifecycleMirrorCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ResetLocalCredentialCommand), typeof(ICommand<LocalCredentialIssueCommandResponse>))]
    [Arguments(typeof(GetLocalCredentialOperationQuery), typeof(IQuery<LocalCredentialOperationStatus?>))]
    [Arguments(typeof(ListLocalIdentitiesQuery), typeof(IQuery<LocalIdentityPage>))]
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
        await Assert.That(Requests.Length).IsEqualTo(21);

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
    public async Task ApplicationComposition_RegistersEveryAuthenticationOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(BootstrapAtprotoSessionCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(21);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
