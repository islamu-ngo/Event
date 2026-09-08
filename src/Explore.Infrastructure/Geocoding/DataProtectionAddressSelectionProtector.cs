using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Geocoding;

public sealed class DataProtectionAddressSelectionProtector
    : IAddressSelectionProtector
{
    private const string Purpose = "ISLAMU.Geocoding.AddressSelection";
    private const int Version = 1;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _selectionLifetime;

    public DataProtectionAddressSelectionProtector(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        IOptions<PhotonGeocodingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _dataProtectionProvider = dataProtectionProvider;
        _timeProvider = timeProvider;
        PhotonGeocodingOptions configured = options.Value;
        _selectionLifetime = TimeSpan.FromSeconds(configured.SelectionLifetimeSeconds);
        ConfigurationFingerprint = CreateConfigurationFingerprint(configured);
    }

    public int CurrentVersion => Version;

    public string ConfigurationFingerprint { get; }

    public Task<AddressSelectionToken> ProtectAsync(
        ProtectedAddressSelection selection,
        AddressSelectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(context);
        if (!IsValid(selection) || !IsValid(context, ConfigurationFingerprint))
        {
            throw new ArgumentException("The address selection protection input is invalid.");
        }

        DateTimeOffset expiresAt = _timeProvider.GetUtcNow().Add(_selectionLifetime);
        var envelope = new SelectionEnvelope(
            Version,
            expiresAt,
            context,
            selection);
        string plaintext = JsonSerializer.Serialize(envelope);
        string token = CreateProtector(context).Protect(plaintext);
        return Task.FromResult(new AddressSelectionToken(token, expiresAt));
    }

    public Task<AddressSelectionUnprotectResult> UnprotectAsync(
        string token,
        AddressSelectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(token)
            || !IsValid(context, ConfigurationFingerprint))
        {
            return Invalid();
        }

        SelectionEnvelope? envelope;
        try
        {
            string plaintext = CreateProtector(context).Unprotect(token);
            envelope = JsonSerializer.Deserialize<SelectionEnvelope>(plaintext);
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or NotSupportedException)
        {
            return Invalid();
        }

        if (envelope is null
            || envelope.Version != Version
            || envelope.Context != context
            || !IsValid(envelope.Selection))
        {
            return Invalid();
        }

        if (envelope.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return Task.FromResult(
                AddressSelectionUnprotectResult.Failure(
                    AddressSelectionFailureCode.Expired));
        }

        return Task.FromResult(
            AddressSelectionUnprotectResult.Success(envelope.Selection));
    }

    private IDataProtector CreateProtector(AddressSelectionContext context) =>
        _dataProtectionProvider
            .CreateProtector(Purpose)
            .CreateProtector($"v{Version}")
            .CreateProtector(context.Purpose.ToString())
            .CreateProtector(context.TenantId.ToString("D"))
            .CreateProtector(ConfigurationFingerprint);

    private static bool IsValid(ProtectedAddressSelection? selection) =>
        selection is not null
        && IsBounded(selection.DisplayName, 300, required: true)
        && IsBounded(selection.Address, 500, required: true)
        && IsBounded(selection.Postcode, 32, required: false)
        && IsBounded(selection.City, 200, required: true)
        && IsBounded(selection.Country, 200, required: true)
        && IsBounded(selection.Timezone, 100, required: false)
        && IsBounded(selection.Attribution, 200, required: true)
        && IsBounded(selection.Provenance.Provider, 64, required: true)
        && IsBounded(selection.Provenance.ProviderRecordId, 160, required: false)
        && IsBounded(selection.Provenance.DatasetVersion, 128, required: false)
        && string.Equals(
            selection.Provenance.Provider,
            PhotonProvenance.Provider,
            StringComparison.Ordinal)
        && double.IsFinite(selection.Latitude)
        && double.IsFinite(selection.Longitude)
        && selection.Latitude is >= -90 and <= 90
        && selection.Longitude is >= -180 and <= 180;

    private static bool IsValid(
        AddressSelectionContext? context,
        string configurationFingerprint) =>
        context is not null
        && context.TenantId != Guid.Empty
        && context.ActorId != Guid.Empty
        && (context.OrganizationId is null || context.OrganizationId != Guid.Empty)
        && context.Target is not null
        && string.Equals(
            context.ConfigurationFingerprint,
            configurationFingerprint,
            StringComparison.Ordinal)
        && context.Purpose switch
        {
            AddressSelectionPurpose.CreateLocation =>
                context.Target.LocationId is null
                && context.Target.ExpectedConcurrencyStamp is null,
            AddressSelectionPurpose.UpdateLocation =>
                context.Target.LocationId is { } locationId
                && locationId != Guid.Empty
                && context.Target.ExpectedConcurrencyStamp is { } stamp
                && stamp != Guid.Empty,
            _ => false
        };

    private static bool IsBounded(string? value, int maximumLength, bool required) =>
        value is null
            ? !required
            : value.Length <= maximumLength
                && (!required || !string.IsNullOrWhiteSpace(value));

    private static string CreateConfigurationFingerprint(
        PhotonGeocodingOptions options)
    {
        string canonical = string.Join(
            '|',
            options.Provider.Trim().ToUpperInvariant(),
            options.Endpoint?.GetLeftPart(UriPartial.Authority).ToUpperInvariant() ?? string.Empty,
            options.Language.Trim().ToUpperInvariant(),
            options.DatasetVersion.Trim().ToUpperInvariant(),
            string.Join(',', options.CountryCodes
                .Select(code => code.Trim().ToUpperInvariant())
                .Order(StringComparer.Ordinal)));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(digest);
    }

    private static Task<AddressSelectionUnprotectResult> Invalid() =>
        Task.FromResult(
            AddressSelectionUnprotectResult.Failure(
                AddressSelectionFailureCode.Invalid));

    private sealed record SelectionEnvelope(
        int Version,
        DateTimeOffset ExpiresAtUtc,
        AddressSelectionContext Context,
        ProtectedAddressSelection Selection);
}
