using System.Text.Json;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Svix;
using Svix.Models;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

[Category(InfrastructureTestCategories.Runtime)]
[Category(InfrastructureTestCategories.Slow)]
[Category(InfrastructureTestCategories.Manual)]
[Explicit]
[NotInParallel("SvixManagedConformance")]
public sealed class SvixManagedConformanceTests
{
    private const int ProbeSchemaVersion = 1;
    private const int ResultSchemaVersion = 1;
    private const string DefaultServerUrl = "https://api.svix.com";
    private const string EventType = "islamu.conformance";
    private const string CurrentAuthTokenVariable = "SVIX_CONFORMANCE_MANAGED_AUTH_TOKEN_CURRENT";
    private const string RotatedAuthTokenVariable = "SVIX_CONFORMANCE_MANAGED_AUTH_TOKEN_ROTATED";
    private const string CredentialVersionVariable = "SVIX_CONFORMANCE_MANAGED_CREDENTIAL_VERSION";
    private const string BaseUrlVariable = "SVIX_CONFORMANCE_MANAGED_BASE_URL";
    private const string ProbePathVariable = "SVIX_CONFORMANCE_MANAGED_PROBE_PATH";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly string[] ImmediateCaseNames =
    [
        "repeat-create-inside-window",
        "duplicate-event-identity",
        "acceptance-timeout",
        "credential-rotation",
        "list-get-consistency-and-capability-availability",
        "expiry-probe-seeded"
    ];

    [Test]
    public async Task ManagedProfile_SeedsExpiryProbeAfterImmediateMatrix()
    {
        var configuration = ManagedConformanceConfiguration.Load(requireRotatedToken: true);
        EnsureProbeDoesNotExist(configuration.ProbePath);
        var currentClient = configuration.CreateCurrentClient();

        await VerifyRepeatCreateInsideWindowAsync(currentClient);
        await VerifyDuplicateEventIdentityAsync(currentClient);
        await VerifyAcceptanceTimeoutAsync(configuration, currentClient);
        await VerifyCredentialRotationAsync(configuration, currentClient);
        var supportsExactMessageLookup = await VerifyListAndGetConsistencyAsync(currentClient);
        await SeedExpiryProbeAsync(configuration, currentClient, supportsExactMessageLookup);
    }

    [Test]
    public async Task ManagedProfile_VerifiesExpiredProbeAndCleansUp()
    {
        var configuration = ManagedConformanceConfiguration.Load(requireRotatedToken: false);
        var state = await ReadProbeStateAsync(configuration);
        var now = TimeProvider.System.GetUtcNow();
        if (now < state.VerifyNotBeforeUtc)
        {
            throw new InvalidOperationException(
                "Managed Svix expiry verification is not eligible until the documented idempotency window has elapsed.");
        }

        var client = configuration.CreateCurrentClient();
        var changedEventId = NewIdentity("managed-after-window");
        var response = await ProviderCallAsync(
            "repeat create after documented idempotency window",
            () => CreateMessageAsync(
                client,
                state.ApplicationId,
                changedEventId,
                "{\"value\":2}",
                state.IdempotencyKey));
        var replayedFirstResponse = string.Equals(response.Id, state.InitialMessageId, StringComparison.Ordinal);
        var responseIdentityIsConsistent = replayedFirstResponse
            ? string.Equals(response.EventId, state.InitialEventId, StringComparison.Ordinal)
            : string.Equals(response.EventId, changedEventId, StringComparison.Ordinal);

        await Assert.That(responseIdentityIsConsistent).IsTrue();

        var completedAtUtc = TimeProvider.System.GetUtcNow();
        var result = new ManagedConformanceResult(
            ResultSchemaVersion,
            completedAtUtc,
            SvixDeploymentKind.Managed.ToString(),
            SvixConformanceProfileRegistry.ManagedEnvironment,
            SvixConformanceProfileRegistry.ManagedProviderVersion,
            SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion,
            $"managed-live-{completedAtUtc:yyyy-MM-dd}",
            SelectedTestCount: 2,
            ExecutedCaseCount: ImmediateCaseNames.Length + 1,
            IdempotencyWindowHours: state.IdempotencyWindowHours,
            state.SupportsExactMessageLookup,
            replayedFirstResponse ? "replayed-after-window" : "created-after-window",
            [.. ImmediateCaseNames, "repeat-create-after-documented-window"]);
        var completedResultPath = $"{configuration.ProbePath}.{Guid.CreateVersion7():N}.completed";
        await WritePrivateJsonFileAsync(completedResultPath, result);

        try
        {
            await DeleteApplicationAsync(client, state.ApplicationId);
            ReplaceProbeWithCompletedResult(completedResultPath, configuration.ProbePath);
        }
        finally
        {
            DeleteFileIfPresent(completedResultPath);
        }
    }

