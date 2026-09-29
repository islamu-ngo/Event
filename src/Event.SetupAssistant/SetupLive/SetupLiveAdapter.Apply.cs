namespace ISLAMU.Event.SetupAssistant.SetupLive;

using System.Collections.Immutable;
using System.Security.Cryptography;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Generated = Explore.Blazor.Client.Clients;
using Wire = ISLAMU.Wire.Contracts.SetupLive;

public sealed record SetupLiveConfigurationSession(
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    ImmutableArray<string> AvailableSectionKeys);

public sealed record SetupLiveConfigurationDifference(
    string SectionKey,
    Generated.ConfigurationImportPreviewCategory Category,
    string ReasonCode,
    string? SourceMappingIdentity,
    string? TargetMappingIdentity);

public sealed record SetupLiveConfigurationPreview(
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    ImmutableArray<SetupLiveConfigurationDifference> Items);

public sealed class SetupLiveConfigurationConflictException()
    : InvalidOperationException("The configuration import conflicts with current server state.");

public sealed partial class SetupLiveAdapter
{
    private const string PreviewConfigurationRelation = "preview-configuration-import";
    private const string ApplyConfigurationRelation = "apply-configuration-import";
    private Guid _configurationSessionId;
    private string? _configurationToken;
    private DateTimeOffset _configurationExpiresAt;
    private Generated.ConfigurationImportPreviewRequest? _configurationPreview;
    private readonly Dictionary<string, (string Href, string? Method)> _configurationLinks =
        new(StringComparer.Ordinal);

    public bool CanApplyConfiguration
    {
        get
        {
            _gate.Wait();
            try
            {
                return IsAuthorityAvailable()
                    && _configurationToken is not null
                    && _configurationExpiresAt > _timeProvider.GetUtcNow()
                    && _configurationPreview is not null
                    && HasConfigurationLink(
                        _configurationLinks, ApplyConfigurationRelation,
                        $"{ConfigurationPath}/{_configurationSessionId:D}/apply");
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public Task<SetupLiveConfigurationSession> UploadConfigurationAsync(
        Stream artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            RequireConfigurationLink(_enrollmentAffordances,
                Wire.SetupLiveHalRelations.CreateConfigurationImportSession, ConfigurationPath);
            ClearConfigurationImport();
            using var bounded = new MemoryStream();
            byte[] buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    int count = await artifact.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                        break;
                    if (bounded.Length + count > ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes)
                        throw new ArgumentException("Configuration artifact exceeds the supported size.", nameof(artifact));
                    await bounded.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
                if (bounded.Length == 0)
                    throw new ArgumentException("Configuration artifact is empty.", nameof(artifact));
                bounded.Position = 0;
                var result = await SendAuthenticatedAsync(
                    () => _client.CreateSetupConfigurationImportSessionAsync(
                        TenantId, _enrollmentId, capability, bounded,
                        cancellationToken: cancellationToken), cancellationToken).ConfigureAwait(false);
                if (result.SessionId == Guid.Empty
                    || result.TargetTenantId != TenantId
                    || result.TargetScope != Generated.ConfigurationImportScope.Tenant
                    || result.State != Generated.ConfigurationImportSessionState.Uploaded
                    || result.ExpiresAt <= _timeProvider.GetUtcNow()
                    || !Wire.SetupEnrollmentCapability.TryCreate(result.AccessToken, out _)
                    || result.AvailableSectionKeys is null)
                    throw ContractViolation();

                _configurationSessionId = result.SessionId;
                _configurationToken = result.AccessToken;
                _configurationExpiresAt = result.ExpiresAt;
                CaptureConfigurationLinks(result._links, allowApply: false);
                return new SetupLiveConfigurationSession(result.SessionId,
                    result.ExpiresAt, [.. result.AvailableSectionKeys]);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
                CryptographicOperations.ZeroMemory(bounded.GetBuffer());
            }
        }, cancellationToken);
    }

