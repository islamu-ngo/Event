using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Geocoding;

public sealed class PhotonGeocodingOptions
{
    public const string SectionName = "Geocoding";
    public const string DisabledProvider = "None";
    public const string PhotonProvider = "Photon";

    public string Provider { get; set; } = DisabledProvider;
    public Uri? Endpoint { get; set; }
    public string Language { get; set; } = "en";
    public string[] CountryCodes { get; set; } = [];
    public string DatasetVersion { get; set; } = string.Empty;
    public int MaximumResults { get; set; } = 10;
    public int MaximumResponseBytes { get; set; } = 65_536;
    public int TotalTimeoutMilliseconds { get; set; } = 5_000;
    public int MaximumRetryCount { get; set; } = 2;
    public int[] RetryDelaysMilliseconds { get; set; } = [200, 500];
    public int ReadinessTimeoutMilliseconds { get; set; } = 2_000;
    public int SelectionLifetimeSeconds { get; set; } = 300;
}

public sealed class PhotonOptionsValidator : IValidateOptions<PhotonGeocodingOptions>
{
    private const string PublicDemoHost = "photon.komoot.io";

    public ValidateOptionsResult Validate(string? name, PhotonGeocodingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        bool disabled = string.Equals(
            options.Provider,
            PhotonGeocodingOptions.DisabledProvider,
            StringComparison.OrdinalIgnoreCase);
        bool photon = string.Equals(
            options.Provider,
            PhotonGeocodingOptions.PhotonProvider,
            StringComparison.OrdinalIgnoreCase);

        if (!disabled && !photon)
        {
            failures.Add("Geocoding:Provider must be 'None' or 'Photon'.");
        }

        if (options.Endpoint is { } endpoint)
        {
            ValidateEndpoint(endpoint, failures);
        }
        else if (photon)
        {
            failures.Add("Geocoding:Endpoint is required when Geocoding:Provider is Photon.");
        }

        if (photon && !IsLocale(options.Language))
        {
            failures.Add("Geocoding:Language must be a bounded language tag.");
        }

        if (photon && (options.CountryCodes.Length is < 1 or > 10
            || options.CountryCodes.Any(code => !IsCountryCode(code))))
        {
            failures.Add("Geocoding:CountryCodes must contain one to ten ISO two-letter country codes.");
        }

        if (photon
            && (string.IsNullOrWhiteSpace(options.DatasetVersion)
                || options.DatasetVersion.Length > 128
                || options.DatasetVersion.Any(char.IsControl)))
        {
            failures.Add(
                "Geocoding:DatasetVersion must be a non-empty bounded identifier.");
        }

        if (options.MaximumResults is < 1 or > 20)
        {
            failures.Add("Geocoding:MaximumResults must be between 1 and 20.");
        }

        if (options.MaximumResponseBytes is < 1_024 or > 1_048_576)
        {
            failures.Add("Geocoding:MaximumResponseBytes must be between 1024 and 1048576.");
        }

        if (options.TotalTimeoutMilliseconds is < 1 or > 5_000)
        {
            failures.Add("Geocoding:TotalTimeoutMilliseconds must be between 1 and 5000.");
        }

        if (options.MaximumRetryCount is < 0 or > 2)
        {
            failures.Add("Geocoding:MaximumRetryCount must be between 0 and 2.");
        }

        if (options.RetryDelaysMilliseconds.Length != options.MaximumRetryCount
            || options.RetryDelaysMilliseconds.Any(delay => delay is < 1 or > 1_000)
            || options.RetryDelaysMilliseconds.Sum() >= options.TotalTimeoutMilliseconds)
        {
            failures.Add(
                "Geocoding:RetryDelaysMilliseconds must match the retry count and fit the total timeout.");
        }

        if (options.ReadinessTimeoutMilliseconds is < 1 or > 2_000)
        {
            failures.Add("Geocoding:ReadinessTimeoutMilliseconds must be between 1 and 2000.");
        }

        if (options.SelectionLifetimeSeconds is < 30 or > 900)
        {
            failures.Add("Geocoding:SelectionLifetimeSeconds must be between 30 and 900.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateEndpoint(Uri endpoint, List<string> failures)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("Geocoding:Endpoint must be an absolute HTTPS URL.");
            return;
        }

        if (string.Equals(endpoint.Host, PublicDemoHost, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Geocoding:Endpoint must not use the public Photon demo service.");
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            failures.Add("Geocoding:Endpoint must not contain credentials, a query, or a fragment.");
        }
    }

    private static bool IsLocale(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length is >= 2 and <= 16
        && value.All(character => char.IsAsciiLetter(character) || character == '-');

    private static bool IsCountryCode(string? value) =>
        value is { Length: 2 }
        && value.All(char.IsAsciiLetter);
}
