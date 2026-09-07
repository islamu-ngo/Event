using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

public class SetupSecretProvider : ISetupSecretProvider, IDisposable
{
    private string _secret;
    private readonly string? _generatedSecretFilePath;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _bootstrapCheckSemaphore = new(1, 1);
    private bool _isLocked;
    private bool? _isBootstrapComplete;

    public bool IsSetupSecretRequired { get; }
    public bool IsTrustedManagedProvisioningConfigured { get; }
    public bool IsFromEnvironmentVariable { get; }
    public string? GeneratedSecretFilePath => IsFromEnvironmentVariable ? null : _generatedSecretFilePath;

    public bool IsSetupModeActive
    {
        get
        {
            if (_isLocked)
                return false;

            if (_isBootstrapComplete.HasValue)
                return !_isBootstrapComplete.Value;

            // Not yet initialized — fail closed (setup mode inactive)
            return false;
        }
    }

    public SetupSecretProvider(IConfiguration configuration, IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _generatedSecretFilePath = ResolveGeneratedSecretFilePath(configuration["SETUP_SECRET_FILE"]);
        IsTrustedManagedProvisioningConfigured = HasTrustedManagedProvisioningConfiguration(configuration);

        var requestedSetupSecretRequired = ReadBoolean(configuration["SETUP_SECRET_REQUIRED"], defaultValue: true);
        IsSetupSecretRequired = requestedSetupSecretRequired || !IsTrustedManagedProvisioningConfigured;

        if (!IsSetupSecretRequired)
        {
            _secret = string.Empty;
            IsFromEnvironmentVariable = false;
            DeleteGeneratedSecretFile();
            return;
        }

        var envSecret = configuration["SETUP_SECRET"];
        var replicaCount = configuration.GetValue<int?>("Hosting:ReplicaCount") ?? 1;
        if (replicaCount > 1 && string.IsNullOrWhiteSpace(envSecret))
        {
            throw new InvalidOperationException(
                "SETUP_SECRET must be provided by one deployment-owned authority when Hosting:ReplicaCount is greater than one.");
        }

        if (!string.IsNullOrWhiteSpace(envSecret))
        {
            _secret = envSecret;
            IsFromEnvironmentVariable = true;
            DeleteGeneratedSecretFile();
        }
        else
        {
            _secret = string.Empty;
            IsFromEnvironmentVariable = false;
        }
    }

    public bool ValidateSecret(string? secret)
    {
        if (!IsSetupSecretRequired || _isLocked || string.IsNullOrEmpty(secret))
            return false;

        return FixedTimeEquals(secret);
    }

    public async Task<SetupSecretValidationOutcome> ValidateSecretAsync(
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (!await IsSetupModeActiveAsync(cancellationToken))
            return SetupSecretValidationOutcome.SetupCompleted;

        if (!IsSetupSecretRequired || string.IsNullOrEmpty(secret))
            return SetupSecretValidationOutcome.Rejected;

        return FixedTimeEquals(secret)
            ? SetupSecretValidationOutcome.Accepted
            : SetupSecretValidationOutcome.Rejected;
    }

    public async Task<bool> IsSetupModeActiveAsync(CancellationToken cancellationToken = default)
    {
        if (_isLocked)
            return false;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateRepository>();
        var bootstrapState = await repository.GetCurrent(cancellationToken);
        var isCompleted = bootstrapState?.Status == InstanceBootstrapStatus.Completed;
        _isBootstrapComplete = isCompleted;
        if (isCompleted)
        {
            DeleteGeneratedSecretFile();
        }

        return !isCompleted;
    }

    public void Lock()
    {
        _isLocked = true;
        _isBootstrapComplete = true;
        DeleteGeneratedSecretFile();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isBootstrapComplete.HasValue)
            return;

        await _bootstrapCheckSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isBootstrapComplete.HasValue)
                return;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateRepository>();
            var bootstrapState = await repository.GetCurrent(cancellationToken);
            _isBootstrapComplete = bootstrapState?.Status == InstanceBootstrapStatus.Completed;

            if (_isBootstrapComplete.Value)
            {
                DeleteGeneratedSecretFile();
            }
            else if (_generatedSecretFilePath is not null && !IsFromEnvironmentVariable)
            {
                _secret = LoadOrCreateGeneratedSecret(_generatedSecretFilePath);
            }
        }
        finally
        {
            _bootstrapCheckSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _bootstrapCheckSemaphore.Dispose();
    }

    private bool FixedTimeEquals(string secret)
    {
        var expected = Encoding.UTF8.GetBytes(_secret);
        var actual = Encoding.UTF8.GetBytes(secret);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string GenerateCryptoRandomSecret()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    private static string ResolveGeneratedSecretFilePath(string? path)
    {
        var resolvedPath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Path.GetTempPath(), "islamu-event", "setup-secret")
            : path;
        return Path.GetFullPath(resolvedPath);
    }

    private static string LoadOrCreateGeneratedSecret(string path)
    {
        try
        {
            return LoadOrCreateGeneratedSecretCore(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "SETUP_SECRET must be provided because the generated setup-secret path is not writable.",
                exception);
        }
    }

    private static string LoadOrCreateGeneratedSecretCore(string path)
    {
        if (File.Exists(path))
            return ReadGeneratedSecret(path);

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("SETUP_SECRET_FILE must include a parent directory.");
        Directory.CreateDirectory(directory);

        var secret = GenerateCryptoRandomSecret();
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var stream = new FileStream(temporaryPath, options))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.WriteLine(secret);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path);
            EnsureOwnerOnlyPermissions(path);
            return secret;
        }
        catch (IOException) when (File.Exists(path))
        {
            return ReadGeneratedSecret(path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ReadGeneratedSecret(string path)
    {
        EnsureOwnerOnlyPermissions(path);
        var secret = File.ReadAllText(path).Trim();
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidDataException("The generated setup secret file is empty.");

        return secret;
    }

    private static void EnsureOwnerOnlyPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private void DeleteGeneratedSecretFile()
    {
        if (_generatedSecretFilePath is not null && File.Exists(_generatedSecretFilePath))
            File.Delete(_generatedSecretFilePath);
    }

    private static bool HasTrustedManagedProvisioningConfiguration(IConfiguration configuration)
    {
        if (!ReadBoolean(configuration["PROVISIONING_TRUSTED"], defaultValue: false))
            return false;

        if (!IsManagedProvisioningMode(configuration["PROVISIONING_MODE"]))
            return false;

        if (string.IsNullOrWhiteSpace(configuration["MANAGED_CLIENT_EXTERNAL_PROVIDER"]))
            return false;

        return !string.IsNullOrWhiteSpace(configuration["PHYSICAL_TENANCY_MODE"]);
    }

    private static bool IsManagedProvisioningMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return false;

        var normalized = mode.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized.Equals("managedprovider", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("managedhosting", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("managed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReadBoolean(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
