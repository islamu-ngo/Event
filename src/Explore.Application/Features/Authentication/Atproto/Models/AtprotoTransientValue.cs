// ABOUTME: Defines immutable private transient results without exposing persistence entities.
// ABOUTME: Keeps protected bytes opaque and carries only locator, tenant, purpose and expiry binding metadata.

using Explore.Domain;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Atproto.Models;

public sealed record AtprotoTransientValue(Guid Id, AtprotoTransientPurpose Purpose, string TokenDigest,
    Guid TenantId, string ProtectedPayload, long ExpiresAtUnixMilliseconds)
{
    public override string ToString() => nameof(AtprotoTransientValue);
}

public sealed record AtprotoTransientCommandResult : BaseCommandResponse<Guid>
{
    private AtprotoTransientCommandResult(BaseCommandResponse<Guid> state, AtprotoTransientValue? value)
        : base(state, true) => Value = value;

    public AtprotoTransientValue? Value { get; }
    public static AtprotoTransientCommandResult Success(AtprotoTransientValue value) =>
        new(BaseCommandResponse.Success(value.Id), value);
    public static AtprotoTransientCommandResult Failure(BaseCommandResponse<Guid> failure) =>
        new(BaseCommandResponse.RequireFailure(failure), null);
    public override string ToString() => nameof(AtprotoTransientCommandResult);
}
