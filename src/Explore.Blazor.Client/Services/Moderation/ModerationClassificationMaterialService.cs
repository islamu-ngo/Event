using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.Moderation;

/// <summary>
/// Transports candidate metadata through the authenticated generated-client pipeline.
/// A displayed inventory or proposal supplies the original coordinates; only native handlers
/// qualify ownership and independent acceptance. Metadata never grants content or audience access.
/// </summary>
public sealed class ModerationClassificationMaterialService(
    IModerationClassificationMaterialInventoryClient inventoryClient,
    IModerationClassificationMaterialReviewClient reviewClient,
    HttpClient http)
{
    private static readonly JsonSerializerOptions Json = EventApiJsonSerializerSettings.Configure(new());

    public async Task<ModerationClassificationMaterialReadResult<HalResourceOfModerationClassificationMaterialInventoryView>>
        ReadInventoryAsync(GetModerationClassificationMaterialInventoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Version7(query.EventGenerationId) || !Version7(query.TaxonomyId) || !Version7(query.ProfileId)
            || !Enum.IsDefined(query.Mode) || query.Mandate is null)
            return new(null, ModerationClassificationMaterialFailure.InvalidIntent);
        var intent = new GetModerationClassificationMaterialInventoryQuery
        {
            EventGenerationId = query.EventGenerationId,
            TaxonomyId = query.TaxonomyId,
            ProfileId = query.ProfileId,
            Mode = query.Mode,
            Mandate = CopyMandate(query.Mandate)
        };
        try
        {
            var view = await inventoryClient.ReadModerationClassificationMaterialInventoryAsync(intent, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (view is null || view.EventGenerationId != intent.EventGenerationId || view.TaxonomyId != intent.TaxonomyId
                || view.ProfileId != intent.ProfileId || view.Mode != intent.Mode
                || view.Target is null || view.ExpectedQualificationRevision < 1 || view.ExpectedMaterialGeneration < 1
                || !Version7(view.PolicyId) || !Version7(view.PublicationId) || view.ExpectedPolicyRevision < 1
                || !Version7(view.ExpectedTaxonomySourceId) || !Version7(view.ExpectedTaxonomyAdoptionId)
                || !Finite(view.ValidUntil) || view.Materials is not { Count: > 0 }
                || view.Components is null || view.Categories is null
                || view.Materials.Any(value => !ValidMaterial(value)))
                return new(null, ModerationClassificationMaterialFailure.InvalidRepresentation);
            if (http.BaseAddress is null) return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
            return new(new(http.BaseAddress, view), ModerationClassificationMaterialFailure.None);
        }
        catch (ApiException exception) { return new(null, Failure(exception.StatusCode)); }
        catch (HttpRequestException) { return new(null, ModerationClassificationMaterialFailure.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, ModerationClassificationMaterialFailure.Unavailable); }
        catch (JsonException) { return new(null, ModerationClassificationMaterialFailure.InvalidRepresentation); }
    }

    public async Task<ModerationClassificationMaterialReadResult<HalResourceOfModerationClassificationMaterialReviewView>>
        ReadReviewAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Version7(sourceId)) return new(null, ModerationClassificationMaterialFailure.InvalidIntent);
        try
        {
            var view = await reviewClient.GetModerationClassificationMaterialReviewAsync(sourceId, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (view is null || view.SourceId != sourceId || view.Target is null
                || view.ExpectedQualificationRevision < 1 || view.ExpectedSourceRevision < 1
                || view.ExpectedSourceWriterGeneration < 1 || !Version7(view.EventGenerationId)
                || !Version7(view.TaxonomyId) || !Version7(view.ExpectedTaxonomySourceId)
                || !Version7(view.ExpectedTaxonomyAdoptionId) || view.Mandate is null
                || view.Materials is not { Count: > 0 } || view.Components is null || view.Categories is null
                || !Finite(view.ValidUntil))
                return new(null, ModerationClassificationMaterialFailure.InvalidRepresentation);
            if (http.BaseAddress is null) return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
            return new(new(http.BaseAddress, view), ModerationClassificationMaterialFailure.None);
        }
        catch (ApiException exception) { return new(null, Failure(exception.StatusCode)); }
        catch (HttpRequestException) { return new(null, ModerationClassificationMaterialFailure.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, ModerationClassificationMaterialFailure.Unavailable); }
        catch (JsonException) { return new(null, ModerationClassificationMaterialFailure.InvalidRepresentation); }
    }

    /// <summary>Freezes a new owner intent without reacquiring or replacing displayed generations.</summary>
    public ModerationClassificationMaterialOperationResult CreateSource(
        ModerationClassificationMaterialView<HalResourceOfModerationClassificationMaterialInventoryView> displayed,
        ModerationClassificationMaterialMandate mandate, ICollection<ModerationClassificationMaterialSelection> selections,
        Guid sourceId, Guid operationId, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        ArgumentNullException.ThrowIfNull(mandate);
        ArgumentNullException.ThrowIfNull(selections);
        if (http.BaseAddress != displayed.BaseAddress)
            return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
        var view = displayed.Value;
        var path = ActionPath(view._links, "record-material-source", "/api/moderation/classification-materials/sources");
        if (path is null) return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
        if (!Version7(sourceId) || !Version7(operationId) || !Finite(validUntil) || validUntil > view.ValidUntil
            || selections.Count != view.Materials.Count || selections.Any(selection => selection is null
                || !Version7(selection.ReviewId) || !ValidMaterial(selection.Material) || selection.CategoryVersionIds is not { Count: > 0 }
                || selection.CategoryVersionIds.Distinct().Count() != selection.CategoryVersionIds.Count
                || selection.CategoryVersionIds.Any(id => !view.Categories.Any(category => category.VersionId == id)))
            || selections.Select(selection => selection.ReviewId).Distinct().Count() != selections.Count
            || !selections.Select(selection => Key(selection.Material)).ToHashSet()
                .SetEquals(view.Materials.Select(Key))
            || selections.Select(selection => Key(selection.Material)).Distinct().Count() != selections.Count)
            return new(null, ModerationClassificationMaterialFailure.InvalidIntent);
        var command = new RecordModerationClassificationMaterialCommand
        {
            OperationId = operationId,
            Target = new() { Kind = view.Target.Kind, Id = view.Target.Id },
            ExpectedQualificationRevision = view.ExpectedQualificationRevision,
            SourceId = sourceId,
            ExpectedMaterialGeneration = view.ExpectedMaterialGeneration,
            EventGenerationId = view.EventGenerationId,
            PolicyId = view.PolicyId,
            ExpectedPolicyRevision = view.ExpectedPolicyRevision,
            PublicationId = view.PublicationId,
            ProfileId = view.ProfileId,
            Mode = view.Mode,
            TaxonomyId = view.TaxonomyId,
            ExpectedTaxonomySourceId = view.ExpectedTaxonomySourceId,
            ExpectedTaxonomyAdoptionId = view.ExpectedTaxonomyAdoptionId,
            Mandate = CopyMandate(mandate),
            ValidUntil = validUntil,
            Materials = selections.Select(selection => new ModerationClassificationMaterialSelection
            {
                ReviewId = selection.ReviewId,
                CategoryVersionIds = selection.CategoryVersionIds.ToArray(),
                Material = new()
                {
                    EventId = selection.Material.EventId,
                    EventStamp = selection.Material.EventStamp,
                    SessionId = selection.Material.SessionId,
                    SessionStamp = selection.Material.SessionStamp,
                    ResourceId = selection.Material.ResourceId,
                    ResourceStamp = selection.Material.ResourceStamp
                }
            }).ToArray(),
            Components = view.Components.Select(component => new ModerationClassificationMaterialComponentVersion
            {
                Kind = component.Kind,
                NativeId = component.NativeId,
                NativeStamp = component.NativeStamp,
                Digest = component.Digest
            }).ToArray()
        };
        return new(new(http.BaseAddress!, path, JsonSerializer.Serialize(command, Json), operationId, sourceId),
            ModerationClassificationMaterialFailure.None);
    }

    /// <summary>Freezes only the nominated proposal's original source and qualification coordinates.</summary>
    public ModerationClassificationMaterialOperationResult CreateAcceptance(
        ModerationClassificationMaterialView<HalResourceOfModerationClassificationMaterialReviewView> displayed,
        Guid reviewId, Guid operationId, DateTimeOffset validUntil)
    {
        ArgumentNullException.ThrowIfNull(displayed);
        if (http.BaseAddress != displayed.BaseAddress)
            return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
        var view = displayed.Value;
        var path = ActionPath(view._links, "accept-material-source", "/api/moderation/classification-materials/reviews");
        if (path is null) return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
        if (!Version7(reviewId) || !Version7(operationId) || !Finite(validUntil) || validUntil > view.ValidUntil)
            return new(null, ModerationClassificationMaterialFailure.InvalidIntent);
        var command = new AcceptModerationClassificationMaterialCommand
        {
            SourceId = view.SourceId,
            ExpectedSourceRevision = view.ExpectedSourceRevision,
            ExpectedSourceWriterGeneration = view.ExpectedSourceWriterGeneration,
            ExpectedQualificationRevision = view.ExpectedQualificationRevision,
            Target = new() { Kind = view.Target.Kind, Id = view.Target.Id },
            OperationId = operationId,
            ReviewId = reviewId,
            ValidUntil = validUntil
        };
        return new(new(http.BaseAddress!, path, JsonSerializer.Serialize(command, Json), operationId, reviewId),
            ModerationClassificationMaterialFailure.None);
    }

    /// <summary>
    /// Sends once. After an uncertain response the caller may explicitly resubmit this exact operation.
    /// No refresh, identity replacement or permission inference occurs on any response.
    /// </summary>
    public async Task<ModerationClassificationMaterialSubmissionResult> SubmitAsync(
        ModerationClassificationMaterialOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (http.BaseAddress != operation.BaseAddress)
            return new(null, ModerationClassificationMaterialFailure.ActionUnavailable);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, operation.Path);
            request.Headers.CacheControl = new() { NoStore = true };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(operation.Body, Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessStatusCode) return new(null, Failure((int)response.StatusCode));
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var receipt = await JsonSerializer.DeserializeAsync<ModerationQualificationResponse>(stream, Json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (receipt is not { Success: true, Revision: 1, QualificationRevision: > 0 }
                || receipt.Id != operation.ResultId)
                return new(null, ModerationClassificationMaterialFailure.InvalidRepresentation);
            return new(new(receipt), ModerationClassificationMaterialFailure.None);
        }
        catch (HttpRequestException) { return new(null, ModerationClassificationMaterialFailure.Uncertain); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, ModerationClassificationMaterialFailure.Uncertain); }
        catch (JsonException) { return new(null, ModerationClassificationMaterialFailure.InvalidRepresentation); }
    }

    private string? ActionPath(IDictionary<string, HalLink>? links, string relation, string expected)
    {
        if (links?.TryGetValue(relation, out var link) != true || link is null
            || link.Method != "POST" || link.Templated == true || string.IsNullOrWhiteSpace(link.Href)
            || http.BaseAddress is not { Scheme: "http" or "https" } origin)
            return null;
        if (link.Href == expected) return expected[1..];
        if (!Uri.TryCreate(link.Href, UriKind.Absolute, out var absolute)
            || absolute.Scheme != origin.Scheme || absolute.Host != origin.Host || absolute.Port != origin.Port
            || absolute.UserInfo.Length != 0)
            return null;
        return link.Href == absolute.GetLeftPart(UriPartial.Authority) + expected ? expected[1..] : null;
    }

    private static ModerationClassificationMaterialMandate CopyMandate(ModerationClassificationMaterialMandate mandate) => new()
    {
        OwnerHumanId = mandate.OwnerHumanId,
        OwnerControllerHumanId = mandate.OwnerControllerHumanId,
        OwnerAssignmentId = mandate.OwnerAssignmentId,
        OwnerAssignmentRevision = mandate.OwnerAssignmentRevision,
        ReviewerHumanId = mandate.ReviewerHumanId,
        ReviewerControllerHumanId = mandate.ReviewerControllerHumanId,
        OwnershipGrounds = mandate.OwnershipGrounds,
        ReviewGrounds = mandate.ReviewGrounds
    };

    private static (Guid, Guid, Guid?, Guid?, Guid?, Guid?) Key(ModerationClassificationMaterial material) =>
        (material.EventId, material.EventStamp, material.SessionId, material.SessionStamp, material.ResourceId, material.ResourceStamp);

    private static bool ValidMaterial(ModerationClassificationMaterial? material) =>
        material is not null && material.EventId != Guid.Empty && material.EventStamp != Guid.Empty
        && material.SessionId.HasValue == material.SessionStamp.HasValue
        && material.ResourceId.HasValue == material.ResourceStamp.HasValue
        && material.SessionId != Guid.Empty && material.SessionStamp != Guid.Empty
        && material.ResourceId != Guid.Empty && material.ResourceStamp != Guid.Empty;

    private static bool Version7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private static bool Finite(DateTimeOffset value) => value > DateTimeOffset.MinValue && value < DateTimeOffset.MaxValue;
    private static ModerationClassificationMaterialFailure Failure(int status) => status switch
    {
        >= 200 and < 300 => ModerationClassificationMaterialFailure.InvalidRepresentation,
        400 => ModerationClassificationMaterialFailure.InvalidIntent,
        401 => ModerationClassificationMaterialFailure.SignInRequired,
        403 => ModerationClassificationMaterialFailure.Forbidden,
        409 => ModerationClassificationMaterialFailure.Conflict,
        429 => ModerationClassificationMaterialFailure.RateLimited,
        _ => ModerationClassificationMaterialFailure.Unavailable
    };
}

