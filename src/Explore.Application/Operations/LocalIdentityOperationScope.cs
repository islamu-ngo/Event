using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Handlers.Queries;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;

namespace Explore.Application.Operations;

/// <summary>Selects construction ownership for the closed Local lifecycle, never caller authorization.</summary>
internal static class LocalIdentityOperationScope
{
    private static readonly HashSet<Type> Operations =
    [
        typeof(LocalLoginCommand),
        typeof(CreateLocalIdentityCommand),
        typeof(ResetLocalCredentialCommand),
        typeof(ReconcileLocalCredentialOperationCommand),
        typeof(CompleteLocalCredentialReplacementCommand),
        typeof(RequestLocalEmailVerificationCommand),
        typeof(ConfirmLocalEmailCommand),
        typeof(RequestLocalPasswordRecoveryCommand),
        typeof(CompleteLocalPasswordRecoveryCommand),
        typeof(ChangeLocalPasswordCommand),
        typeof(ReconcileLocalIdentityLifecycleMirrorCommand),
        typeof(ListLocalIdentitiesQuery),
        typeof(GetLocalCredentialOperationQuery),
        typeof(GetLocalIdentityLifecycleCapabilitiesQuery)
    ];

    internal static bool Handles(Type operation) => Operations.Contains(operation);
}