    private static async Task VerifyRepeatCreateInsideWindowAsync(SvixClient client)
    {
        await WithDisposableApplicationAsync(client, async app =>
        {
            var idempotencyKey = NewIdentity("managed-inside");
            var first = await ProviderCallAsync(
                "create baseline idempotent message",
                () => CreateMessageAsync(
                    client,
                    app.Id,
                    NewIdentity("managed-event"),
                    "{\"value\":1}",
                    idempotencyKey));
            var replay = await ProviderCallAsync(
                "repeat idempotent message",
                () => CreateMessageAsync(
                    client,
                    app.Id,
                    NewIdentity("managed-changed-event"),
                    "{\"value\":2}",
                    idempotencyKey));

            await Assert.That(string.Equals(replay.Id, first.Id, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(replay.EventId, first.EventId, StringComparison.Ordinal)).IsTrue();
        });
    }

    private static async Task VerifyDuplicateEventIdentityAsync(SvixClient client)
    {
        await WithDisposableApplicationAsync(client, async app =>
        {
            var eventId = NewIdentity("managed-duplicate-event");
            await ProviderCallAsync(
                "create duplicate-event baseline",
                () => CreateMessageAsync(
                    client,
                    app.Id,
                    eventId,
                    "{\"value\":1}",
                    NewIdentity("managed-idem")));
            var samePayloadStatus = await CaptureApiFailureStatusAsync(() => CreateMessageAsync(
                client,
                app.Id,
                eventId,
                "{\"value\":1}",
                NewIdentity("managed-same-payload")));
            var changedPayloadStatus = await CaptureApiFailureStatusAsync(() => CreateMessageAsync(
                client,
                app.Id,
                eventId,
                "{\"value\":2}",
                NewIdentity("managed-changed-payload")));

            await Assert.That(samePayloadStatus).IsEqualTo(409);
            await Assert.That(changedPayloadStatus).IsEqualTo(409);
        });
    }

    private static async Task VerifyAcceptanceTimeoutAsync(
        ManagedConformanceConfiguration configuration,
        SvixClient directClient)
    {
        await WithDisposableApplicationAsync(directClient, async app =>
        {
            var eventId = NewIdentity("managed-acceptance-timeout");
            await using var proxy = await SvixAcceptThenDropProxy.StartAsync(configuration.ServerUri);
            var proxyClient = configuration.CreateCurrentClient(proxy.ServerUrl);
            string? transportFailureType = null;

            try
            {
                await CreateMessageAsync(
                    proxyClient,
                    app.Id,
                    eventId,
                    "{\"value\":1}",
                    NewIdentity("managed-acceptance-timeout"));
            }
            catch (Exception exception)
            {
                transportFailureType = exception.GetType().Name;
            }

            await AwaitAcceptedForwardAsync(proxy.Forwarded);
            var listed = await ProviderCallAsync(
                "list accepted message after dropped response",
                () => directClient.Message.ListAsync(
                    app.Id,
                    new MessageListOptions
                    {
                        Limit = 100,
                        EventTypes = [EventType],
                        WithContent = false
                    },
                    CancellationToken.None));

            await Assert.That(transportFailureType is not null).IsTrue();
            await Assert.That(listed.Data!.Count(message => message.EventId == eventId)).IsEqualTo(1);
        });
    }

    private static async Task VerifyCredentialRotationAsync(
        ManagedConformanceConfiguration configuration,
        SvixClient currentClient)
    {
        await WithDisposableApplicationAsync(currentClient, async app =>
        {
            var idempotencyKey = NewIdentity("managed-credential-rotation");
            var first = await ProviderCallAsync(
                "create message with current credential",
                () => CreateMessageAsync(
                    currentClient,
                    app.Id,
                    NewIdentity("managed-current-token"),
                    "{\"value\":1}",
                    idempotencyKey));
            var rotatedClient = configuration.CreateRotatedClient();
            var second = await ProviderCallAsync(
                "create message with rotated credential",
                () => CreateMessageAsync(
                    rotatedClient,
                    app.Id,
                    NewIdentity("managed-rotated-token"),
                    "{\"value\":2}",
                    idempotencyKey));

            await Assert.That(string.Equals(second.Id, first.Id, StringComparison.Ordinal)).IsFalse();
        });
    }

    private static async Task<bool> VerifyListAndGetConsistencyAsync(SvixClient client)
    {
        var supportsExactMessageLookup = false;
        await WithDisposableApplicationAsync(client, async app =>
        {
            var eventId = NewIdentity("managed-lookup");
            var evidenceTag = NewIdentity("managed-evidence");
            var created = await ProviderCallAsync(
                "create tagged lookup message",
                () => CreateMessageAsync(
                    client,
                    app.Id,
                    eventId,
                    "{\"value\":1}",
                    NewIdentity("managed-lookup"),
                    [evidenceTag]));
            var listed = await ProviderCallAsync(
                "list tagged lookup message",
                () => client.Message.ListAsync(
                    app.Id,
                    new MessageListOptions
                    {
                        Limit = 100,
                        EventTypes = [EventType],
                        Tag = evidenceTag,
                        WithContent = false,
                        After = DateTime.UtcNow.AddMinutes(-5),
                        Before = DateTime.UtcNow.AddMinutes(5)
                    },
                    CancellationToken.None));
            var matches = listed.Data!.Where(message => message.EventId == eventId).ToArray();
            await Assert.That(matches).Count().IsEqualTo(1);
            var match = matches.Single();
            var fetched = await ProviderCallAsync(
                "get tagged lookup message",
                () => client.Message.GetAsync(
                    app.Id,
                    created.Id,
                    new MessageGetOptions { WithContent = false },
                    CancellationToken.None));

            var listIdentityMatches =
                string.Equals(match.Id, created.Id, StringComparison.Ordinal) &&
                string.Equals(match.EventType, EventType, StringComparison.Ordinal) &&
                string.Equals(match.EventId, eventId, StringComparison.Ordinal) &&
                match.Timestamp == created.Timestamp;
            var getIdentityMatches =
                string.Equals(fetched.Id, created.Id, StringComparison.Ordinal) &&
                string.Equals(fetched.EventId, eventId, StringComparison.Ordinal);
            await Assert.That(listIdentityMatches).IsTrue();
            await Assert.That(getIdentityMatches).IsTrue();

            supportsExactMessageLookup =
                match.Tags?.Contains(evidenceTag, StringComparer.Ordinal) == true &&
                fetched.Tags?.Contains(evidenceTag, StringComparer.Ordinal) == true;
        });

        return supportsExactMessageLookup;
    }

    private static async Task SeedExpiryProbeAsync(
        ManagedConformanceConfiguration configuration,
        SvixClient client,
        bool supportsExactMessageLookup)
    {
        var app = await CreateApplicationAsync(client);
        var retainApplicationForVerification = false;
        try
        {
            var idempotencyKey = NewIdentity("managed-expiry");
            var initialEventId = NewIdentity("managed-before-window");
            var initial = await ProviderCallAsync(
                "seed expiry probe message",
                () => CreateMessageAsync(
                    client,
                    app.Id,
                    initialEventId,
                    "{\"value\":1}",
                    idempotencyKey));
            var seededAtUtc = TimeProvider.System.GetUtcNow();
            var profile = SvixConformanceProfileRegistry.All.Single(candidate =>
                candidate.DeploymentKind == SvixDeploymentKind.Managed);
            var state = new ManagedExpiryProbeState(
                ProbeSchemaVersion,
                SvixDeploymentKind.Managed.ToString(),
                profile.Environment,
                profile.ProviderVersion,
                profile.CapabilityPolicyVersion,
                configuration.CredentialVersion,
                app.Id,
                idempotencyKey,
                initial.Id,
                initialEventId,
                seededAtUtc,
                seededAtUtc.Add(profile.IdempotencyWindow).AddMinutes(1),
                profile.IdempotencyWindow.TotalHours,
                ImmediateCaseNames.Length,
                supportsExactMessageLookup,
                ImmediateCaseNames);

            await WriteNewProbeStateAsync(configuration.ProbePath, state);
            retainApplicationForVerification = true;
        }
        finally
        {
            if (!retainApplicationForVerification)
            {
                await DeleteApplicationAsync(client, app.Id);
            }
        }
    }

    private static async Task WithDisposableApplicationAsync(
        SvixClient client,
        Func<ApplicationOut, Task> action)
    {
        var app = await CreateApplicationAsync(client);
        try
        {
            await action(app);
        }
        finally
        {
            await DeleteApplicationAsync(client, app.Id);
        }
    }

    private static Task<ApplicationOut> CreateApplicationAsync(SvixClient client) =>
        ProviderCallAsync(
            "create disposable application",
            () => client.Application.GetOrCreateAsync(
                new ApplicationIn
                {
                    Name = "ISLAMU managed Svix conformance",
                    Uid = NewIdentity("managed-app")
                },
                new ApplicationCreateOptions { IdempotencyKey = NewIdentity("managed-app-idem") },
                CancellationToken.None));

    private static async Task DeleteApplicationAsync(SvixClient client, string applicationId)
    {
        var deleted = await ProviderCallAsync(
            "delete disposable application",
            () => client.Application.DeleteAsync(applicationId, CancellationToken.None));
        await Assert.That(deleted).IsTrue();
    }

    private static Task<MessageOut> CreateMessageAsync(
        SvixClient client,
        string applicationId,
        string eventId,
        string payloadJson,
        string idempotencyKey,
        List<string>? tags = null)
    {
        var message = Message.messageInRaw(
            EventType,
            payloadJson,
            "application/json",
            application: null,
            channels: null,
            eventId: eventId,
            payloadRetentionHours: null,
            payloadRetentionPeriod: 1,
            tags: tags,
            transformationsParams: null);
        return client.Message.CreateAsync(
            applicationId,
            message,
            new MessageCreateOptions { IdempotencyKey = idempotencyKey },
            CancellationToken.None);
    }

    private static async Task<int> CaptureApiFailureStatusAsync(Func<Task<MessageOut>> action)
    {
        try
        {
            await action();
            return 0;
        }
        catch (ApiException exception)
        {
            return exception.ErrorCode;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Managed Svix conformance request failed with {exception.GetType().Name} before an API status was available.");
        }
    }

    private static async Task<T> ProviderCallAsync<T>(string operation, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException exception)
        {
            throw new InvalidOperationException(
                $"Managed Svix conformance operation '{operation}' failed with HTTP {exception.ErrorCode}.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Managed Svix conformance operation '{operation}' failed with {exception.GetType().Name}.");
        }
    }

