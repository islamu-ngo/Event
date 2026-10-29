using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services.Moderation;

public enum PreFindingNoticeFailure
{
    None, Unavailable, SignInRequired, Conflict, RateLimited, InvalidResponse, Expired
}

/// <summary>A transient display projection of the generated private HAL resource, not an authority cache.</summary>
public sealed record PreFindingNoticeView(
    Guid NoticeId, Guid CaseId, long CaseRevision, long NoticeRevision,
    string Allegation, string ApplicableRule, string ResponseInstructions,
    DateTimeOffset RetainUntil, DateTimeOffset? AcknowledgedAt, DateTimeOffset? RespondedAt,
    string? Response, bool HasAcknowledgeLink, bool HasSubmitResponseLink)
{
    public override string ToString() => nameof(PreFindingNoticeView);
}

public sealed record PreFindingNoticeReadResult(PreFindingNoticeView? View, PreFindingNoticeFailure Failure)
{
    public override string ToString() => nameof(PreFindingNoticeReadResult);
}

/// <summary>
/// Uses the registered generated client and its BFF transport. No persistence, logging or retry policy.
/// </summary>
public sealed class PreFindingNoticeService(IModerationPreFindingNoticesClient client, TimeProvider clock) : IPreFindingNoticeService
{
    public const int MaximumResponseBytes = 65536;

    public async Task<PreFindingNoticeReadResult> GetAsync(
        Guid noticeId, bool reviewResponse, CancellationToken cancellationToken)
    {
        if (noticeId == Guid.Empty)
            return new(null, PreFindingNoticeFailure.Unavailable);
        try
        {
            var resource = await client.GetModerationPreFindingNoticeAsync(
                noticeId, reviewResponse, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var id = resource.NoticeId;
            var caseId = resource.CaseId;
            var caseRevision = resource.CaseRevision;
            var revision = resource.Revision;
            var expiry = resource.RetainUntil;
            if (id != noticeId || caseId == Guid.Empty || caseRevision < 1 || revision < 1)
                return new(null, PreFindingNoticeFailure.Unavailable);
            if (expiry <= clock.GetUtcNow())
                return new(null, PreFindingNoticeFailure.Expired);
            var view = new PreFindingNoticeView(id, caseId, caseRevision, revision,
                SafeText(resource.Notice?.Allegation?.Value), SafeText(resource.Notice?.ApplicableRule?.Value),
                SafeText(resource.Notice?.ResponseInstructions?.Value), expiry,
                resource.AcknowledgedAt, resource.RespondedAt,
                OptionalResponse(resource.Response),
                !reviewResponse && HasLink(resource._links, "acknowledge"),
                !reviewResponse && HasLink(resource._links, "submit-response"));
            return new(view, PreFindingNoticeFailure.None);
        }
        catch (ApiException exception) { return new(null, Failure(exception.StatusCode)); }
        catch (HttpRequestException) { return new(null, PreFindingNoticeFailure.Unavailable); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, PreFindingNoticeFailure.Unavailable); }
        catch (JsonException) { return new(null, PreFindingNoticeFailure.Unavailable); }
        catch (FormatException) { return new(null, PreFindingNoticeFailure.Unavailable); }
        catch (InvalidOperationException) { return new(null, PreFindingNoticeFailure.Unavailable); }
    }

    public Task<PreFindingNoticeFailure> AcknowledgeAsync(
        PreFindingNoticeView view, CancellationToken cancellationToken)
    {
        if (!view.HasAcknowledgeLink)
            return Task.FromResult(PreFindingNoticeFailure.Unavailable);
        if (view.RetainUntil <= clock.GetUtcNow())
            return Task.FromResult(PreFindingNoticeFailure.Expired);
        // One operation ID belongs to this invocation. The request is never retried.
        var command = new AcknowledgeModerationPreFindingNoticeCommand
        {
            OperationId = Guid.CreateVersion7(),
            CaseId = view.CaseId,
            ExpectedCaseRevision = view.CaseRevision,
            NoticeId = view.NoticeId,
            ExpectedNoticeRevision = view.NoticeRevision
        };
        return WriteAsync(() => client.AcknowledgeModerationPreFindingNoticeAsync(
            view.NoticeId, command, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<PreFindingNoticeFailure> RespondAsync(
        PreFindingNoticeView view, string statement, CancellationToken cancellationToken)
    {
        if (!view.HasSubmitResponseLink)
            return Task.FromResult(PreFindingNoticeFailure.Unavailable);
        if (view.RetainUntil <= clock.GetUtcNow())
            return Task.FromResult(PreFindingNoticeFailure.Expired);
        if (!IsValidResponse(statement))
            return Task.FromResult(PreFindingNoticeFailure.InvalidResponse);
        var command = new SubmitModerationPreFindingResponseCommand
        {
            OperationId = Guid.CreateVersion7(),
            CaseId = view.CaseId,
            ExpectedCaseRevision = view.CaseRevision,
            NoticeId = view.NoticeId,
            ExpectedNoticeRevision = view.NoticeRevision,
            Statement = statement
        };
        return WriteAsync(() => client.SubmitModerationPreFindingResponseAsync(
            view.NoticeId, command, cancellationToken: cancellationToken), cancellationToken);
    }

    public static bool IsValidResponse(string statement) =>
        !string.IsNullOrWhiteSpace(statement) && Encoding.UTF8.GetByteCount(statement) <= MaximumResponseBytes;

    private static async Task<PreFindingNoticeFailure> WriteAsync(
        Func<Task<BaseCommandResponseOfGuid>> write, CancellationToken cancellationToken)
    {
        try
        {
            var result = await write();
            cancellationToken.ThrowIfCancellationRequested();
            return result.Success ? PreFindingNoticeFailure.None : PreFindingNoticeFailure.Unavailable;
        }
        catch (ApiException exception) { return Failure(exception.StatusCode); }
        catch (HttpRequestException) { return PreFindingNoticeFailure.Unavailable; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return PreFindingNoticeFailure.Unavailable; }
        catch (JsonException) { return PreFindingNoticeFailure.Unavailable; }
    }

    private static PreFindingNoticeFailure Failure(int status) => status switch
    {
        401 => PreFindingNoticeFailure.SignInRequired,
        409 => PreFindingNoticeFailure.Conflict,
        429 => PreFindingNoticeFailure.RateLimited,
        _ => PreFindingNoticeFailure.Unavailable
    };

    private static string SafeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000)
            throw new JsonException("The protected notice text is invalid.");
        return text;
    }

    private static string? OptionalResponse(string? response)
    {
        if (response is not null && Encoding.UTF8.GetByteCount(response) > MaximumResponseBytes)
            throw new JsonException("The protected response exceeds its bound.");
        return response;
    }

    private static bool HasLink(IDictionary<string, HalLink>? links, string relation) =>
        links is not null && links.TryGetValue(relation, out var link)
        && !string.IsNullOrWhiteSpace(link?.Href);
}
