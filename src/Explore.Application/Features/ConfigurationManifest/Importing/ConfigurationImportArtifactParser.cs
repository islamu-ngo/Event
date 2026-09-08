namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Validation;

public sealed record ConfigurationImportParsedArtifact(
    ConfigurationManifestV1Alpha2 Manifest,
    string Sha256Digest,
    int ByteLength)
{
    public override string ToString() => nameof(ConfigurationImportParsedArtifact);
}

public sealed record ConfigurationImportParsedTenantPackage(
    TenantConfigurationPackageV1Alpha2 Package,
    string Sha256Digest,
    int ByteLength)
{
    public override string ToString() =>
        nameof(ConfigurationImportParsedTenantPackage);
}

public sealed class ConfigurationImportArtifactParser
{
    private const int MaximumDepth = 32;

    public ConfigurationImportParsedArtifact Parse(
        ReadOnlyMemory<byte> artifact)
    {
        if (artifact.IsEmpty)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }

        if (artifact.Length > ConfigurationImportSessionLimits.MaximumArtifactBytes)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TooLarge);
        }

        try
        {
            ConfigurationManifestV1Alpha2 manifest =
                ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(artifact);
            ConfigurationManifestValidationResult validation =
                ConfigurationManifestValidator.Validate(manifest);
            if (!validation.IsValid)
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.ContractInvalid);
            }

            return new ConfigurationImportParsedArtifact(
                manifest,
                ConfigurationImportDigest.ComputeBytes(artifact.Span),
                artifact.Length);
        }
        catch (ConfigurationImportSessionException)
        {
            throw;
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            throw new ConfigurationImportSessionException(
                exception.Code == ConfigurationPortabilityDiagnosticCodes.TooLarge
                    ? ConfigurationImportFailureCodes.TooLarge
                    : ConfigurationImportFailureCodes.ContractInvalid);
        }
        catch (JsonException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
    }

    public ConfigurationImportParsedTenantPackage ParseTenantPackage(
        ReadOnlyMemory<byte> artifact)
    {
        EnsureBounded(artifact);

        try
        {
            TenantConfigurationPackageV1Alpha2 package =
                ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(artifact);
            ConfigurationManifestValidationResult validation =
                ConfigurationManifestValidator.Validate(package);
            if (!validation.IsValid)
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.ContractInvalid);
            }

            return new ConfigurationImportParsedTenantPackage(
                package,
                ConfigurationImportDigest.ComputeBytes(artifact.Span),
                artifact.Length);
        }
        catch (ConfigurationImportSessionException)
        {
            throw;
        }
        catch (ConfigurationPortabilityContractException exception)
        {
            throw new ConfigurationImportSessionException(
                exception.Code == ConfigurationPortabilityDiagnosticCodes.TooLarge
                    ? ConfigurationImportFailureCodes.TooLarge
                    : ConfigurationImportFailureCodes.ContractInvalid);
        }
        catch (JsonException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
    }

    private static void EnsureBounded(ReadOnlyMemory<byte> artifact)
    {
        if (artifact.IsEmpty)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
        if (artifact.Length > ConfigurationImportSessionLimits.MaximumArtifactBytes)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TooLarge);
        }
    }

    private static JsonDocument ParseDocument(ReadOnlyMemory<byte> artifact) =>
        JsonDocument.Parse(
            artifact,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth
            });

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new ConfigurationImportSessionException(
                            ConfigurationImportFailureCodes.ContractInvalid);
                    }

                    EnsureNoDuplicateProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    EnsureNoDuplicateProperties(item);
                break;
        }
    }
}
