using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookProviderPublicationDispatcher(
    IWebhookMessageRepository messageRepository,
    IWebhookProviderPublicationRepository publicationRepository,
    ISvixWebhookClient svixClient,
    IOptions<WebhookProviderPublicationProcessorSettings> settings,
    TimeProvider timeProvider)
{
    private const int SvixMaximumPayloadRetentionDays = 90;

    private readonly WebhookProviderPublicationProcessorSettings _settings = settings.Value;

    public async Task<WebhookProviderPublicationDispatchResult> DispatchAsync(
        WebhookProviderPublicationClaim claim,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        cancellationToken.ThrowIfCancellationRequested();

        var publication = claim.Publication;
        var observedAt = GetUtcNow();
        if (!ClaimIsActive(claim, observedAt))
        {
            return WebhookProviderPublicationDispatchResult.LeaseLost();
        }

        SvixProviderPublicationCreateRequest request;
        try
        {
            request = await CreateRequestAsync(publication, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return await DeadLetterAsync(
                claim,
                "webhook_publication_snapshot_invalid",
                exception.GetType().Name,
                cancellationToken);
        }

        SvixMessageCreateResult created;
        try
        {
            created = await svixClient.CreatePublicationMessageAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SvixWebhookSubmissionException exception)
        {
            return exception.MayHaveBeenAccepted
                ? await MarkUnknownAsync(claim, exception.FailureCategory, exception.SafeDetail, cancellationToken)
                : await HandleKnownFailureAsync(claim, exception, cancellationToken);
        }
        catch (Exception exception)
        {
            return await MarkUnknownAsync(
                claim,
                "svix_submission_outcome_unknown",
                exception.GetType().Name,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(created.MessageId))
        {
            return await MarkUnknownAsync(
                claim,
                "svix_submission_response_invalid",
                nameof(SvixMessageCreateResult),
                cancellationToken);
        }

        observedAt = GetUtcNow();
        try
        {
            publication.MarkProviderQueued(
                claim.LeaseToken,
                claim.PublicationFence,
                created.MessageId,
                observedAt);
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderPublicationDispatchResult.LeaseLost();
        }

        return await PersistAsync(
            publication,
            WebhookProviderPublicationDispatchOutcome.ProviderQueued,
            cancellationToken);
    }

    private async Task<SvixProviderPublicationCreateRequest> CreateRequestAsync(
        WebhookProviderPublication publication,
        CancellationToken cancellationToken)
    {
        if (publication.ProviderKind != WebhookProviderKind.Svix ||
            publication.ModeSnapshot is not (WebhookProviderMode.Svix or WebhookProviderMode.Composite) ||
            publication.WebhookDeliveryPlanSnapshotId == Guid.Empty)
        {
            throw new InvalidOperationException("The provider publication is not an immutable Svix plan target.");
        }

        var message = await messageRepository.GetByTenantAndIdAsync(
            publication.TenantId,
            publication.WebhookMessageId,
            cancellationToken);
        var payloadBytes = message?.GetPayloadBytes();
        if (message is null ||
            message.TenantId != publication.TenantId ||
            message.Id != publication.WebhookMessageId ||
            payloadBytes is null ||
            !string.Equals(message.PayloadHash, publication.RequestHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The exact payload no longer matches the publication snapshot.");
        }

        if (string.IsNullOrWhiteSpace(publication.ProviderApplicationId))
        {
            throw new InvalidOperationException(
                "Provider publication cannot execute until its application identity is verified.");
        }

        var retentionDays = (int)Math.Ceiling(
            (publication.PayloadRetentionUntil - publication.PreparedAt).TotalDays);

        return new SvixProviderPublicationCreateRequest(
            publication.TenantId,
            publication.ProviderApplicationId,
            publication.ApplicationUid,
            publication.ProviderEnvironment,
            publication.ProviderVersion,
            publication.CredentialReference,
            publication.CredentialVersion,
            message.EventType,
            publication.ProviderEventId,
            payloadBytes,
            Math.Clamp(retentionDays, 1, SvixMaximumPayloadRetentionDays),
            publication.IdempotencyKey,
            publication.RequestHash);
    }

    private async Task<WebhookProviderPublicationDispatchResult> HandleKnownFailureAsync(
        WebhookProviderPublicationClaim claim,
        SvixWebhookSubmissionException exception,
        CancellationToken cancellationToken)
    {
        var publication = claim.Publication;
        var failedAt = GetUtcNow();
        if (!exception.IsRetryable ||
            publication.AutomaticPublicationAttemptCount >= _settings.MaxAutomaticPublicationAttempts)
        {
            return await DeadLetterAsync(
                claim,
                exception.FailureCategory,
                exception.SafeDetail,
                cancellationToken);
        }

        var nextActionAt = CalculateRetryAt(publication, failedAt);
        if (nextActionAt >= publication.IdempotencyValidUntil)
        {
            return await DeadLetterAsync(
                claim,
                "svix_idempotency_window_exhausted",
                null,
                cancellationToken);
        }

        try
        {
            publication.ScheduleRetry(
                claim.LeaseToken,
                claim.PublicationFence,
                exception.FailureCategory,
                exception.SafeDetail,
                nextActionAt,
                failedAt);
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderPublicationDispatchResult.LeaseLost();
        }

        return await PersistAsync(
            publication,
            WebhookProviderPublicationDispatchOutcome.RetryScheduled,
            cancellationToken);
    }

    private async Task<WebhookProviderPublicationDispatchResult> MarkUnknownAsync(
        WebhookProviderPublicationClaim claim,
        string failureCategory,
        string? safeDetail,
        CancellationToken cancellationToken)
    {
        var observedAt = GetUtcNow();
        try
        {
            claim.Publication.MarkPublicationUnknown(
                claim.LeaseToken,
                claim.PublicationFence,
                failureCategory,
                safeDetail,
                observedAt.AddSeconds(_settings.UnknownReconciliationDelaySeconds),
                observedAt);
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderPublicationDispatchResult.LeaseLost();
        }

        return await PersistAsync(
            claim.Publication,
            WebhookProviderPublicationDispatchOutcome.PublicationUnknown,
            cancellationToken);
    }

    private async Task<WebhookProviderPublicationDispatchResult> DeadLetterAsync(
        WebhookProviderPublicationClaim claim,
        string failureCategory,
        string? safeDetail,
        CancellationToken cancellationToken)
    {
        var failedAt = GetUtcNow();
        try
        {
            claim.Publication.DeadLetter(
                claim.LeaseToken,
                claim.PublicationFence,
                failureCategory,
                safeDetail,
                failedAt);
        }
        catch (InvalidOperationException)
        {
            return WebhookProviderPublicationDispatchResult.LeaseLost();
        }

        return await PersistAsync(
            claim.Publication,
            WebhookProviderPublicationDispatchOutcome.DeadLettered,
            cancellationToken);
    }

    private async Task<WebhookProviderPublicationDispatchResult> PersistAsync(
        WebhookProviderPublication publication,
        WebhookProviderPublicationDispatchOutcome outcome,
        CancellationToken cancellationToken)
    {
        try
        {
            await publicationRepository.UpdateAsync(publication, cancellationToken);
            return new WebhookProviderPublicationDispatchResult(outcome);
        }
        catch (WebhookProviderPublicationConcurrencyException)
        {
            return WebhookProviderPublicationDispatchResult.LeaseLost();
        }
    }

    private DateTime CalculateRetryAt(WebhookProviderPublication publication, DateTime failedAt)
    {
        var exponent = Math.Min(publication.AutomaticPublicationAttemptCount - 1, 30);
        var ceilingSeconds = Math.Min(
            _settings.InitialRetryDelaySeconds * Math.Pow(2, exponent),
            _settings.MaxRetryDelaySeconds);
        var identityHash = publication.Id.GetHashCode() ^ publication.AutomaticPublicationAttemptCount;
        var jitterRatio = (uint)identityHash / (double)uint.MaxValue;
        var delaySeconds = Math.Max(1, ceilingSeconds * (0.5 + (jitterRatio * 0.5)));
        return failedAt.AddSeconds(delaySeconds);
    }

    private static bool ClaimIsActive(WebhookProviderPublicationClaim claim, DateTime observedAt) =>
        claim.Publication.Status == WebhookProviderPublicationStatus.Publishing &&
        claim.Publication.ProcessingLeaseToken == claim.LeaseToken &&
        claim.Publication.PublicationFence == claim.PublicationFence &&
        claim.Publication.IdempotencyValidUntil > observedAt &&
        claim.LeaseExpiresAt > observedAt;

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record WebhookProviderPublicationDispatchResult(
    WebhookProviderPublicationDispatchOutcome Outcome)
{
    public static WebhookProviderPublicationDispatchResult LeaseLost() =>
        new(WebhookProviderPublicationDispatchOutcome.LeaseLost);
}

public enum WebhookProviderPublicationDispatchOutcome
{
    ProviderQueued = 1,
    RetryScheduled = 2,
    PublicationUnknown = 3,
    DeadLettered = 4,
    LeaseLost = 5
}
