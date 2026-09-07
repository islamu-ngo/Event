using CarpaNet;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoJetstreamOptions
{
    public const string SectionName = "Atproto:Jetstream";
    public string Endpoint { get; set; } = BlueskyServices.JetstreamUsEast;
    public int MaxMessageSizeBytes { get; set; } = 2_113_536;

    /// <summary>
    /// Enables dict-zstd transport compression. The v2 client fetches and rotates the dictionary itself,
    /// so this costs an extra outbound fetch at connect time; it is opt-in to keep the default connection
    /// profile unchanged.
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Maximum sealed-archive blocks the recovery change probe will download in one pass. Blocks are
    /// roughly 430KB and are not pre-filtered by collection, so this caps a probe at about 27MB before it
    /// gives up and lets recovery run at full scope.
    /// </summary>
    public int ArchiveProbeMaximumBlocks { get; set; } = 64;

    public int ArchiveProbeDownloadConcurrency { get; set; } = 4;
    public int LeaseDurationSeconds { get; set; } = 60;
    public int LeaseRenewalSeconds { get; set; } = 20;
    public int CapabilityPollMilliseconds { get; set; } = 5_000;
    public int RetryMinimumMilliseconds { get; set; } = 1_000;
    public int RetryMaximumMilliseconds { get; set; } = 30_000;
    public string[] AllowedDids { get; set; } = [];
}

public sealed class AtprotoJetstreamOptionsValidator : IValidateOptions<AtprotoJetstreamOptions>
{
    public ValidateOptionsResult Validate(string? name, AtprotoJetstreamOptions options)
    {
        var failures = new List<string>();
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || endpoint.AbsolutePath is not ("" or "/"))
        {
            failures.Add("Atproto:Jetstream:Endpoint must be a fixed HTTPS origin without credentials, path, query, or fragment.");
        }

        if (options.MaxMessageSizeBytes < AtprotoRecordSizeValidator.MaximumJsonBytes
            || options.MaxMessageSizeBytes > 2_162_688)
        {
            failures.Add("Atproto:Jetstream:MaxMessageSizeBytes must be between 2097152 and 2162688 bytes.");
        }

        if (options.LeaseDurationSeconds is < 15 or > 300
            || options.LeaseRenewalSeconds is < 5 or > 120
            || options.LeaseRenewalSeconds >= options.LeaseDurationSeconds)
        {
            failures.Add("Jetstream lease renewal must be between 5 and 120 seconds and shorter than the 15-300 second lease.");
        }

        if (options.CapabilityPollMilliseconds is < 100 or > 60_000
            || options.RetryMinimumMilliseconds is < 10 or > 60_000
            || options.RetryMaximumMilliseconds < options.RetryMinimumMilliseconds
            || options.RetryMaximumMilliseconds > 300_000)
        {
            failures.Add("Jetstream polling and retry intervals are outside their bounded ranges.");
        }

        if (options.ArchiveProbeMaximumBlocks is < 0 or > 4_096
            || options.ArchiveProbeDownloadConcurrency is < 1 or > 32)
        {
            failures.Add("Atproto:Jetstream archive probe budgets are outside their bounded ranges.");
        }

        if (options.AllowedDids is not { Length: <= 10_000 }
            || options.AllowedDids.Any(did => !IsValidDid(did)))
        {
            failures.Add("Atproto:Jetstream:AllowedDids must contain at most 10000 valid DIDs when filtering is configured.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidDid(string did) =>
        AtprotoDid.TryParse(did, out AtprotoDid parsedDid)
        && parsedDid.Value.Length <= 255;
}