    private static async Task AwaitAcceptedForwardAsync(Task forwarded)
    {
        try
        {
            await forwarded;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Managed Svix acceptance proxy failed with {exception.GetType().Name}.");
        }
    }

    private static async Task<ManagedExpiryProbeState> ReadProbeStateAsync(
        ManagedConformanceConfiguration configuration)
    {
        ManagedExpiryProbeState? state;
        try
        {
            await using var stream = File.OpenRead(configuration.ProbePath);
            state = await JsonSerializer.DeserializeAsync<ManagedExpiryProbeState>(
                stream,
                JsonOptions,
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException("Managed Svix expiry probe state could not be read safely.");
        }

        if (state is null ||
            state.SchemaVersion != ProbeSchemaVersion ||
            !string.Equals(state.DeploymentKind, SvixDeploymentKind.Managed.ToString(), StringComparison.Ordinal) ||
            !string.Equals(state.Environment, SvixConformanceProfileRegistry.ManagedEnvironment, StringComparison.Ordinal) ||
            !string.Equals(state.ProviderVersion, SvixConformanceProfileRegistry.ManagedProviderVersion, StringComparison.Ordinal) ||
            !string.Equals(
                state.CapabilityPolicyVersion,
                SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion,
                StringComparison.Ordinal) ||
            !string.Equals(state.CredentialVersion, configuration.CredentialVersion, StringComparison.Ordinal) ||
            state.ImmediateExecutedCaseCount != ImmediateCaseNames.Length ||
            !state.ImmediateCases.SequenceEqual(ImmediateCaseNames, StringComparer.Ordinal) ||
            state.VerifyNotBeforeUtc < state.SeededAtUtc.AddHours(state.IdempotencyWindowHours) ||
            !IsSafeIdentifier(state.ApplicationId) ||
            !IsSafeIdentifier(state.IdempotencyKey) ||
            !IsSafeIdentifier(state.InitialMessageId) ||
            !IsSafeIdentifier(state.InitialEventId))
        {
            throw new InvalidOperationException("Managed Svix expiry probe state failed validation.");
        }

        return state;
    }

    private static async Task WriteNewProbeStateAsync(string probePath, ManagedExpiryProbeState state)
    {
        var temporaryPath = $"{probePath}.{Guid.CreateVersion7():N}.pending";
        await WritePrivateJsonFileAsync(temporaryPath, state);
        try
        {
            File.Move(temporaryPath, probePath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Managed Svix expiry probe state could not be committed safely.");
        }
        finally
        {
            DeleteFileIfPresent(temporaryPath);
        }
    }

    private static async Task WritePrivateJsonFileAsync<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path) ??
                throw new InvalidOperationException("Managed Svix conformance path has no parent directory.");
            Directory.CreateDirectory(directory);
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using var stream = new FileStream(path, options);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, CancellationToken.None);
            await stream.FlushAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException("Managed Svix conformance state could not be persisted safely.");
        }
    }

    private static void ReplaceProbeWithCompletedResult(string completedResultPath, string probePath)
    {
        try
        {
            File.Move(completedResultPath, probePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Managed Svix conformance result could not replace the private probe state.");
        }
    }

    private static void EnsureProbeDoesNotExist(string probePath)
    {
        if (File.Exists(probePath))
        {
            throw new InvalidOperationException(
                "Managed Svix conformance probe or completed result already exists; do not overwrite provider evidence.");
        }
    }

    private static void DeleteFileIfPresent(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Managed Svix conformance temporary state could not be removed safely.");
        }
    }

    private static bool IsSafeIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static string NewIdentity(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}";

    private sealed record ManagedConformanceConfiguration(
        string CurrentAuthToken,
        string? RotatedAuthToken,
        string CredentialVersion,
        Uri ServerUri,
        string ProbePath)
    {
        public static ManagedConformanceConfiguration Load(bool requireRotatedToken)
        {
            var currentAuthToken = RequireEnvironmentValue(CurrentAuthTokenVariable);
            var rotatedAuthToken = requireRotatedToken
                ? RequireEnvironmentValue(RotatedAuthTokenVariable)
                : null;
            if (rotatedAuthToken is not null &&
                string.Equals(currentAuthToken, rotatedAuthToken, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Managed Svix conformance requires two distinct active API keys.");
            }

            var credentialVersion = RequireEnvironmentValue(CredentialVersionVariable);
            if (!IsSafeIdentifier(credentialVersion) || credentialVersion.Length > 64)
            {
                throw new InvalidOperationException(
                    "Managed Svix conformance credential version must be a non-secret 1-64 character identifier.");
            }

            var serverUri = ParseServerUri(System.Environment.GetEnvironmentVariable(BaseUrlVariable));
            var probePathValue = RequireEnvironmentValue(ProbePathVariable);
            if (!Path.IsPathFullyQualified(probePathValue) ||
                !string.Equals(Path.GetExtension(probePathValue), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Managed Svix conformance probe path must be an absolute JSON file path outside source control.");
            }

            return new ManagedConformanceConfiguration(
                currentAuthToken,
                rotatedAuthToken,
                credentialVersion,
                serverUri,
                Path.GetFullPath(probePathValue));
        }

        public SvixClient CreateCurrentClient(string? serverUrl = null) =>
            CreateClient(CurrentAuthToken, serverUrl);

        public SvixClient CreateRotatedClient() =>
            CreateClient(
                RotatedAuthToken ??
                throw new InvalidOperationException("Managed Svix rotated credential is unavailable."),
                serverUrl: null);

        private SvixClient CreateClient(string authToken, string? serverUrl) =>
            new(
                authToken,
                new SvixOptions(
                    serverUrl: serverUrl ?? ServerUri.AbsoluteUri.TrimEnd('/'),
                    timeoutMilliseconds: 10_000,
                    retryScheduleMilliseconds: []),
                NullLogger<SvixClient>.Instance);

        private static string RequireEnvironmentValue(string variableName)
        {
            var value = System.Environment.GetEnvironmentVariable(variableName)?.Trim();
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"{variableName} is required for managed Svix conformance.")
                : value;
        }

        private static Uri ParseServerUri(string? configuredValue)
        {
            var value = string.IsNullOrWhiteSpace(configuredValue)
                ? DefaultServerUrl
                : configuredValue.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    "Managed Svix conformance base URL must be an absolute HTTPS URL without credentials, query, or fragment.");
            }

            return uri;
        }
    }

    private sealed record ManagedExpiryProbeState(
        int SchemaVersion,
        string DeploymentKind,
        string Environment,
        string ProviderVersion,
        string CapabilityPolicyVersion,
        string CredentialVersion,
        string ApplicationId,
        string IdempotencyKey,
        string InitialMessageId,
        string InitialEventId,
        DateTimeOffset SeededAtUtc,
        DateTimeOffset VerifyNotBeforeUtc,
        double IdempotencyWindowHours,
        int ImmediateExecutedCaseCount,
        bool SupportsExactMessageLookup,
        IReadOnlyList<string> ImmediateCases);

    private sealed record ManagedConformanceResult(
        int SchemaVersion,
        DateTimeOffset CompletedAtUtc,
        string DeploymentKind,
        string Environment,
        string ProviderVersion,
        string CapabilityPolicyVersion,
        string EvidenceRevision,
        int SelectedTestCount,
        int ExecutedCaseCount,
        double IdempotencyWindowHours,
        bool SupportsExactMessageLookup,
        string AfterWindowBehavior,
        IReadOnlyList<string> Cases);
}
