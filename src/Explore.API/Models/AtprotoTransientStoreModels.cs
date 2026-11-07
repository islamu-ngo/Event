using System.Text.Json.Serialization;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.API.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAtprotoTransientRequest(string Purpose, string TokenDigest, Guid? TenantId,
    string ProtectedPayload, long ExpiresAtUnixMilliseconds, AtprotoAuthenticationFlowKind FlowKind)
{
    public CreateAtprotoTransientCommand ToCommand() => new(AtprotoTransientWirePurpose.Parse(Purpose),
        TokenDigest, TenantId, ProtectedPayload, ExpiresAtUnixMilliseconds, FlowKind);
    public override string ToString() => nameof(CreateAtprotoTransientRequest);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReadAtprotoTransientRequest(string Purpose, string TokenDigest, Guid? ExpectedTenantId,
    AtprotoAuthenticationFlowKind FlowKind)
{
    public ReadAtprotoTransientQuery ToQuery() => new(AtprotoTransientWirePurpose.Parse(Purpose), TokenDigest, ExpectedTenantId, FlowKind);
    public override string ToString() => nameof(ReadAtprotoTransientRequest);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConsumeAtprotoTransientRequest(Guid CandidateId, string Purpose, string TokenDigest, Guid ExpectedTenantId)
{
    public ConsumeAtprotoTransientCommand ToCommand() => new(CandidateId, AtprotoTransientWirePurpose.Parse(Purpose), TokenDigest, ExpectedTenantId);
    public override string ToString() => nameof(ConsumeAtprotoTransientRequest);
}

public sealed record AtprotoTransientResponse(Guid Id, string Purpose, string TokenDigest, Guid? TenantId,
    string ProtectedPayload, long ExpiresAtUnixMilliseconds, AtprotoAuthenticationFlowKind FlowKind)
{
    public static AtprotoTransientResponse From(AtprotoTransientValue value) => new(value.Id,
        value.Purpose == AtprotoTransientPurpose.OAuthState ? "oauth_state" : "tenant_handoff",
        value.TokenDigest, value.TenantId, value.ProtectedPayload, value.ExpiresAtUnixMilliseconds, value.FlowKind);
    public override string ToString() => nameof(AtprotoTransientResponse);
}

internal static class AtprotoTransientWirePurpose
{
    internal static AtprotoTransientPurpose Parse(string purpose) => purpose switch
    {
        "oauth_state" => AtprotoTransientPurpose.OAuthState,
        "tenant_handoff" => AtprotoTransientPurpose.TenantHandoff,
        _ => (AtprotoTransientPurpose)0
    };
}
