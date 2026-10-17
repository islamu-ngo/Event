using Explore.Application.Features.Actors.Requests.Commands;

namespace Explore.Application.Operations;

internal static class ModerationMutationCommandScope
{
    internal static bool Handles(Type request) =>
        request == typeof(ModerateActorCommand) || request == typeof(ModerateAtprotoIdentityCommand);
}
