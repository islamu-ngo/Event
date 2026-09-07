using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;
using YamlDotNet.Serialization;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Builds policy package manifests from the bundled Cerbos policy directory.
/// </summary>
public sealed class CerbosPolicyPackageService : IPolicyPackageService
{
    private const string SchemaDirectoryName = "_schemas";
    private const string DerivedRolesPolicyFileName = "derived_roles.yaml";
    private const string ArchiveContentType = "application/zip";
    private const string ArchiveInstructionsFileName = "INSTRUCTIONS.md";
    private const string ArchiveManifestFileName = "manifest.json";
    private static readonly string[] PolicyFilePatterns = ["*.yaml", "*.yml", "*.json"];
    private static readonly JsonSerializerOptions AdminApiJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer JsonCompatibleYamlSerializer = new SerializerBuilder()
        .JsonCompatible()
        .Build();

    private readonly CerbosPolicyPackageOptions _options;
    private readonly CerbosAdminApiSettings _adminApiSettings;
    private readonly ICerbosConfigResolver _cerbosConfigResolver;
    private readonly CerbosAdminEndpointValidator _adminEndpointValidator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CerbosPolicyPackageService> _logger;

    public CerbosPolicyPackageService(
        IOptions<CerbosPolicyPackageOptions> options,
        IOptions<CerbosAdminApiSettings> adminApiSettings,
        ICerbosConfigResolver cerbosConfigResolver,
        CerbosAdminEndpointValidator adminEndpointValidator,
        IHttpClientFactory httpClientFactory,
        ILogger<CerbosPolicyPackageService> logger)
    {
        _options = options.Value;
        _adminApiSettings = adminApiSettings.Value;
        _cerbosConfigResolver = cerbosConfigResolver;
        _adminEndpointValidator = adminEndpointValidator;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PolicyPackageManifest> BuildManifestAsync(CancellationToken cancellationToken = default)
    {
        var packageRoot = ResolvePolicyRoot();
        var artifacts = new List<PolicyPackageArtifact>();

        foreach (var policyFile in EnumeratePolicyFiles(packageRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(policyFile);
            if (!fileName.Equals(DerivedRolesPolicyFileName, StringComparison.Ordinal)
                && !Path.GetFileNameWithoutExtension(fileName).StartsWith(_options.ProductNamespacePrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Policy artifact '{fileName}' must use the '{_options.ProductNamespacePrefix}' namespace prefix.");
            }

            artifacts.Add(await BuildArtifactAsync(packageRoot, policyFile, PolicyArtifactKind.Policy, cancellationToken));
        }

        var schemaRoot = Path.Combine(packageRoot, SchemaDirectoryName);
        if (!Directory.Exists(schemaRoot))
        {
            _logger.LogWarning("Cerbos policy package is unavailable because the schema directory is missing.");
            throw CreatePackageUnavailableException();
        }

        foreach (var schemaFile in Directory.EnumerateFiles(schemaRoot, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(schemaFile);
            if (!Path.GetFileNameWithoutExtension(fileName).StartsWith(_options.ProductNamespacePrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Schema artifact '{fileName}' must use the '{_options.ProductNamespacePrefix}' namespace prefix.");
            }

            artifacts.Add(await BuildArtifactAsync(packageRoot, schemaFile, PolicyArtifactKind.Schema, cancellationToken));
        }

        var orderedArtifacts = artifacts.OrderBy(a => a.LogicalId, StringComparer.Ordinal).ToArray();
        if (orderedArtifacts.Length == 0)
        {
            _logger.LogWarning("Cerbos policy package is unavailable because no policy or schema artifacts were found.");
            throw CreatePackageUnavailableException();
        }

        var contentHash = ComputeManifestHash(orderedArtifacts);

        _logger.LogInformation(
            "Built policy package manifest {PackageId} with {ArtifactCount} artifact(s) and contentHash={ContentHash}",
            _options.PackageId,
            orderedArtifacts.Length,
            contentHash);

        return new PolicyPackageManifest(
            PackageId: _options.PackageId,
            Version: contentHash,
            ContentHash: contentHash,
            GeneratedAt: DateTimeOffset.UtcNow,
            Artifacts: orderedArtifacts);
    }

    /// <inheritdoc />
    public Task<PolicyPackagePublishResult> PublishAsync(
        CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null) =>
        PublishCoreAsync(instanceTargetOnly: false, oneTimeCredentials, cancellationToken);

    /// <inheritdoc />
    public Task<PolicyPackagePublishResult> PublishInstanceAsync(
        CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null) =>
        PublishCoreAsync(instanceTargetOnly: true, oneTimeCredentials, cancellationToken);

    private async Task<PolicyPackagePublishResult> PublishCoreAsync(
        bool instanceTargetOnly,
        PolicyPackageAdminCredentials? oneTimeCredentials,
        CancellationToken cancellationToken)
    {
        var manifest = CreateUnavailableManifest();

        try
        {
            var packageRoot = ResolvePolicyRoot();
            manifest = await BuildManifestAsync(cancellationToken);
            var targetResolution = instanceTargetOnly
                ? ResolveInstanceAdminApiTarget(oneTimeCredentials)
                : await ResolveAdminApiTargetAsync(oneTimeCredentials, cancellationToken);
            if (!targetResolution.Succeeded || targetResolution.Target is null)
            {
                return new PolicyPackagePublishResult(
                    Succeeded: false,
                    PackageId: manifest.PackageId,
                    ContentHash: manifest.ContentHash,
                    Message: "Policy package publishing skipped because no safe Cerbos Admin API target is configured.",
                    PublishedAt: DateTimeOffset.UtcNow,
                    Warnings: targetResolution.Warnings)
                {
                    IssueCode = PolicyPackageIssueCode.AdminApiNotConfigured
                };
            }

            var target = targetResolution.Target;
            var schemas = await BuildSchemaDefinitionsAsync(packageRoot, manifest, cancellationToken);
            var policies = await BuildPolicyDocumentsAsync(packageRoot, manifest, cancellationToken);

            if (schemas.Count > 0)
                await PushSchemasAsync(target, schemas, cancellationToken);

            foreach (var policyBatch in policies.Chunk(GetPolicyBatchSize()))
                await PushPoliciesAsync(target, policyBatch, cancellationToken);

            var reloadSucceeded = await ReloadAllInstancesAsync(target, cancellationToken);
            if (!reloadSucceeded)
            {
                return new PolicyPackagePublishResult(
                    Succeeded: false,
                    PackageId: manifest.PackageId,
                    ContentHash: manifest.ContentHash,
                    Message: "Policy package was uploaded, but one or more Cerbos instances failed to reload.",
                    PublishedAt: DateTimeOffset.UtcNow,
                    Warnings: ["Investigate Cerbos Admin API reload failures before relying on the new package."])
                {
                    IssueCode = PolicyPackageIssueCode.ReloadFailed
                };
            }

            _logger.LogInformation(
                "Published policy package {PackageId} with {PolicyCount} polic(ies), {SchemaCount} schema(s), contentHash={ContentHash}",
                manifest.PackageId,
                policies.Count,
                schemas.Count,
                manifest.ContentHash);

            return new PolicyPackagePublishResult(
                Succeeded: true,
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                Message: "Policy package uploaded and Cerbos instances reloaded successfully.",
                PublishedAt: DateTimeOffset.UtcNow,
                Warnings: []);
        }
        catch (PolicyPackageUnavailableException ex)
        {
            _logger.LogWarning(ex, "Policy package publish skipped because the package is unavailable for this API deployment.");

            return new PolicyPackagePublishResult(
                Succeeded: false,
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                Message: "Policy package publishing skipped because the bundled Cerbos policy package is unavailable.",
                PublishedAt: DateTimeOffset.UtcNow,
                Warnings: [ex.Message])
            {
                IssueCode = PolicyPackageIssueCode.PackageUnavailable
            };
        }
        catch (CerbosAdminApiException ex) when (ex.IssueCode == PolicyPackageIssueCode.AdminApiAuthenticationFailed)
        {
            _logger.LogWarning(
                ex,
                "Cerbos Admin API authentication failed while publishing policy package {PackageId}",
                manifest.PackageId);

            return new PolicyPackagePublishResult(
                Succeeded: false,
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                Message: "Policy package publish failed because Cerbos Admin API authentication failed.",
                PublishedAt: DateTimeOffset.UtcNow,
                Warnings: [ex.Message])
            {
                IssueCode = ex.IssueCode
            };
        }
        catch (CerbosAdminApiException ex)
        {
            _logger.LogError(
                ex,
                "Cerbos Admin API request failed while publishing policy package {PackageId}",
                manifest.PackageId);

            return new PolicyPackagePublishResult(
                Succeeded: false,
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                Message: "Policy package publish failed because the Cerbos Admin API was unavailable or rejected the package.",
                PublishedAt: DateTimeOffset.UtcNow,
                Warnings: [ex.Message])
            {
                IssueCode = ex.IssueCode
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Policy package publish failed for {PackageId}", manifest.PackageId);

            return new PolicyPackagePublishResult(
                Succeeded: false,
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                Message: "Policy package publish failed. See server logs for Cerbos Admin API status details.",
                PublishedAt: DateTimeOffset.UtcNow,
                Warnings: [ToSafeWarning(ex)])
            {
                IssueCode = PolicyPackageIssueCode.PublishFailed
            };
        }
    }

    /// <inheritdoc />
    public async Task<PolicyPackageStatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        PolicyPackageManifest manifest;
        try
        {
            manifest = await BuildManifestAsync(cancellationToken);
        }
        catch (PolicyPackageUnavailableException ex)
        {
            _logger.LogWarning(ex, "Policy package status is unavailable because the package is unavailable for this API deployment.");

            manifest = CreateUnavailableManifest();
            return new PolicyPackageStatusResult(
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                CheckedAt: DateTimeOffset.UtcNow,
                IssueCode: PolicyPackageIssueCode.PackageUnavailable,
                Message: "The bundled Cerbos policy package is unavailable for this API deployment.",
                Warnings: [ex.Message]);
        }

        var targetResolution = await ResolveAdminApiTargetAsync(null, cancellationToken);

        if (!targetResolution.Succeeded || targetResolution.Target is null)
        {
            return new PolicyPackageStatusResult(
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                CheckedAt: DateTimeOffset.UtcNow,
                IssueCode: PolicyPackageIssueCode.AdminApiNotConfigured,
                Message: "No safe Cerbos Admin API target is configured for authorization policy package publishing.",
                Warnings: targetResolution.Warnings);
        }

        return await CompareStoreContentsAsync(manifest, targetResolution.Target, cancellationToken);
    }

    /// <summary>
    /// Compares what the store actually holds against what this deployment believes it published, and
    /// observes the store's current revision.
    /// <para>
    /// Two independent signals. <c>GET /admin/policies</c> answers whether the package reached the store at
    /// all and arrived complete — the difference between "the PDP is serving something" and "the PDP is
    /// serving nothing, and every check fails closed for a reason nobody can see", which matters more now
    /// that no local evaluator answers around it. <c>GET /admin/policy</c> then returns each policy's
    /// Cerbos-computed content hash, and folding those detects an edit to a policy that kept its
    /// identifier — the case a listing alone cannot see.
    /// </para>
    /// <para>
    /// The observed revision is reported, never compared against the app-owned package hash: the two are
    /// computed by different algorithms over different inputs and will never be equal. Its use is
    /// comparison against a previous observation of the same store.
    /// </para>
    /// </summary>
    private async Task<PolicyPackageStatusResult> CompareStoreContentsAsync(
        PolicyPackageManifest manifest,
        AdminApiTarget target,
        CancellationToken cancellationToken)
    {
        var expectedPolicyCount = manifest.Artifacts.Count(artifact => artifact.Kind == PolicyArtifactKind.Policy);

        IReadOnlyList<string>? storePolicyIds;
        try
        {
            storePolicyIds = await ListStorePolicyIdsAsync(target, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Could not list Cerbos store policies to verify package freshness. FailureType={FailureType}",
                ex.GetType().Name);

            return new PolicyPackageStatusResult(
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                CheckedAt: DateTimeOffset.UtcNow,
                IssueCode: PolicyPackageIssueCode.PackageStatusUnknown,
                Message: "Cerbos Admin API target is configured, but the policy store could not be listed.",
                Warnings: ["Package freshness is unverified until the store can be listed through the explicit status operation."]);
        }

        if (storePolicyIds is null || storePolicyIds.Count == 0)
        {
            return new PolicyPackageStatusResult(
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                CheckedAt: DateTimeOffset.UtcNow,
                IssueCode: PolicyPackageIssueCode.PackageMismatch,
                Message: "The Cerbos policy store is empty; this deployment's policy package has not been published.",
                Warnings: ["Publish the policy package. Until then the PDP denies every check it is asked to decide."]);
        }

        var observedRevision = await ObserveStoreRevisionAsync(target, storePolicyIds, cancellationToken);

        if (storePolicyIds.Count < expectedPolicyCount)
        {
            return new PolicyPackageStatusResult(
                PackageId: manifest.PackageId,
                ContentHash: manifest.ContentHash,
                CheckedAt: DateTimeOffset.UtcNow,
                IssueCode: PolicyPackageIssueCode.PackageMismatch,
                Message: $"The Cerbos policy store holds {storePolicyIds.Count} polic(ies) but this package declares {expectedPolicyCount}.",
                Warnings: ["Re-publish the policy package; the previous publish appears to have been partial."],
                ObservedRevision: observedRevision);
        }

        var warnings = new List<string>(2);

        if (storePolicyIds.Count > expectedPolicyCount)
        {
            warnings.Add(
                $"The store holds {storePolicyIds.Count - expectedPolicyCount} polic(ies) this package does not declare. Confirm they were added deliberately.");
        }

        if (observedRevision is null)
        {
            warnings.Add(
                "The store revision could not be read, so an in-place edit to a policy already in the store would not be visible here.");
        }

        return new PolicyPackageStatusResult(
            PackageId: manifest.PackageId,
            ContentHash: manifest.ContentHash,
            CheckedAt: DateTimeOffset.UtcNow,
            IssueCode: PolicyPackageIssueCode.None,
            Message: $"The Cerbos policy store holds {storePolicyIds.Count} polic(ies), covering all {expectedPolicyCount} declared by this package.",
            Warnings: warnings,
            ObservedRevision: observedRevision);
    }

    /// <summary>
    /// Reads the content hash of every stored policy and folds them into one revision token.
    /// Returns <c>null</c> when the store cannot be read; a caller must treat that as uncertainty rather
    /// than as an unchanged store.
    /// </summary>
    private async Task<string?> ObserveStoreRevisionAsync(
        AdminApiTarget target,
        IReadOnlyList<string> storePolicyIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var api = CreateAdminApi(target.PrimaryEndpoint);
            var auth = GetBasicAuthHeader(target);

            using var response = await api.GetPoliciesAsync(auth, storePolicyIds, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content?.Policies is not { } policies)
                return null;

            return CerbosStoreRevision.Compute(
                policies.Select(policy => (policy.Metadata?.StoreIdentifier, policy.Metadata?.Hash)));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Could not read Cerbos store policy hashes to compute an observed revision. FailureType={FailureType}",
                ex.GetType().Name);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>?> ListStorePolicyIdsAsync(
        AdminApiTarget target,
        CancellationToken cancellationToken)
    {
        var api = CreateAdminApi(target.PrimaryEndpoint);
        var auth = GetBasicAuthHeader(target);

        var response = await api.ListPoliciesAsync(auth, includeDisabled: "true", cancellationToken);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CerbosAdminApiException(
                    PolicyPackageIssueCode.AdminApiUnavailable,
                    $"Cerbos Admin API returned {(int)response.StatusCode} when listing policies.");
            }

            return response.Content?.PolicyIds;
        }
    }

    /// <inheritdoc />
    public async Task<PolicyPackageArchive> ExportArchiveAsync(CancellationToken cancellationToken = default)
    {
        var packageRoot = ResolvePolicyRoot();
        var manifest = await BuildManifestAsync(cancellationToken);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var artifact in manifest.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artifactPath = ResolveArtifactPath(packageRoot, artifact.LogicalId);
                await AddFileEntryAsync(archive, artifact.LogicalId, artifactPath, cancellationToken);
            }

            await AddTextEntryAsync(
                archive,
                ArchiveManifestFileName,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);

            await AddTextEntryAsync(
                archive,
                ArchiveInstructionsFileName,
                BuildArchiveInstructions(manifest),
                cancellationToken);
        }

        return new PolicyPackageArchive(
            FileName: CreateArchiveFileName(manifest),
            ContentType: ArchiveContentType,
            Content: memoryStream.ToArray(),
            Manifest: manifest);
    }

    private async Task<AdminApiTargetResolution> ResolveAdminApiTargetAsync(
        PolicyPackageAdminCredentials? oneTimeCredentials,
        CancellationToken cancellationToken)
    {
        var configuration = await _cerbosConfigResolver.ResolveAsync(cancellationToken);
        if (configuration is { IsInstanceDefault: false, Mode: CerbosMode.CustomEndpoint })
        {
            if (string.IsNullOrWhiteSpace(configuration.AdminEndpoint))
            {
                return AdminApiTargetResolution.Failed(
                    "BYO Cerbos Admin API endpoint must be configured before publishing.");
            }

            var username = oneTimeCredentials?.Username ?? configuration.AdminUsername;
            var password = oneTimeCredentials?.Password ?? configuration.AdminPassword;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return AdminApiTargetResolution.Failed(
                    "BYO Cerbos Admin API endpoint requires both admin username and admin password before publishing.");
            }

            if (!_adminEndpointValidator.TryNormalize(configuration.AdminEndpoint, isByo: true, out var endpoint, out var warning))
                return AdminApiTargetResolution.Failed(warning);

            return AdminApiTargetResolution.Success(new AdminApiTarget(
                Endpoints: [endpoint],
                AdminUsername: username,
                AdminPassword: password,
                Source: AdminApiTargetSource.Byo));
        }

        return ResolveInstanceAdminApiTarget(oneTimeCredentials);
    }

