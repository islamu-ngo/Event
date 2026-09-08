namespace Explore.Infrastructure.ConfigurationManifest;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Validation;

public sealed class ConfigurationManifestReader : IConfigurationManifestReader
{
    public const int MaximumBytes = 4_194_304;
    public const string ConventionPath = ConfigurationManifestOptions.ConventionPath;

    private const int ReadBufferSize = 8_192;

    public async Task<ConfigurationManifestReadResult?> ReadAsync(
        ConfigurationManifestReadOptions request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Mode))
            throw Failure(ConfigurationManifestIngestionFailureCodes.ModeInvalid);
        if (request.Mode == ConfigurationManifestMode.Off)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        bool hasExplicitPath = !string.IsNullOrWhiteSpace(request.ConfiguredPath);
        string path = hasExplicitPath ? request.ConfiguredPath! : ConventionPath;
        if (!Path.IsPathFullyQualified(path))
            throw Failure(ConfigurationManifestIngestionFailureCodes.PathInvalid);
        if (Directory.Exists(path))
            throw Failure(ConfigurationManifestIngestionFailureCodes.FileNotRegular);
        if (!File.Exists(path))
        {
            if (!hasExplicitPath)
                return null;
            throw Failure(ConfigurationManifestIngestionFailureCodes.FileMissing);
        }

        EnsureRegularFile(path);

        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = ReadBufferSize
                });
            ConfigurationManifestReadResult result = await ReadStreamAsync(
                stream,
                request.Mode,
                cancellationToken);
            EnsureRegularFile(path);
            return result;
        }
        catch (ConfigurationManifestIngestionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Failure(
                ConfigurationManifestIngestionFailureCodes.FileUnreadable,
                exception);
        }
        catch (IOException exception)
        {
            throw Failure(
                ConfigurationManifestIngestionFailureCodes.FileUnreadable,
                exception);
        }
    }

    internal async Task<ConfigurationManifestReadResult> ReadStreamAsync(
        Stream source,
        ConfigurationManifestMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(mode) || mode == ConfigurationManifestMode.Off)
            throw Failure(ConfigurationManifestIngestionFailureCodes.ModeInvalid);

        if (source.CanSeek && source.Length > MaximumBytes)
            throw Failure(ConfigurationManifestIngestionFailureCodes.TooLarge);

        byte[] bytes = GC.AllocateUninitializedArray<byte>(MaximumBytes + 1);
        int byteLength = 0;
        while (byteLength < bytes.Length)
        {
            int read = await source.ReadAsync(
                bytes.AsMemory(byteLength, bytes.Length - byteLength),
                cancellationToken);
            if (read == 0)
                break;

            byteLength += read;
            if (byteLength > MaximumBytes)
                throw Failure(ConfigurationManifestIngestionFailureCodes.TooLarge);
        }

        if (byteLength == 0)
            throw Failure(ConfigurationManifestIngestionFailureCodes.Empty);

        ReadOnlySpan<byte> content = bytes.AsSpan(0, byteLength);
        ValidateLexicalJson(content);
        cancellationToken.ThrowIfCancellationRequested();

        ConfigurationManifestV1Alpha2 manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(
                content,
                ConfigurationPortabilityJsonContext.Default.ConfigurationManifestV1Alpha2)
                    ?? throw new JsonException(
                        "The configuration manifest root was null.");
        }
        catch (JsonException exception)
        {
            throw new ConfigurationManifestIngestionException(
                ConfigurationManifestFailureCodes.ContractInvalid,
                "The configuration manifest contract is invalid.",
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationManifestValidationResult validation =
            ConfigurationManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            ConfigurationManifestValidationError error = validation.Errors[0];
            throw new ConfigurationManifestIngestionException(
                error.Code,
                error.Message);
        }

        return new ConfigurationManifestReadResult(
            manifest,
            mode,
            Convert.ToHexStringLower(SHA256.HashData(content)),
            byteLength);
    }

    private static void ValidateLexicalJson(ReadOnlySpan<byte> content)
    {
        try
        {
            ConfigurationManifestJsonScanner.Validate(content);
        }
        catch (ConfigurationManifestIngestionException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Failure(
                ConfigurationManifestIngestionFailureCodes.JsonInvalid,
                exception);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is DecoderFallbackException)
        {
            throw Failure(
                ConfigurationManifestIngestionFailureCodes.JsonInvalid,
                exception);
        }
    }

    private static void EnsureRegularFile(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            FileAttributes attributes = File.GetAttributes(path);
            if (fileInfo.LinkTarget is not null
                || attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw Failure(
                    ConfigurationManifestIngestionFailureCodes.FileSymlinkNotAllowed);
            }

            if (attributes.HasFlag(FileAttributes.Directory))
            {
                throw Failure(
                    ConfigurationManifestIngestionFailureCodes.FileNotRegular);
            }
        }
        catch (ConfigurationManifestIngestionException)
        {
            throw;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Failure(
                ConfigurationManifestIngestionFailureCodes.FileUnreadable,
                exception);
        }
        catch (IOException exception)
        {
            throw Failure(
                ConfigurationManifestIngestionFailureCodes.FileUnreadable,
                exception);
        }
    }

    private static ConfigurationManifestIngestionException Failure(
        string failureCode,
        Exception? innerException = null) =>
        new(
            failureCode,
            SafeMessage(failureCode),
            innerException);

    private static string SafeMessage(string failureCode) =>
        failureCode switch
        {
            ConfigurationManifestIngestionFailureCodes.ModeInvalid =>
                "The configuration manifest mode is invalid.",
            ConfigurationManifestIngestionFailureCodes.PathInvalid =>
                "The configuration manifest path is invalid.",
            ConfigurationManifestIngestionFailureCodes.FileMissing =>
                "The configured configuration manifest file is missing.",
            ConfigurationManifestIngestionFailureCodes.FileNotRegular =>
                "The configuration manifest source is not a regular file.",
            ConfigurationManifestIngestionFailureCodes.FileSymlinkNotAllowed =>
                "Configuration manifest symbolic links are not allowed.",
            ConfigurationManifestIngestionFailureCodes.Empty =>
                "The configuration manifest file is empty.",
            ConfigurationManifestIngestionFailureCodes.TooLarge =>
                "The configuration manifest file exceeds the maximum size.",
            ConfigurationManifestIngestionFailureCodes.JsonInvalid =>
                "The configuration manifest is not valid strict UTF-8 JSON.",
            _ => "The configuration manifest file cannot be read."
        };
}
