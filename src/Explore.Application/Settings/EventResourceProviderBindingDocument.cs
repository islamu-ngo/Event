using System.Text.Json;
using Explore.Application.Utilities;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Settings;

/// <summary>Instance-owned, non-secret deployment aliases. Aliases cannot be reassigned or removed.</summary>
public sealed record EventResourceProviderBindingDocument
{
    private IReadOnlyList<EventResourceDeploymentBinding> _deployments = [];

    public Guid Revision { get; init; }
    public IReadOnlyList<EventResourceDeploymentBinding> Deployments
    {
        get => _deployments;
        init => _deployments = value is null
            ? throw new JsonException("Deployment bindings must be an array.")
            : Array.AsReadOnly(value.ToArray());
    }

    public EventResourceProviderBindingDocument(Guid revision, IReadOnlyList<EventResourceDeploymentBinding> deployments)
    {
        Revision = revision;
        Deployments = deployments;
    }

    public static readonly string SettingKey = CerbosSettingDefinitions.ResourceDeploymentBindings.Key;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EventResourceProviderBindingDocument Parse(string? value)
    {
        if (value is null)
            return new(Guid.Empty, []);
        var document = JsonSerializer.Deserialize<EventResourceProviderBindingDocument>(value, JsonOptions)
            ?? throw new InvalidOperationException("Invalid resource provider binding document.");
        if (document.Revision.Version != 7 || document.Deployments is null)
            throw new InvalidOperationException("Invalid resource provider binding document.");
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        var deployments = new HashSet<Guid>();
        foreach (var binding in document.Deployments)
        {
            ArgumentNullException.ThrowIfNull(binding);
            binding.Validate();
            if (!deployments.Add(binding.DeploymentId) || binding.Endpoints.Any(endpoint => !endpoints.Add(endpoint)))
                throw new InvalidOperationException("Deployment identities and endpoint aliases must be unique.");
        }
        return document;
    }

    public static string NormalizeEndpoint(string endpoint)
    {
        if (!GrpcEndpointNormalizer.IsValid(endpoint))
            throw new ArgumentException("A non-secret HTTP(S) gRPC authority is required.", nameof(endpoint));
        return new Uri(GrpcEndpointNormalizer.Normalize(endpoint)).GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    public static void RejectGenericMutation(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Trim().Equals(SettingKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resource provider bindings require the coordinated instance control plane.");
    }
}

/// <summary>Scope is explicit; the empty string denotes Cerbos root/unscoped policy.</summary>
public sealed record EventResourceDeploymentBinding
{
    private IReadOnlyList<string> _endpoints = [];

    public Guid DeploymentId { get; init; }
    public IReadOnlyList<string> Endpoints
    {
        get => _endpoints;
        init => _endpoints = value is null
            ? throw new JsonException("Endpoint aliases must be an array.")
            : Array.AsReadOnly(value.ToArray());
    }
    public string Scope { get; init; }
    public string PolicyVersion { get; init; }

    public EventResourceDeploymentBinding(Guid deploymentId, IReadOnlyList<string> endpoints, string scope, string policyVersion)
    {
        DeploymentId = deploymentId;
        Endpoints = endpoints;
        Scope = scope;
        PolicyVersion = policyVersion;
    }

    public void Validate()
    {
        if (DeploymentId.Version != 7 || Endpoints is not { Count: > 0 }
            || Scope is null || string.IsNullOrWhiteSpace(PolicyVersion)
            || Scope.Length > 256 || PolicyVersion.Length > 128
            || Scope != Scope.Trim() || PolicyVersion != PolicyVersion.Trim())
            throw new ArgumentException("A deployment UUIDv7, aliases, scope and policy version are required.");
        if (Endpoints.Any(endpoint => endpoint != EventResourceProviderBindingDocument.NormalizeEndpoint(endpoint))
            || Endpoints.Distinct(StringComparer.Ordinal).Count() != Endpoints.Count)
            throw new ArgumentException("Endpoint aliases must be normalized and unique.");
    }
}
