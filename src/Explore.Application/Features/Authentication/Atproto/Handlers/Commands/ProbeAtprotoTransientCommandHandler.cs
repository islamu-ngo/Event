// ABOUTME: Verifies actual transient create, read and single-use consumption with synthetic non-secret data.
// ABOUTME: Restricts failed probes to tenantless records expiring after thirty seconds for bounded cleanup.

using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class ProbeAtprotoTransientCommandHandler(IAtprotoTransientStoreRepository store, TimeProvider clock)
    : IRequestHandler<ProbeAtprotoTransientCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ProbeAtprotoTransientCommand request, CancellationToken cancellationToken)
    {
        var row = AtprotoTransientRecord.CreateHealthProbe(
            Convert.ToHexStringLower(SHA256.HashData(RandomNumberGenerator.GetBytes(32))),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            clock.GetUtcNow().AddSeconds(30).ToUnixTimeMilliseconds());
        if (!await store.TryCreateHealthProbeAsync(row, cancellationToken))
            throw new InvalidOperationException("Transient probe unavailable.");
        var read = await store.ReadHealthProbeAsync(row.Id, row.TokenDigest, cancellationToken);
        if (read is null || read.ProtectedPayload != row.ProtectedPayload
            || read.ExpiresAtUnixMilliseconds != row.ExpiresAtUnixMilliseconds
            || !await store.ConsumeHealthProbeAsync(row.Id, row.TokenDigest, cancellationToken))
            throw new InvalidOperationException("Transient probe unavailable.");
        return BaseCommandResponse.Success(row.Id);
    }
}