public enum ModerationClassificationMaterialFailure
{
    None, InvalidIntent, SignInRequired, Forbidden, Conflict, RateLimited,
    Unavailable, InvalidRepresentation, ActionUnavailable, Uncertain
}

public sealed record ModerationClassificationMaterialReadResult<T>(
    ModerationClassificationMaterialView<T>? View, ModerationClassificationMaterialFailure Failure) where T : class
{
    public override string ToString() => nameof(ModerationClassificationMaterialReadResult<T>);
}

/// <summary>Detached generated metadata bound to the HTTP context in which it was displayed.</summary>
public sealed record ModerationClassificationMaterialView<T> where T : class
{
    private readonly ModerationPolicyTransportSnapshot<T> snapshot;
    internal ModerationClassificationMaterialView(Uri baseAddress, T value)
    {
        BaseAddress = baseAddress;
        snapshot = new(value);
    }
    internal Uri BaseAddress { get; }
    public T Value => snapshot.Value;
    public override string ToString() => nameof(ModerationClassificationMaterialView<T>);
}

public sealed record ModerationClassificationMaterialOperationResult(
    ModerationClassificationMaterialOperation? Operation, ModerationClassificationMaterialFailure Failure)
{
    public override string ToString() => nameof(ModerationClassificationMaterialOperationResult);
}

public sealed record ModerationClassificationMaterialSubmissionResult(
    ModerationPolicyTransportSnapshot<ModerationQualificationResponse>? Receipt, ModerationClassificationMaterialFailure Failure)
{
    public override string ToString() => nameof(ModerationClassificationMaterialSubmissionResult);
}

/// <summary>A frozen request and native identity bound to the original authenticated HTTP base address.</summary>
public sealed record ModerationClassificationMaterialOperation
{
    internal ModerationClassificationMaterialOperation(Uri baseAddress, string path, string body, Guid operationId, Guid resultId)
    {
        BaseAddress = baseAddress; Path = path; Body = body; OperationId = operationId; ResultId = resultId;
    }
    internal Uri BaseAddress { get; }
    internal string Path { get; }
    internal string Body { get; }
    public Guid OperationId { get; }
    public Guid ResultId { get; }
    public override string ToString() => nameof(ModerationClassificationMaterialOperation);
}
