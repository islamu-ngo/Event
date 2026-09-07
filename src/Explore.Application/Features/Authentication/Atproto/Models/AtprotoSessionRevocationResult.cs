namespace Explore.Application.Features.Authentication.Atproto.Models;

public enum AtprotoSessionRevocationOutcome
{
    Revoked,
    AlreadyAbsent,
    RemoteFailedLocalCleared
}

public sealed record AtprotoSessionRevocationResult(AtprotoSessionRevocationOutcome Outcome);
