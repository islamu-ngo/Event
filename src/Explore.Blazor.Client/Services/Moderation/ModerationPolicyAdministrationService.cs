using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.Moderation;

/// <summary>
/// Stateless generated-contract transport through the registered authenticated BFF HTTP pipeline.
/// It owns neither tenant identity nor policy authority. New operations acquire current coordinates;
/// explicit receipt retries preserve the exact original request, including historical definitions.
/// </summary>
public sealed class ModerationPolicyAdministrationService(
    IModerationCurrentPolicyClient currentClient,
    IModerationReferenceCatalogueClient referenceClient,
    HttpClient http)
{
    internal static readonly JsonSerializerOptions Json = EventApiJsonSerializerSettings.Configure(new());

    public async Task<ModerationPolicyAdministrationReadResult> ReadAsync(
        ModerationPolicyAdministrationScope scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ScopeName(scope) is not { } name)
            return new(null, ModerationPolicyAdministrationFailure.InvalidIntent);
        try
        {
            var current = await currentClient.GetModerationCurrentPolicyAsync(name, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidCurrent(current, scope))
                return new(null, ModerationPolicyAdministrationFailure.InvalidRepresentation);
            var reference = await referenceClient.GetModerationReferenceCatalogueAsync(cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (reference is null || reference.Revision < 1 || reference.Profiles is null)
                return new(null, ModerationPolicyAdministrationFailure.InvalidRepresentation);
            return new(new(current, reference,
                ActionPath(current, false) is not null, ActionPath(current, true) is not null),
                ModerationPolicyAdministrationFailure.None);
        }
        catch (ApiException exception) { return new(null, Failure(exception.StatusCode)); }
        catch (HttpRequestException) { return new(null, ModerationPolicyAdministrationFailure.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, ModerationPolicyAdministrationFailure.Unavailable); }
        catch (JsonException) { return new(null, ModerationPolicyAdministrationFailure.InvalidRepresentation); }
    }

    public Task<ModerationPolicyAdministrationOperationResult> CreateDraftAsync(
        ModerationPolicyAdministrationScope scope, ModerationPolicyProposedState proposed,
        Guid operationId, CancellationToken cancellationToken = default) =>
        CreateAsync(scope, proposed, operationId, null, cancellationToken);

    public Task<ModerationPolicyAdministrationOperationResult> CreatePreparationAsync(
        ModerationPolicyAdministrationScope scope, ModerationPolicyProposedState proposed,
        Guid operationId, DateTimeOffset validUntil, CancellationToken cancellationToken = default) =>
        CreateAsync(scope, proposed, operationId, validUntil, cancellationToken);

    private async Task<ModerationPolicyAdministrationOperationResult> CreateAsync(
        ModerationPolicyAdministrationScope scope, ModerationPolicyProposedState proposed,
        Guid operationId, DateTimeOffset? validUntil, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = ScopeName(scope);
        if (name is null || proposed is null || !Version7(operationId) || !Version7(proposed.PolicyId)
            || !Version7(proposed.PublicationId)
            || proposed.RequestedState != (validUntil.HasValue
                ? ModerationPolicyRequestedState.Active : ModerationPolicyRequestedState.Draft))
            return new(null, ModerationPolicyAdministrationFailure.InvalidIntent);
        // Freeze caller intent before awaiting a current read. Never reinterpret retained inventory.
        var intent = new ModerationPolicyTransportSnapshot<ModerationPolicyProposedState>(proposed).Value;
        try
        {
            var current = await currentClient.GetModerationCurrentPolicyAsync(name,
                prospectivePolicyId: intent.PolicyId, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidCurrent(current, scope))
                return new(null, ModerationPolicyAdministrationFailure.InvalidRepresentation);
            var path = ActionPath(current, validUntil.HasValue);
            if (path is null || http.BaseAddress is not { } baseAddress)
                return new(null, ModerationPolicyAdministrationFailure.ActionUnavailable);
            // Only current request-scope observations supply authority/selection coordinates.
            // Catalogue/inventory/profile meaning stays exactly as deliberately proposed.
            intent.PolicyId = current.PolicyId;
            intent.ExpectedRevision = current.ExpectedRevision;
            intent.Jurisdiction = current.Jurisdiction;
            var request = new ModerationPolicyPublicationRequest
            {
                OperationId = operationId,
                ExpectedHostBindingId = current.ExpectedHostBindingId,
                ExpectedAuthoritySourceId = current.ExpectedAuthoritySourceId,
                ExpectedQualificationRevision = current.ExpectedQualificationRevision,
                ExpectedWriterGeneration = current.ExpectedWriterGeneration,
                ExpectedPublicationId = current.ExpectedPublicationId,
                Proposed = intent
            };
            var body = validUntil is { } deadline
                ? JsonSerializer.Serialize(new PrepareModerationPolicyPublicationCommand
                { Request = request, ValidUntil = deadline }, Json)
                : JsonSerializer.Serialize(new PublishModerationPolicyCommand { Request = request }, Json);
            return new(new(baseAddress, path, body, operationId, current.PolicyId,
                intent.PublicationId, current.ExpectedRevision, validUntil), ModerationPolicyAdministrationFailure.None);
        }
        catch (ApiException exception) { return new(null, Failure(exception.StatusCode)); }
        catch (HttpRequestException) { return new(null, ModerationPolicyAdministrationFailure.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, ModerationPolicyAdministrationFailure.Unavailable); }
        catch (JsonException) { return new(null, ModerationPolicyAdministrationFailure.InvalidRepresentation); }
    }

    /// <summary>
    /// Sends exactly once. A transport loss may follow commit and is reported as uncertain.
    /// To recover a native receipt, the caller deliberately resubmits this exact frozen operation.
    /// No read, identity replacement, implicit retry or activation follows any response.
    /// </summary>
    public async Task<ModerationPolicyAdministrationSubmissionResult> SubmitAsync(
        ModerationPolicyAdministrationOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (http.BaseAddress != operation.BaseAddress)
            return new(null, null, ModerationPolicyAdministrationFailure.ActionUnavailable);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, operation.Path);
            request.Content = new StringContent(operation.Body, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode)
                return new(null, null, Failure((int)response.StatusCode));
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            if (operation.IsPreparation)
            {
                var candidate = await JsonSerializer.DeserializeAsync<ModerationPolicyPublicationCandidateResponse>(
                    stream, Json, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate is not { Success: true, Id: { } id, Revision: { } revision, ValidUntil: { } deadline }
                    || id != operation.PublicationId || revision != operation.ExpectedRevision + 1
                    || deadline != operation.ValidUntil)
                    return new(null, null, ModerationPolicyAdministrationFailure.InvalidRepresentation);
                return new(null, new(candidate), ModerationPolicyAdministrationFailure.None);
            }
            var publication = await JsonSerializer.DeserializeAsync<ModerationPolicyPublicationResponse>(
                stream, Json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (publication is not { Success: true, PublishedActive: false }
                || publication.Id != operation.PolicyId || publication.PublicationId != operation.PublicationId
                || publication.Revision != operation.ExpectedRevision + 1)
                return new(null, null, ModerationPolicyAdministrationFailure.InvalidRepresentation);
            return new(new(publication), null, ModerationPolicyAdministrationFailure.None);
        }
        catch (HttpRequestException) { return new(null, null, ModerationPolicyAdministrationFailure.Uncertain); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, null, ModerationPolicyAdministrationFailure.Uncertain); }
        catch (JsonException) { return new(null, null, ModerationPolicyAdministrationFailure.InvalidRepresentation); }
    }

    private string? ActionPath(HalResourceOfModerationCurrentPolicyView current, bool preparation)
    {
        var relation = preparation ? "publication-preparation" : "draft-publication";
        var expected = $"/api/moderation/policies/{current.PolicyId:D}/"
            + (preparation ? "publication-preparations" : "publications");
        if (current._links?.TryGetValue(relation, out var link) != true || link is null
            || link.Method != "POST" || link.Templated == true || http.BaseAddress is not { } baseAddress
            || baseAddress.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(link.Href))
            return null;
        var href = link.Href;
        // Compare raw paths too: URI normalization must not legitimize traversal, escapes or queries.
        if (href == expected) return expected[1..];
        if (!Uri.TryCreate(href, UriKind.Absolute, out var absolute)
            || absolute.Scheme != baseAddress.Scheme || absolute.Host != baseAddress.Host
            || absolute.Port != baseAddress.Port || absolute.UserInfo.Length != 0)
            return null;
        var origin = absolute.GetLeftPart(UriPartial.Authority);
        return href == origin + expected ? expected[1..] : null;
    }

    private static bool ValidCurrent(HalResourceOfModerationCurrentPolicyView? current,
        ModerationPolicyAdministrationScope scope) =>
        current is not null && Version7(current.PolicyId) && current.Scope == ScopeName(scope)
        && current.Jurisdiction is not null
        && current.Jurisdiction.Kind == (scope == ModerationPolicyAdministrationScope.Tenant
            ? AuthorityScopeKind.Tenant : AuthorityScopeKind.Instance)
        && Version7(current.Jurisdiction.Id)
        && Version7(current.ExpectedHostBindingId) && Version7(current.ExpectedAuthoritySourceId)
        && current.ExpectedQualificationRevision is > 0 and < long.MaxValue
        && !string.IsNullOrWhiteSpace(current.ExpectedWriterGeneration)
        && current.InstalledCatalogueRevision > 0 && current.InstalledInventoryRevision > 0
        && !string.IsNullOrWhiteSpace(current.InstalledInventoryFingerprint)
        && current.AuthoringBounds is not null
        && ((current.State == "uninitialized" && current.Authoring is null
                && current.ExpectedRevision == 0 && current.ExpectedPublicationId is null)
            || (current.State == "adopted" && current.Authoring is not null
                && current.ExpectedRevision is > 0 and < long.MaxValue
                && current.ExpectedPublicationId is { } predecessor && Version7(predecessor)
                && current.Authoring.PublicationId == predecessor));

    private static bool Version7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;

    private static string? ScopeName(ModerationPolicyAdministrationScope scope) => scope switch
    {
        ModerationPolicyAdministrationScope.Tenant => "tenant",
        ModerationPolicyAdministrationScope.Instance => "instance",
        _ => null
    };

    private static ModerationPolicyAdministrationFailure Failure(int status) => status switch
    {
        >= 200 and < 300 => ModerationPolicyAdministrationFailure.InvalidRepresentation,
        400 => ModerationPolicyAdministrationFailure.InvalidIntent,
        401 => ModerationPolicyAdministrationFailure.SignInRequired,
        403 => ModerationPolicyAdministrationFailure.Forbidden,
        409 => ModerationPolicyAdministrationFailure.Conflict,
        429 => ModerationPolicyAdministrationFailure.RateLimited,
        _ => ModerationPolicyAdministrationFailure.Unavailable
    };
}
