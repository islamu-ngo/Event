using System.Security.Cryptography;
using System.Text;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Services;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoPublicationPayloadBuilder(
    AtprotoEventPublicationSnapshotFactory snapshotFactory,
    IConfiguration configuration,
    ISystemSettingRepository systemSettings) : IAtprotoPublicationPayloadBuilder
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

        var publication = snapshot.Snapshot!;
        Uri? publicAddress = null;
        if (publication.Uris.Any(value => value.Uri.StartsWith("/", StringComparison.Ordinal)))
        {
            publicAddress = await PublicAddressResolver.ResolveAsync(configuration, systemSettings, cancellationToken);
            if (publicAddress is null)
                return AtprotoPublicationPayloadBuildResult.Invalid("public_origin_unavailable");
        }

        var record = AtprotoCalendarEventRecordMapper.Map(publication, publicAddress);
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
