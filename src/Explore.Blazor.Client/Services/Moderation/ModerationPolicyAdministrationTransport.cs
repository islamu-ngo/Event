using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.Moderation;

public enum ModerationPolicyAdministrationScope { Tenant, Instance }

public enum ModerationPolicyAdministrationFailure
{
    None, InvalidIntent, SignInRequired, Forbidden, Conflict, RateLimited,
    Unavailable, InvalidRepresentation, ActionUnavailable, Uncertain
}

/// <summary>
/// Owns a complete generated contract as frozen wire meaning. Each access returns a detached
/// typed graph: generated collection members and extension-data setters cannot mutate this snapshot.
/// This is not a second policy model, authority cache or display-mode projection.
/// </summary>
public sealed record ModerationPolicyTransportSnapshot<T> where T : class
{
    private readonly string json;

    internal ModerationPolicyTransportSnapshot(T value) =>
        json = JsonSerializer.Serialize(value, ModerationPolicyAdministrationService.Json);

    public T Value => JsonSerializer.Deserialize<T>(json, ModerationPolicyAdministrationService.Json)
        ?? throw new JsonException("The moderation transport snapshot is incomplete.");

    public override string ToString() => nameof(ModerationPolicyTransportSnapshot<T>);
}

public sealed record ModerationPolicyAdministrationView
{
    private readonly ModerationPolicyTransportSnapshot<HalResourceOfModerationCurrentPolicyView> current;
    private readonly ModerationPolicyTransportSnapshot<HalResourceOfModerationReferenceCatalogueView> reference;

    internal ModerationPolicyAdministrationView(
        HalResourceOfModerationCurrentPolicyView current,
        HalResourceOfModerationReferenceCatalogueView reference,
        bool canPublishDraft, bool canPreparePublication)
    {
        this.current = new(current);
        this.reference = new(reference);
        IsUninitialized = current.State == "uninitialized";
        CanPublishDraft = canPublishDraft;
        CanPreparePublication = canPreparePublication;
    }

    public HalResourceOfModerationCurrentPolicyView Current => current.Value;
    public HalResourceOfModerationReferenceCatalogueView ReferenceCatalogue => reference.Value;
    public bool IsUninitialized { get; }
    public bool CanPublishDraft { get; }
    public bool CanPreparePublication { get; }
    public override string ToString() => nameof(ModerationPolicyAdministrationView);
}

/// <summary>
/// One deliberately reusable native receipt identity and immutable request. Retry means explicitly
/// submitting this same operation, not fetching new coordinates or generating another identity.
/// No activation operation exists. The API must requalify authority on every submission.
/// </summary>
public sealed record ModerationPolicyAdministrationOperation
{
    internal ModerationPolicyAdministrationOperation(
        Uri baseAddress, string path, string body, Guid operationId, Guid policyId,
        Guid publicationId, long expectedRevision, DateTimeOffset? validUntil)
    {
        BaseAddress = baseAddress;
        Path = path;
        Body = body;
        OperationId = operationId;
        PolicyId = policyId;
        PublicationId = publicationId;
        ExpectedRevision = expectedRevision;
        ValidUntil = validUntil;
    }

    internal Uri BaseAddress { get; }
    internal string Path { get; }
    internal string Body { get; }
    public Guid OperationId { get; }
    public Guid PolicyId { get; }
    public Guid PublicationId { get; }
    public long ExpectedRevision { get; }
    public DateTimeOffset? ValidUntil { get; }
    public bool IsPreparation => ValidUntil.HasValue;
    public override string ToString() => nameof(ModerationPolicyAdministrationOperation);
}

public sealed record ModerationPolicyAdministrationReadResult(
    ModerationPolicyAdministrationView? View, ModerationPolicyAdministrationFailure Failure)
{
    public override string ToString() => nameof(ModerationPolicyAdministrationReadResult);
}

public sealed record ModerationPolicyAdministrationOperationResult(
    ModerationPolicyAdministrationOperation? Operation, ModerationPolicyAdministrationFailure Failure)
{
    public override string ToString() => nameof(ModerationPolicyAdministrationOperationResult);
}

/// <summary>
/// Publication is a publication-instant observation, not an enduring grant.
/// Candidate is only preparation for measurement, never adoption or active readiness.
/// </summary>
public sealed record ModerationPolicyAdministrationSubmissionResult(
    ModerationPolicyTransportSnapshot<ModerationPolicyPublicationResponse>? Publication,
    ModerationPolicyTransportSnapshot<ModerationPolicyPublicationCandidateResponse>? Candidate,
    ModerationPolicyAdministrationFailure Failure)
{
    public override string ToString() => nameof(ModerationPolicyAdministrationSubmissionResult);
}
