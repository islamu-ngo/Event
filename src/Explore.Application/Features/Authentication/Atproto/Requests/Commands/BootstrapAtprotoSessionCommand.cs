using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record BootstrapAtprotoSessionCommand : ICommand<AtprotoSessionBootstrapResult>
{
    public BootstrapAtprotoSessionCommand(
        AtprotoDid ExpectedDid,
        string ExpectedPdsUri,
        string OAuthClientKeyId,
        AtprotoSubjectClassification Classification,
        ReadOnlyMemory<byte> OAuthSessionPayload,
        Guid? CanonicalActorId = null,
        Guid? ExpectedCanonicalActorConcurrencyStamp = null)
    {
        this.ExpectedDid = ExpectedDid;
        this.ExpectedPdsUri = ExpectedPdsUri;
        this.OAuthClientKeyId = OAuthClientKeyId;
        this.Classification = Classification;
        this.OAuthSessionPayload = OAuthSessionPayload.ToArray();
        this.CanonicalActorId = CanonicalActorId;
        this.ExpectedCanonicalActorConcurrencyStamp = ExpectedCanonicalActorConcurrencyStamp;
    }

    public AtprotoDid ExpectedDid { get; }
    public string ExpectedPdsUri { get; }
    public string OAuthClientKeyId { get; }
    public AtprotoSubjectClassification Classification { get; }
    public ReadOnlyMemory<byte> OAuthSessionPayload { get; }
    public Guid? CanonicalActorId { get; }
    public Guid? ExpectedCanonicalActorConcurrencyStamp { get; }
}
