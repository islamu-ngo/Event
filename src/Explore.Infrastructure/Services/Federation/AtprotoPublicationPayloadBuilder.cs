using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoPublicationPayloadBuilder(
    AtprotoEventPublicationSnapshotFactory snapshotFactory) : IAtprotoPublicationPayloadBuilder
{
    public async Task<AtprotoPublicationPayloadBuildResult> BuildEventAsync(
        AtprotoEventPublicationEntityGraph graph,
        DateTimeOffset serverNowUtc,
        CancellationToken cancellationToken)
    {
        AtprotoEventPublicationSnapshotResult snapshot = await snapshotFactory.CreateAsync(
            graph,
            serverNowUtc,
            cancellationToken);
        if (!snapshot.IsEligible)
        {
            return AtprotoPublicationPayloadBuildResult.Invalid("projection_invalid");
        }

        var record = AtprotoCalendarEventRecordMapper.Map(snapshot.Snapshot!);
        return AtprotoCalendarEventRecordValidator.Validate(record).IsValid
            ? Build(record.ToJson().GetRawText())
            : AtprotoPublicationPayloadBuildResult.Invalid("payload_invalid");
    }
    private static AtprotoPublicationPayloadBuildResult Build(string json)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return AtprotoPublicationPayloadBuildResult.Valid(new(json, hash));
    }
}