    public Task<SetupLiveConfigurationPreview> PreviewConfigurationAsync(
        Generated.ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = new Generated.ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = request.SelectedSectionKeys.ToArray(),
            Mappings = new Dictionary<string, string>(request.Mappings, StringComparer.Ordinal),
            ApplyMode = request.ApplyMode,
            GrantedApprovalCodes = request.GrantedApprovalCodes.ToArray()
        };
        return ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            string token = RequireConfigurationSession();
            RequireConfigurationLink(_configurationLinks, PreviewConfigurationRelation,
                $"{ConfigurationPath}/{_configurationSessionId:D}/preview");
            _configurationPreview = null;
            _configurationLinks.Remove(ApplyConfigurationRelation);
            var result = await SendAuthenticatedAsync(
                () => _client.PreviewSetupConfigurationImportSessionAsync(
                    TenantId, _enrollmentId, _configurationSessionId, capability, token, snapshot,
                    cancellationToken: cancellationToken), cancellationToken).ConfigureAwait(false);
            if (result.SessionId != _configurationSessionId
                || result.TargetTenantId != TenantId
                || result.TargetScope != Generated.ConfigurationImportScope.Tenant
                || result.State != Generated.ConfigurationImportSessionState.PreviewReady
                || result.ExpiresAt <= _timeProvider.GetUtcNow()
                || result.Items is null)
                throw ContractViolation();
            _configurationExpiresAt = result.ExpiresAt;
            _configurationPreview = snapshot;
            CaptureConfigurationLinks(result._links, allowApply: true);
            return new SetupLiveConfigurationPreview(result.SessionId, result.ExpiresAt,
                [.. result.Items.Select(item => new SetupLiveConfigurationDifference(
                    item.SectionKey, item.Category, item.ReasonCode,
                    item.SourceMappingIdentity, item.TargetMappingIdentity))]);
        }, cancellationToken);
    }

    public Task<Generated.HalResourceOfConfigurationImportOperationResult> ApplyConfigurationAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteLockedAsync(async () =>
        {
            string capability = RequireAuthority();
            string token = RequireConfigurationSession();
            RequireConfigurationLink(_configurationLinks, ApplyConfigurationRelation,
                $"{ConfigurationPath}/{_configurationSessionId:D}/apply");
            var preview = _configurationPreview
                ?? throw new SetupLiveAffordanceUnavailableException(ApplyConfigurationRelation);
            Guid sessionId = _configurationSessionId;
            ClearConfigurationImport();
            return await SendAuthenticatedAsync(async () =>
            {
                try
                {
                    var result = await _client.ApplySetupConfigurationImportSessionAsync(
                        TenantId, _enrollmentId, sessionId, capability, token, preview,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (result.SessionId != sessionId
                        || result.TargetTenantId != TenantId
                        || result.TargetScope != Generated.ConfigurationImportScope.Tenant
                        || result.OperationId == Guid.Empty)
                        throw ContractViolation();
                    return result;
                }
                catch (Generated.ApiException exception) when (exception.StatusCode == 409)
                {
                    throw new SetupLiveConfigurationConflictException();
                }
            }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private string ConfigurationPath =>
        $"/api/tenants/{TenantId:D}/setup/enrollments/{_enrollmentId:D}/configuration-import/sessions";

    private string RequireConfigurationSession()
    {
        if (_configurationToken is null
            || _configurationExpiresAt <= _timeProvider.GetUtcNow())
        {
            ClearConfigurationImport();
            throw new SetupLiveAffordanceUnavailableException(PreviewConfigurationRelation);
        }
        return _configurationToken;
    }

    private bool HasConfigurationLink(
        Dictionary<string, (string Href, string? Method)> links,
        string relation,
        string expectedPath)
    {
        if (!links.TryGetValue(relation, out var link)
            || !MethodMatches(link.Method, HttpMethod.Post)
            || !Uri.TryCreate(TargetBaseAddress, link.Href, out Uri? target))
            return false;
        return target.Scheme == Uri.UriSchemeHttps
            && target.Authority == TargetBaseAddress.Authority
            && target.UserInfo.Length == 0
            && target.Query.Length == 0
            && target.Fragment.Length == 0
            && target == new Uri(TargetBaseAddress, expectedPath.TrimStart('/'));
    }

    private void RequireConfigurationLink(
        Dictionary<string, (string Href, string? Method)> links,
        string relation,
        string expectedPath)
    {
        if (!HasConfigurationLink(links, relation, expectedPath))
            throw new SetupLiveAffordanceUnavailableException(relation);
    }

    private void CaptureConfigurationLinks(
        IDictionary<string, Generated.HalLink>? links, bool allowApply)
    {
        _configurationLinks.Clear();
        if (links is null)
            return;
        foreach (var (relation, link) in links)
        {
            if (link is not null && !string.IsNullOrWhiteSpace(link.Href)
                && (relation == PreviewConfigurationRelation
                    || allowApply && relation == ApplyConfigurationRelation))
                _configurationLinks[relation] = (link.Href, link.Method);
        }
    }

    private void ClearConfigurationImport()
    {
        _configurationSessionId = Guid.Empty;
        _configurationToken = null;
        _configurationExpiresAt = default;
        _configurationPreview = null;
        _configurationLinks.Clear();
    }
}
