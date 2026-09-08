using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Svix;
using Svix.Models;

namespace Explore.Infrastructure.Webhooks;

internal sealed class SvixWebhookClient(
    ISecretResolver secretResolver,
    IOptionsMonitor<WebhookOptions> options,
    ILogger<SvixClient> svixLogger) : ISvixWebhookClient
{
    public async Task<SvixApplicationBindingResult> GetApplicationAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        var application = await client.Application.GetAsync(applicationId, cancellationToken);

        return new SvixApplicationBindingResult(
            application.Id,
            application.Uid ?? string.Empty,
            application.Metadata ?? new Dictionary<string, string>());
    }

    public async Task<SvixApplicationSyncResult> GetOrCreateApplicationAsync(
        SvixApplicationSyncRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        var application = await client.Application.GetOrCreateAsync(
            new ApplicationIn
            {
                Name = request.Name,
                Uid = request.AppUid,
                Metadata = request.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            },
            new ApplicationCreateOptions
            {
                IdempotencyKey = request.IdempotencyKey
            },
            cancellationToken);

        return new SvixApplicationSyncResult(
            application.Id,
            string.IsNullOrWhiteSpace(application.Uid) ? request.AppUid : application.Uid);
    }

    public async Task<SvixMessageCreateResult> CreateMessageAsync(
        SvixMessageCreateRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        var payloadJson = DecodeExactUtf8(request.PayloadBytes);
        var message = Message.messageInRaw(
            request.EventType,
            payloadJson,
            "application/json",
            application: null,
            channels: null,
            eventId: request.EventId,
            payloadRetentionHours: null,
            payloadRetentionPeriod: request.PayloadRetentionDays,
            tags: null,
            transformationsParams: null);

        var created = await client.Message.CreateAsync(
            request.AppUid,
            message,
            new MessageCreateOptions
            {
                IdempotencyKey = request.IdempotencyKey
            },
            cancellationToken);

        return new SvixMessageCreateResult(created.Id);
    }

    public async Task<SvixMessageCreateResult> CreatePublicationMessageAsync(
        SvixProviderPublicationCreateRequest request,
        CancellationToken cancellationToken)
    {
        SvixClient client;
        try
        {
            client = await CreateSnapshotClientAsync(
                request.CredentialReference,
                request.ProviderEnvironment,
                request.ProviderVersion,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SvixWebhookConfigurationException exception)
        {
            throw SvixWebhookSubmissionException.DefinitelyNotAccepted(
                exception.FailureCategory,
                isRetryable: false,
                exception.FailureCategory,
                exception);
        }
        catch (Exception exception)
        {
            throw SvixWebhookSubmissionException.DefinitelyNotAccepted(
                "svix_client_initialization_failed",
                isRetryable: true,
                exception.GetType().Name,
                exception);
        }

        try
        {
            var payloadJson = DecodeExactUtf8(request.PayloadBytes);
            var message = Message.messageInRaw(
                request.EventType,
                payloadJson,
                "application/json",
                application: null,
                channels: null,
                eventId: request.EventId,
                payloadRetentionHours: null,
                payloadRetentionPeriod: request.PayloadRetentionDays,
                tags: [CreateEvidenceTag(request.RequestHash)],
                transformationsParams: null);
            var created = await client.Message.CreateAsync(
                request.ProviderApplicationId,
                message,
                new MessageCreateOptions
                {
                    IdempotencyKey = request.IdempotencyKey
                },
                cancellationToken);

            return new SvixMessageCreateResult(created.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception) when (exception.ErrorCode == 429)
        {
            var failure = SvixWebhookFailureClassifier.Classify(exception);
            throw SvixWebhookSubmissionException.DefinitelyNotAccepted(
                failure.Category,
                failure.IsRetryable,
                failure.SafeDetail,
                exception);
        }
        catch (ApiException exception) when (exception.ErrorCode is >= 400 and < 500)
        {
            var failure = SvixWebhookFailureClassifier.Classify(exception);
            throw SvixWebhookSubmissionException.DefinitelyNotAccepted(
                failure.Category,
                failure.IsRetryable,
                failure.SafeDetail,
                exception);
        }
        catch (Exception exception)
        {
            throw SvixWebhookSubmissionException.AcceptanceUnknown(
                "svix_submission_outcome_unknown",
                exception.GetType().Name,
                exception);
        }
    }

    public async Task<SvixProviderPublicationLookupResult> LookupPublicationMessageAsync(
        SvixProviderPublicationLookupRequest request,
        CancellationToken cancellationToken)
    {
        SvixClient client;
        try
        {
            client = await CreateSnapshotClientAsync(
                request.CredentialReference,
                request.ProviderEnvironment,
                request.ProviderVersion,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SvixWebhookConfigurationException exception)
        {
            return new SvixProviderPublicationLookupResult(
                SvixProviderPublicationLookupOutcome.Unsupported,
                null,
                exception.FailureCategory);
        }
        catch (Exception)
        {
            return SvixProviderPublicationLookupResult.Unavailable("svix_lookup_client_unavailable");
        }

        try
        {
            var evidenceTag = CreateEvidenceTag(request.RequestHash);
            var exactMessageIds = new HashSet<string>(StringComparer.Ordinal);
            var conflictingMatch = false;
            var seenIterators = new HashSet<string>(StringComparer.Ordinal);
            string? iterator = null;

            for (var page = 0; page < request.PageLimit; page++)
            {
                var response = await client.Message.ListAsync(
                    request.ProviderApplicationId,
                    new MessageListOptions
                    {
                        Limit = 100,
                        Iterator = iterator,
                        After = request.PreparedAt.AddMinutes(-5),
                        Before = request.IdempotencyValidUntil.AddMinutes(5),
                        WithContent = false,
                        EventTypes = [request.EventType]
                    },
                    cancellationToken);

                foreach (var message in response.Data ?? [])
                {
                    if (!string.Equals(message.EventId, request.EventId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (message.Tags?.Contains(evidenceTag, StringComparer.Ordinal) == true &&
                        !string.IsNullOrWhiteSpace(message.Id))
                    {
                        exactMessageIds.Add(message.Id);
                    }
                    else
                    {
                        conflictingMatch = true;
                    }
                }

                if (response.Done)
                {
                    return ClassifyLookup(exactMessageIds, conflictingMatch);
                }

                if (string.IsNullOrWhiteSpace(response.Iterator) ||
                    !seenIterators.Add(response.Iterator))
                {
                    return new SvixProviderPublicationLookupResult(
                        SvixProviderPublicationLookupOutcome.Ambiguous,
                        null,
                        "svix_lookup_pagination_ambiguous");
                }

                iterator = response.Iterator;
            }

            return new SvixProviderPublicationLookupResult(
                SvixProviderPublicationLookupOutcome.Ambiguous,
                null,
                "svix_lookup_page_limit_exceeded");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception) when (exception.ErrorCode is 401 or 403 or 404)
        {
            return new SvixProviderPublicationLookupResult(
                SvixProviderPublicationLookupOutcome.Unsupported,
                null,
                $"svix_lookup_rejected_{exception.ErrorCode}");
        }
        catch (Exception)
        {
            return SvixProviderPublicationLookupResult.Unavailable("svix_lookup_unavailable");
        }
    }

    private static string DecodeExactUtf8(byte[] payloadBytes)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var payload = utf8.GetString(payloadBytes);
            if (!utf8.GetBytes(payload).AsSpan().SequenceEqual(payloadBytes))
            {
                throw new SvixWebhookConfigurationException("svix_payload_not_exact_utf8");
            }

            return payload;
        }
        catch (DecoderFallbackException)
        {
            throw new SvixWebhookConfigurationException("svix_payload_not_utf8");
        }
    }

    public async Task<SvixAppPortalAccessResult> CreateAppPortalAccessAsync(
        SvixAppPortalAccessRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        var access = await client.Authentication.AppPortalAccessAsync(
            request.AppId,
            new AppPortalAccessIn
            {
                SessionId = request.SessionId,
                ReadOnly = request.ReadOnly,
                Expiry = (ulong)Math.Ceiling(request.ExpiresIn.TotalSeconds),
                FeatureFlags = request.FeatureFlags.Count == 0
                    ? null
                    : request.FeatureFlags.ToList()
            },
            new AuthenticationAppPortalAccessOptions
            {
                IdempotencyKey = request.IdempotencyKey
            },
            cancellationToken);

        return new SvixAppPortalAccessResult(access.Url, access.Token);
    }

    public async Task<SvixEventTypeSyncResult> UpsertEventTypeAsync(
        SvixEventTypeSyncRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        var schema = ParseSchema(request.SchemaJson);
        try
        {
            var created = await client.EventType.CreateAsync(
                new EventTypeIn
                {
                    Name = request.Name,
                    Description = request.Description,
                    GroupName = request.GroupName,
                    Schemas = schema
                },
                new EventTypeCreateOptions
                {
                    IdempotencyKey = request.IdempotencyKey
                },
                cancellationToken);

            return new SvixEventTypeSyncResult(created.Name);
        }
        catch (ApiException ex) when (ex.ErrorCode == 409)
        {
            var updated = await client.EventType.UpdateAsync(
                request.Name,
                new EventTypeUpdate
                {
                    Description = request.Description,
                    GroupName = request.GroupName,
                    Schemas = schema
                },
                cancellationToken);

            return new SvixEventTypeSyncResult(updated.Name);
        }
    }

    private async Task<SvixClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue.Svix;
        var settingKey = currentOptions.AuthTokenSecretRef?.Trim();
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            throw new SvixWebhookConfigurationException("svix_auth_token_secret_missing");
        }

        var resolved = await secretResolver.ResolveAsync(settingKey, tenantId: null, cancellationToken);
        if (!resolved.IsResolved || string.IsNullOrWhiteSpace(resolved.Value))
        {
            throw new SvixWebhookConfigurationException("svix_auth_token_unresolved");
        }

        var svixOptions = string.IsNullOrWhiteSpace(currentOptions.BaseUrl)
            ? null
            : new SvixOptions(serverUrl: currentOptions.BaseUrl.Trim());

        return new SvixClient(resolved.Value, svixOptions, svixLogger);
    }

    private async Task<SvixClient> CreateSnapshotClientAsync(
        string credentialReference,
        string providerEnvironment,
        string providerVersion,
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue.Svix;
        if (!string.Equals(
                currentOptions.AuthTokenSecretRef?.Trim(),
                credentialReference,
                StringComparison.Ordinal) ||
            !string.Equals(
                currentOptions.Environment.Trim(),
                providerEnvironment,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                currentOptions.ProviderVersion.Trim(),
                providerVersion,
                StringComparison.Ordinal))
        {
            throw new SvixWebhookConfigurationException("svix_publication_snapshot_mismatch");
        }

        var resolved = await secretResolver.ResolveAsync(
            credentialReference,
            tenantId: null,
            cancellationToken);
        if (!resolved.IsResolved || string.IsNullOrWhiteSpace(resolved.Value))
        {
            throw new SvixWebhookConfigurationException("svix_auth_token_unresolved");
        }

        var svixOptions = string.IsNullOrWhiteSpace(currentOptions.BaseUrl)
            ? null
            : new SvixOptions(serverUrl: currentOptions.BaseUrl.Trim());

        return new SvixClient(resolved.Value, svixOptions, svixLogger);
    }

    private static SvixProviderPublicationLookupResult ClassifyLookup(
        IReadOnlyCollection<string> exactMessageIds,
        bool conflictingMatch)
    {
        if (conflictingMatch)
        {
            return new SvixProviderPublicationLookupResult(
                SvixProviderPublicationLookupOutcome.ConflictingMatch,
                null,
                "svix_lookup_conflicting_event_identity");
        }

        return exactMessageIds.Count switch
        {
            0 => SvixProviderPublicationLookupResult.NotFound(),
            1 => SvixProviderPublicationLookupResult.ExactMatch(exactMessageIds.Single()),
            _ => new SvixProviderPublicationLookupResult(
                SvixProviderPublicationLookupOutcome.Ambiguous,
                null,
                "svix_lookup_multiple_exact_matches")
        };
    }

    private static string CreateEvidenceTag(string requestHash) =>
        $"islamu-{requestHash.Replace(':', '-')}";

    private static JsonElement ParseSchema(string schemaJson)
    {
        using var document = JsonDocument.Parse(schemaJson);
        return document.RootElement.Clone();
    }
}

internal sealed class SvixWebhookConfigurationException(string failureCategory) : Exception(failureCategory)
{
    public string FailureCategory { get; } = failureCategory;
}
