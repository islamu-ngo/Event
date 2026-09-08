using System.Net;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationProviderConnection : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationProviderApprovedOrigin> _approvedOrigins = [];

    private RegistrationProviderConnection() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public string Name { get; private set; } = string.Empty;
    public int ProviderKindId { get; private set; }
    public int DeploymentKindId { get; private set; }
    public string ProviderCode { get; private set; } = string.Empty;
    public string ProviderDeploymentCode { get; private set; } = string.Empty;
    public string ApiVersion { get; private set; } = string.Empty;
    public string AdapterPolicyVersion { get; private set; } = string.Empty;
    public string ConformanceEvidenceRevision { get; private set; } = string.Empty;
    public string ManagementApiBaseUrl { get; private set; } = string.Empty;
    public string PublicBaseUrl { get; private set; } = string.Empty;
    public string ProviderWorkspaceId { get; private set; } = string.Empty;
    public string GrantedOAuthScopes { get; private set; } = string.Empty;
    public string ProviderIdentity { get; private set; } = string.Empty;
    public string PubSubConfigurationReference { get; private set; } = string.Empty;
    public DateTime? LastCredentialRefreshAt { get; private set; }
    public DateTime? LastAccessValidatedAt { get; private set; }
    public Guid? ApiTokenSecretBindingId { get; private set; }
    public Guid? WebhookSecretBindingId { get; private set; }
    public IReadOnlyList<RegistrationProviderApprovedOrigin> ApprovedOrigins => _approvedOrigins;
    public string TupleKey => string.Join('|', ProviderCode, ProviderDeploymentCode, ApiVersion, AdapterPolicyVersion, ConformanceEvidenceRevision);
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RegistrationProviderConnection Create(Guid tenantId, string name, RegistrationProviderKindEnum providerKind,
        RegistrationProviderDeploymentKindEnum deploymentKind, string providerCode, string providerDeploymentCode, string apiVersion,
        string adapterPolicyVersion, string conformanceEvidenceRevision, string managementApiBaseUrl, string publicBaseUrl,
        string providerWorkspaceId, Guid? apiTokenSecretBindingId, Guid? webhookSecretBindingId, DateTime createdAt) =>
        Create(Guid.CreateVersion7(), tenantId, name, providerKind, deploymentKind, providerCode, providerDeploymentCode,
            apiVersion, adapterPolicyVersion, conformanceEvidenceRevision, managementApiBaseUrl, publicBaseUrl,
            providerWorkspaceId, apiTokenSecretBindingId, webhookSecretBindingId, createdAt);

    public static RegistrationProviderConnection Create(Guid id, Guid tenantId, string name, RegistrationProviderKindEnum providerKind,
        RegistrationProviderDeploymentKindEnum deploymentKind, string providerCode, string providerDeploymentCode, string apiVersion,
        string adapterPolicyVersion, string conformanceEvidenceRevision, string managementApiBaseUrl, string publicBaseUrl,
        string providerWorkspaceId, Guid? apiTokenSecretBindingId, Guid? webhookSecretBindingId, DateTime createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || apiTokenSecretBindingId == Guid.Empty || webhookSecretBindingId == Guid.Empty ||
            !Enum.IsDefined(providerKind) || !Enum.IsDefined(deploymentKind))
        {
            throw new ArgumentException("Provider connection identities and lookup values must be valid.");
        }

        return new RegistrationProviderConnection
        {
            Id = id,
            TenantId = tenantId,
            Name = NormalizeText(name, nameof(name), 120),
            ProviderKindId = (int)providerKind,
            DeploymentKindId = (int)deploymentKind,
            ProviderCode = NormalizeText(providerCode, nameof(providerCode), 100),
            ProviderDeploymentCode = NormalizeText(providerDeploymentCode, nameof(providerDeploymentCode), 100),
            ApiVersion = NormalizeText(apiVersion, nameof(apiVersion), 100),
            AdapterPolicyVersion = NormalizeText(adapterPolicyVersion, nameof(adapterPolicyVersion), 100),
            ConformanceEvidenceRevision = NormalizeText(conformanceEvidenceRevision, nameof(conformanceEvidenceRevision), 120),
            ManagementApiBaseUrl = NormalizeHttpsUrl(managementApiBaseUrl, nameof(managementApiBaseUrl)),
            PublicBaseUrl = NormalizeHttpsUrl(publicBaseUrl, nameof(publicBaseUrl)),
            ProviderWorkspaceId = NormalizeText(providerWorkspaceId, nameof(providerWorkspaceId), 200),
            ApiTokenSecretBindingId = apiTokenSecretBindingId,
            WebhookSecretBindingId = webhookSecretBindingId,
            CreatedAt = EnsureUtc(createdAt, nameof(createdAt))
        };
    }

    public void Update(string name, RegistrationProviderKindEnum providerKind, RegistrationProviderDeploymentKindEnum deploymentKind,
        string providerCode, string providerDeploymentCode, string apiVersion, string adapterPolicyVersion,
        string conformanceEvidenceRevision, string managementApiBaseUrl, string publicBaseUrl, string providerWorkspaceId,
        Guid? apiTokenSecretBindingId, Guid? webhookSecretBindingId)
    {
        if (apiTokenSecretBindingId == Guid.Empty || webhookSecretBindingId == Guid.Empty || !Enum.IsDefined(providerKind) || !Enum.IsDefined(deploymentKind))
        {
            throw new ArgumentException("Provider connection lookup and secret references must be valid.");
        }

        Name = NormalizeText(name, nameof(name), 120);
        ProviderKindId = (int)providerKind;
        DeploymentKindId = (int)deploymentKind;
        ProviderCode = NormalizeText(providerCode, nameof(providerCode), 100);
        ProviderDeploymentCode = NormalizeText(providerDeploymentCode, nameof(providerDeploymentCode), 100);
        ApiVersion = NormalizeText(apiVersion, nameof(apiVersion), 100);
        AdapterPolicyVersion = NormalizeText(adapterPolicyVersion, nameof(adapterPolicyVersion), 100);
        ConformanceEvidenceRevision = NormalizeText(conformanceEvidenceRevision, nameof(conformanceEvidenceRevision), 120);
        ManagementApiBaseUrl = NormalizeHttpsUrl(managementApiBaseUrl, nameof(managementApiBaseUrl));
        PublicBaseUrl = NormalizeHttpsUrl(publicBaseUrl, nameof(publicBaseUrl));
        ProviderWorkspaceId = NormalizeText(providerWorkspaceId, nameof(providerWorkspaceId), 200);
        ApiTokenSecretBindingId = apiTokenSecretBindingId;
        WebhookSecretBindingId = webhookSecretBindingId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateOAuthMetadata(string grantedOAuthScopes, string providerIdentity, string pubSubConfigurationReference)
    {
        GrantedOAuthScopes = NormalizeScopes(grantedOAuthScopes);
        ProviderIdentity = NormalizeOptionalText(providerIdentity, nameof(providerIdentity), 200);
        PubSubConfigurationReference = NormalizeOptionalText(pubSubConfigurationReference, nameof(pubSubConfigurationReference), 300);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RecordCredentialRefresh(DateTime refreshedAt)
    {
        LastCredentialRefreshAt = EnsureUtc(refreshedAt, nameof(refreshedAt));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void RecordAccessValidated(DateTime validatedAt)
    {
        LastAccessValidatedAt = EnsureUtc(validatedAt, nameof(validatedAt));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void ReplaceApprovedOrigins(IEnumerable<string> origins, DateTime observedAt)
    {
        EnsureUtc(observedAt, nameof(observedAt));
        ArgumentNullException.ThrowIfNull(origins);
        string[] normalized = [.. origins.Select(RegistrationProviderApprovedOrigin.NormalizeOrigin).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (normalized.Length > 20)
        {
            throw new ArgumentException("At most 20 approved origins are allowed.", nameof(origins));
        }

        foreach (RegistrationProviderApprovedOrigin existing in _approvedOrigins.Where(origin => !normalized.Contains(origin.Origin, StringComparer.OrdinalIgnoreCase)))
        {
            existing.IsDeleted = true;
            existing.DeletedAt = observedAt;
        }

        foreach (string origin in normalized.Where(origin => _approvedOrigins.All(existing => !string.Equals(existing.Origin, origin, StringComparison.OrdinalIgnoreCase))))
        {
            _approvedOrigins.Add(RegistrationProviderApprovedOrigin.Create(this, origin, observedAt));
        }

        foreach (RegistrationProviderApprovedOrigin existing in _approvedOrigins.Where(origin => normalized.Contains(origin.Origin, StringComparer.OrdinalIgnoreCase)))
        {
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.DeletedBy = null;
        }

        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Remove(DateTime removedAt)
    {
        IsDeleted = true;
        DeletedAt = EnsureUtc(removedAt, nameof(removedAt));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public bool IsOriginApproved(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && !RegistrationProviderApprovedOrigin.HostIsBlocked(uri.Host) &&
        _approvedOrigins.Any(origin => !origin.IsDeleted && string.Equals(origin.Origin, uri.IsDefaultPort ? $"https://{uri.IdnHost.ToLowerInvariant()}" : $"https://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeText(string value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maxLength && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException($"Value must be non-blank and at most {maxLength} characters.", parameterName);
    }

    private static string NormalizeOptionalText(string value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException($"Value must be at most {maxLength} characters.", parameterName);
    }

    private static string NormalizeScopes(string value)
    {
        string normalized = NormalizeOptionalText(value, nameof(value), 1000);
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
    }

    private static string NormalizeHttpsUrl(string value, string parameterName)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > 500 || !Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            RegistrationProviderApprovedOrigin.HostIsBlocked(uri.Host))
        {
            throw new ArgumentException("Provider base URL must be an absolute HTTPS URL and cannot target local/private hosts.", parameterName);
        }

        string path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath.TrimEnd('/');
        return uri.IsDefaultPort ? $"https://{uri.IdnHost.ToLowerInvariant()}{path}" : $"https://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}{path}";
    }

    internal static DateTime EnsureUtc(DateTime value, string parameterName) =>
        value != default && value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
}

public sealed class RegistrationProviderApprovedOrigin : ITenantEntity, IAuditableEntity, ISoftDeletable
{
    private RegistrationProviderApprovedOrigin() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderConnectionId { get; private set; }
    public RegistrationProviderConnection? Connection { get; private set; }
    public string Origin { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    internal static RegistrationProviderApprovedOrigin Create(RegistrationProviderConnection connection, string origin, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new RegistrationProviderApprovedOrigin
        {
            Id = Guid.CreateVersion7(),
            TenantId = connection.TenantId,
            RegistrationProviderConnectionId = connection.Id,
            Origin = NormalizeOrigin(origin),
            CreatedAt = RegistrationProviderConnection.EnsureUtc(createdAt, nameof(createdAt))
        };
    }

    public static string NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin?.Trim(), UriKind.Absolute, out Uri? uri))
        {
            throw new ArgumentException("Approved origin must be an absolute HTTPS origin.", nameof(origin));
        }

        return NormalizeOrigin(uri);
    }

    public static string NormalizeOrigin(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || HostIsBlocked(uri.Host))
        {
            throw new ArgumentException("Approved origin must be HTTPS origin only and cannot target local/private hosts.", nameof(uri));
        }

        return uri.IsDefaultPort ? $"https://{uri.IdnHost.ToLowerInvariant()}" : $"https://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
    }

    internal static bool HostIsBlocked(string host)
    {
        string normalized = (host ?? string.Empty).TrimEnd('.').ToLowerInvariant();
        if (normalized is "" or "localhost" or "metadata.google.internal" or "169.254.169.254" || normalized.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return true;
        }

        if (!IPAddress.TryParse(normalized, out IPAddress? address)) return false;
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        byte[] bytes = address.GetAddressBytes();
        return IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            bytes.Length == 4 && (bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 || (bytes[0] == 169 && bytes[1] == 254) ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168)) ||
            bytes.Length == 16 && (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xfe) == 0xfc);
    }
}
