using System.Security.Cryptography;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;

namespace Explore.Application.Features.Authentication.Atproto.Handlers.Commands;

public sealed class ProbeAtprotoTransientCommandHandler(IAtprotoTransientStoreRepository store, TimeProvider clock)
    : ICommandHandler<ProbeAtprotoTransientCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(ProbeAtprotoTransientCommand request, CancellationToken cancellationToken = default)
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
