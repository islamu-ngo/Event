namespace Explore.Application.Features.ConfigurationManifest.Requests.Queries;

using Explore.Application.Authorization;
using FluentValidation;
using MediatR;

public enum ConfigurationManifestExportView
{
    Overrides = 0,
    Portable = 1
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record ExportConfigurationManifestQuery(
    ConfigurationManifestExportView View = ConfigurationManifestExportView.Overrides)
    : IRequest<ConfigurationManifestExportResult>, ISecureRequest
{
    public const string ResourceKey = "instance.configuration-manifest.export";

    string? ISecureRequest.ResourceId => ResourceKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new ConfigurationManifestExportAuthorizationFacts();
}

public sealed record ConfigurationManifestExportResult
{
    public required ConfigurationManifestExportView View { get; init; }

    public required string FileName { get; init; }

    public required ReadOnlyMemory<byte> Utf8Json { get; init; }

    public bool SensitiveValuesOmitted => true;
}

public static class ConfigurationManifestExportContract
{
    public const int MaximumUtf8Bytes = 4 * 1024 * 1024;
    public const string TooLargeFailureCode = "configuration_manifest_export_too_large";
}

public sealed class ConfigurationManifestExportTooLargeException : InvalidOperationException
{
    public ConfigurationManifestExportTooLargeException()
        : base("The configuration manifest export exceeds the maximum supported size.")
    {
    }

    public string FailureCode => ConfigurationManifestExportContract.TooLargeFailureCode;
}

public sealed class ExportConfigurationManifestQueryValidator
    : AbstractValidator<ExportConfigurationManifestQuery>
{
    public ExportConfigurationManifestQueryValidator()
    {
        RuleFor(request => request.View).IsInEnum();
    }
}