    private AdminApiTargetResolution ResolveInstanceAdminApiTarget(
        PolicyPackageAdminCredentials? oneTimeCredentials = null)
    {
        if (_adminApiSettings.Endpoints.Count == 0)
            return AdminApiTargetResolution.Failed("Configure Cerbos:AdminApi:Endpoints before publishing the policy package.");

        var endpoints = new List<Uri>();
        foreach (var configuredEndpoint in _adminApiSettings.Endpoints)
        {
            if (!_adminEndpointValidator.TryNormalize(configuredEndpoint, isByo: false, out var endpoint, out var warning))
                return AdminApiTargetResolution.Failed(warning);

            endpoints.Add(endpoint);
        }

        var username = oneTimeCredentials?.Username ?? _adminApiSettings.AdminUsername;
        var password = oneTimeCredentials?.Password ?? _adminApiSettings.AdminPassword;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return AdminApiTargetResolution.Failed(
                "Configure Cerbos Admin API credentials or provide a complete one-time credential pair before publishing.");
        }

        return AdminApiTargetResolution.Success(new AdminApiTarget(
            Endpoints: endpoints,
            AdminUsername: username,
            AdminPassword: password,
            Source: AdminApiTargetSource.Instance));
    }

    private string ResolvePolicyRoot()
    {
        var packageRoot = Path.IsPathRooted(_options.PoliciesPath)
            ? _options.PoliciesPath
            : Path.GetFullPath(_options.PoliciesPath, Directory.GetCurrentDirectory());

        if (!Directory.Exists(packageRoot)
            && IsDefaultRelativePolicyPath(_options.PoliciesPath)
            && TryResolveRepositoryPolicyRoot(Directory.GetCurrentDirectory(), out var repositoryPolicyRoot))
        {
            packageRoot = repositoryPolicyRoot;
        }

        if (!Directory.Exists(packageRoot))
        {
            _logger.LogWarning("Cerbos policy package is unavailable because the configured policy directory does not exist.");
            throw CreatePackageUnavailableException();
        }

        return packageRoot;
    }

    private static bool IsDefaultRelativePolicyPath(string policiesPath) =>
        policiesPath.Equals("cerbos/policies", StringComparison.Ordinal)
        || policiesPath.Equals("cerbos\\policies", StringComparison.Ordinal);

    private static bool TryResolveRepositoryPolicyRoot(string startDirectory, out string policyRoot)
    {
        var current = new DirectoryInfo(startDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "cerbos", "policies");
            if ((File.Exists(Path.Combine(current.FullName, "Explore.slnx")) || File.Exists(Path.Combine(current.FullName, "Explore.sln"))) && Directory.Exists(candidate))
            {
                policyRoot = candidate;
                return true;
            }

            current = current.Parent;
        }

        policyRoot = string.Empty;
        return false;
    }

    private PolicyPackageManifest CreateUnavailableManifest() =>
        new(
            PackageId: _options.PackageId,
            Version: "unavailable",
            ContentHash: "unavailable",
            GeneratedAt: DateTimeOffset.UtcNow,
            Artifacts: []);

    private static PolicyPackageUnavailableException CreatePackageUnavailableException() =>
        new("Authorization policy package assets are not available in this deployment. Configure Cerbos:PolicyPackagePath to a bundled or mounted policy directory.");

    private static IEnumerable<string> EnumeratePolicyFiles(string packageRoot)
    {
        return PolicyFilePatterns
            .SelectMany(pattern => Directory.EnumerateFiles(packageRoot, pattern, SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal);
    }

    private static async Task<PolicyPackageArtifact> BuildArtifactAsync(
        string packageRoot,
        string filePath,
        PolicyArtifactKind kind,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var logicalId = Path.GetRelativePath(packageRoot, filePath).Replace(Path.DirectorySeparatorChar, '/');
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return new PolicyPackageArtifact(
            LogicalId: logicalId,
            Kind: kind,
            Sha256: sha256,
            SizeInBytes: bytes.LongLength,
            Metadata: new Dictionary<string, string>
            {
                ["extension"] = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant()
            });
    }

    private async Task<IReadOnlyList<CerbosSchemaDefinition>> BuildSchemaDefinitionsAsync(
        string packageRoot,
        PolicyPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var schemas = new List<CerbosSchemaDefinition>();
        foreach (var artifact in manifest.Artifacts.Where(a => a.Kind == PolicyArtifactKind.Schema))
        {
            var path = ResolveArtifactPath(packageRoot, artifact.LogicalId);
            var schemaBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            schemas.Add(new CerbosSchemaDefinition
            {
                Id = artifact.LogicalId.StartsWith($"{SchemaDirectoryName}/", StringComparison.Ordinal)
                    ? artifact.LogicalId[(SchemaDirectoryName.Length + 1)..]
                    : artifact.LogicalId,
                Definition = Convert.ToBase64String(schemaBytes)
            });
        }

        return schemas;
    }

    private async Task<IReadOnlyList<object>> BuildPolicyDocumentsAsync(
        string packageRoot,
        PolicyPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var policies = new List<object>();
        foreach (var artifact in manifest.Artifacts.Where(a => a.Kind == PolicyArtifactKind.Policy))
        {
            var path = ResolveArtifactPath(packageRoot, artifact.LogicalId);
            policies.Add(await ReadPolicyDocumentAsync(path, cancellationToken));
        }

        return policies;
    }

    private static async Task<object> ReadPolicyDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var extension = Path.GetExtension(path);
        var json = extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? content
            : ConvertYamlToJson(content);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ConvertYamlToJson(string yaml)
    {
        var yamlObject = YamlDeserializer.Deserialize(new StringReader(yaml));
        return JsonCompatibleYamlSerializer.Serialize(yamlObject);
    }

    private ICerbosAdminApi CreateAdminApi(Uri endpoint)
    {
        var client = _httpClientFactory.CreateClient("CerbosAdminClient");
        if (client.BaseAddress is null)
        {
            client.BaseAddress = endpoint;
        }
        else if (client.BaseAddress != endpoint)
        {
            throw new InvalidOperationException("Cerbos Admin API HTTP client was reused for multiple endpoints.");
        }

        return RestService.For<ICerbosAdminApi>(client, new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(AdminApiJsonOptions),
            ExceptionFactory = _ => ValueTask.FromResult<Exception?>(null)
        });
    }

    private async Task PushSchemasAsync(
        AdminApiTarget target,
        IReadOnlyList<CerbosSchemaDefinition> schemas,
        CancellationToken cancellationToken)
    {
        var api = CreateAdminApi(target.PrimaryEndpoint);
        var auth = GetBasicAuthHeader(target);

        using var response = await SendAdminRequestAsync(
            async () => await api.PushSchemasAsync(auth, new CerbosSchemaBatchRequest { Schemas = schemas }, cancellationToken),
            "schema", target.PrimaryEndpoint, cancellationToken);

        await EnsureSuccessAsync(response, "schema", target.PrimaryEndpoint, cancellationToken);
    }

    private async Task PushPoliciesAsync(
        AdminApiTarget target,
        IReadOnlyList<object> policies,
        CancellationToken cancellationToken)
    {
        var api = CreateAdminApi(target.PrimaryEndpoint);
        var auth = GetBasicAuthHeader(target);

        using var response = await SendAdminRequestAsync(
            async () => await api.PushPoliciesAsync(auth, new CerbosPolicyBatchRequest { Policies = policies }, cancellationToken),
            "policy", target.PrimaryEndpoint, cancellationToken);

        await EnsureSuccessAsync(response, "policy", target.PrimaryEndpoint, cancellationToken);
    }

    private async Task<bool> ReloadAllInstancesAsync(AdminApiTarget target, CancellationToken cancellationToken)
    {
        var tasks = target.Endpoints.Select(endpoint => ReloadInstanceAsync(endpoint, target, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.All(succeeded => succeeded);
    }

    private async Task<bool> ReloadInstanceAsync(Uri endpoint, AdminApiTarget target, CancellationToken cancellationToken)
    {
        var api = CreateAdminApi(endpoint);
        var auth = GetBasicAuthHeader(target);

        IApiResponse response;
        try
        {
            response = await api.ReloadInstanceAsync(auth, wait: "true", cancellationToken);
            if (response is null)
                return false;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Cerbos package reload failed at {Endpoint}: {StatusCode}",
                CerbosAdminEndpointValidator.ToSafeEndpoint(endpoint),
                ex.StatusCode);
            return false;
        }
        catch (Exception ex) when (IsAdminApiTransportFailure(ex, cancellationToken))
        {
            _logger.LogWarning(
                "Cerbos package reload could not reach {Endpoint}. TransportFailure={TransportFailure}",
                CerbosAdminEndpointValidator.ToSafeEndpoint(endpoint),
                ex.GetType().Name);
            return false;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return true;

            _logger.LogWarning(
                "Cerbos package reload failed at {Endpoint}: {StatusCode}",
                CerbosAdminEndpointValidator.ToSafeEndpoint(endpoint),
                response.StatusCode);
            return false;
        }
    }

    private async Task<IApiResponse> SendAdminRequestAsync(
        Func<Task<IApiResponse>> requestAction,
        string artifactKind,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await requestAction()
                ?? throw new CerbosAdminApiException(
                    PolicyPackageIssueCode.AdminApiUnavailable,
                    $"Cerbos Admin API {artifactKind} upload failed before a response was received.");
            if (response.HasRequestError(out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new CerbosAdminApiException(
                    PolicyPackageIssueCode.AdminApiUnavailable,
                    $"Cerbos Admin API {artifactKind} upload failed before a response was received.");
            }

            return response;
        }
        catch (ApiException ex)
        {
            var issueCode = ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? PolicyPackageIssueCode.AdminApiAuthenticationFailed
                : PolicyPackageIssueCode.AdminApiUnavailable;

            _logger.LogError(
                "Cerbos Admin API {ArtifactKind} upload failed at {Endpoint}: {StatusCode}",
                artifactKind,
                CerbosAdminEndpointValidator.ToSafeEndpoint(endpoint),
                ex.StatusCode);
            throw new CerbosAdminApiException(
                issueCode,
                $"Cerbos Admin API {artifactKind} upload failed: {ex.StatusCode}");
        }
        catch (Exception ex) when (IsAdminApiTransportFailure(ex, cancellationToken))
        {
            _logger.LogError(
                "Cerbos Admin API {ArtifactKind} upload could not reach {Endpoint}. TransportFailure={TransportFailure}",
                artifactKind,
                CerbosAdminEndpointValidator.ToSafeEndpoint(endpoint),
                ex.GetType().Name);
            throw new CerbosAdminApiException(
                PolicyPackageIssueCode.AdminApiUnavailable,
                $"Cerbos Admin API {artifactKind} upload failed before a response was received.");
        }
    }

    private async Task EnsureSuccessAsync(
        IApiResponse response,
        string artifactKind,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var issueCode = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            ? PolicyPackageIssueCode.AdminApiAuthenticationFailed
            : PolicyPackageIssueCode.AdminApiUnavailable;

        _logger.LogError(
            "Cerbos Admin API {ArtifactKind} upload failed at {Endpoint}: {StatusCode}",
            artifactKind,
            CerbosAdminEndpointValidator.ToSafeEndpoint(endpoint),
            response.StatusCode);

        throw new CerbosAdminApiException(
            issueCode,
            $"Cerbos Admin API {artifactKind} upload failed: {response.StatusCode}");
    }

    private static string GetBasicAuthHeader(AdminApiTarget target)
    {
        if (string.IsNullOrEmpty(target.AdminUsername))
            return null!;

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{target.AdminUsername}:{target.AdminPassword}"));
        return $"Basic {credentials}";
    }

    private int GetPolicyBatchSize()
    {
        return _options.MaxPoliciesPerRequest > 0 ? _options.MaxPoliciesPerRequest : 100;
    }

    private static string ResolveArtifactPath(string packageRoot, string logicalId)
    {
        var pathSegments = logicalId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine([packageRoot, .. pathSegments]);
    }

    private static async Task AddFileEntryAsync(
        ZipArchive archive,
        string entryName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var fileStream = File.OpenRead(filePath);
        await fileStream.CopyToAsync(entryStream, cancellationToken);
    }

    private static async Task AddTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static string BuildArchiveInstructions(PolicyPackageManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Authorization Policy Package Manual Install");
        builder.AppendLine();
        builder.AppendLine($"Package: `{manifest.PackageId}`");
        builder.AppendLine($"Content hash: `{manifest.ContentHash}`");
        builder.AppendLine();
        builder.AppendLine("This archive contains the bundled authorization policies and schemas used by the application.");
        builder.AppendLine("Use it when automated Admin API package sync is unavailable or credentials are not configured.");
        builder.AppendLine();
        builder.AppendLine("1. Extract this ZIP to a temporary directory.");
        builder.AppendLine("2. Review `manifest.json` and the policy/schema files before applying them.");
        builder.AppendLine("3. Recommended for Docker Compose: mount the extracted policies directory read-only and run:");
        builder.AppendLine("   docker compose --profile authz run --rm cerbos-policy-sync");
        builder.AppendLine("4. Manual fallback from the extracted directory:");
        builder.AppendLine("   cerbosctl put policy --recursive .");
        builder.AppendLine("   cerbosctl put schema --recursive _schemas");
        builder.AppendLine("   cerbosctl store reload --wait");
        builder.AppendLine("5. Restart Cerbos if your storage driver does not support Admin API reloads.");
        builder.AppendLine();
        builder.AppendLine("The archive intentionally does not include Admin API credentials or runtime secrets.");
        return builder.ToString();
    }

    private static string CreateArchiveFileName(PolicyPackageManifest manifest)
    {
        var safePackageId = new string(manifest.PackageId
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')
            .ToArray());
        var shortHash = manifest.ContentHash.Length >= 12 ? manifest.ContentHash[..12] : manifest.ContentHash;
        return $"{safePackageId}-{shortHash}.zip";
    }

    private static Uri BuildAdminUrl(Uri endpoint, string path)
    {
        return new Uri(string.Concat(endpoint.AbsoluteUri.TrimEnd('/'), path));
    }

    private static string ToSafeWarning(Exception exception)
    {
        return exception is InvalidOperationException ? exception.Message : "Cerbos Admin API publish failed.";
    }

    private static bool IsAdminApiTransportFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;
    }

    private static string ComputeManifestHash(IReadOnlyList<PolicyPackageArtifact> artifacts)
    {
        var builder = new StringBuilder();
        foreach (var artifact in artifacts)
        {
            builder
                .Append(artifact.Kind)
                .Append('|')
                .Append(artifact.LogicalId)
                .Append('|')
                .Append(artifact.Sha256)
                .Append('|')
                .Append(artifact.SizeInBytes)
                .Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private sealed record AdminApiTarget(
        IReadOnlyList<Uri> Endpoints,
        string? AdminUsername,
        string? AdminPassword,
        AdminApiTargetSource Source)
    {
        public Uri PrimaryEndpoint => Endpoints[0];

        public override string ToString() => $"{nameof(AdminApiTarget)} {{ Source = {Source} }}";
    }

    private enum AdminApiTargetSource
    {
        Instance,
        Byo
    }

    private sealed record AdminApiTargetResolution(
        bool Succeeded,
        AdminApiTarget? Target,
        IReadOnlyList<string> Warnings)
    {
        public static AdminApiTargetResolution Success(AdminApiTarget target) => new(true, target, []);

        public static AdminApiTargetResolution Failed(string warning) => new(false, null, [warning]);
    }

    private sealed class CerbosAdminApiException(PolicyPackageIssueCode issueCode, string message)
        : InvalidOperationException(message)
    {
        public PolicyPackageIssueCode IssueCode { get; } = issueCode;
    }
}
