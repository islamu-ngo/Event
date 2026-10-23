using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;

namespace Event.Distributed.IntegrationTests;

internal static class K6LoadRunner
{
    internal const int DurationSeconds = 5;
    internal const int VirtualUsers = 4;
    internal const int OfferedRate = 8;
    private const string AdmittedExecutableDigest = "E001BBF39BA67424A85C9C8516440BA5C73ECF7A6021BE96C76194380C15F260";
    private const string WorkloadId = "owned-language-read-v1";
    private const long MemoryBudgetBytes = 512 * 1024 * 1024;
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };
    private static readonly string[] SummaryFields =
    [
        "schema", "journey", "profile", "mode", "attempts", "responses", "correct", "iterations", "httpRequests",
        "correctnessRate", "httpFailureRate", "dropped", "p50Milliseconds", "p95Milliseconds", "p99Milliseconds"
    ];

    internal static K6LoadBudget BudgetFor(K6LoadProfile profile) => profile switch
    {
        K6LoadProfile.ClosedLookup => new("closed-lookup", DurationSeconds, 0, VirtualUsers),
        K6LoadProfile.OpenLookup => new("open-lookup", DurationSeconds, OfferedRate, VirtualUsers),
        K6LoadProfile.LowRate => new("low-rate", 10, 1, 4),
        K6LoadProfile.Steady => new("steady", 30, 4, 4),
        K6LoadProfile.Spike => new("spike", 5, 8, 4),
        K6LoadProfile.Recovery => new("recovery", 10, 1, 4),
        K6LoadProfile.Soak => new("soak", 120, 2, 4),
        K6LoadProfile.ConstrainedWrite => new("constrained-write", 20, 1, 1),
        _ => throw new InvalidOperationException("load_profile_not_admitted")
    };

    internal static Uri ValidateEventTarget(Uri ownedOrigin, Uri target, Guid readId, Guid writeId, string searchTerm,
        K6Journey journey)
    {
        string expectedPath = journey switch
        {
            K6Journey.EventRead => $"/api/Event/{readId:D}",
            K6Journey.EventSearch => "/api/Event?pageSize=20&searchTerm=" + Uri.EscapeDataString(searchTerm),
            K6Journey.EventWrite => $"/api/Event/{writeId:D}",
            _ => throw new InvalidOperationException("load_event_target_not_admitted")
        };
        if (ownedOrigin.Scheme != "http" || ownedOrigin.Host != "127.0.0.1" || ownedOrigin.IsDefaultPort ||
            ownedOrigin.AbsolutePath != "/" || ownedOrigin.Query.Length != 0 || ownedOrigin.UserInfo.Length != 0 ||
            ownedOrigin.Fragment.Length != 0 || readId == Guid.Empty || writeId == Guid.Empty || readId == writeId ||
            string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length > 64 ||
            !searchTerm.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ||
            target.AbsoluteUri != new Uri(ownedOrigin, expectedPath).AbsoluteUri)
            throw new InvalidOperationException("load_event_target_not_admitted");
        return target;
    }

    internal static void ValidateJourneyBudget(K6Journey journey, K6LoadProfile profile)
    {
        bool admitted = journey switch
        {
            K6Journey.LanguageLookup => profile is K6LoadProfile.ClosedLookup or K6LoadProfile.OpenLookup,
            K6Journey.EventRead or K6Journey.EventSearch => profile is K6LoadProfile.LowRate or K6LoadProfile.Steady or
                K6LoadProfile.Spike or K6LoadProfile.Recovery or K6LoadProfile.Soak,
            K6Journey.EventWrite => profile == K6LoadProfile.ConstrainedWrite,
            _ => false
        };
        if (!admitted)
            throw new InvalidOperationException("load_journey_budget_not_admitted");
    }

    private static string JourneyName(K6Journey journey) => journey switch
    {
        K6Journey.LanguageLookup => "public-language",
        K6Journey.EventRead => "event-read",
        K6Journey.EventSearch => "event-search",
        K6Journey.EventWrite => "event-write",
        _ => throw new InvalidOperationException("load_journey_not_admitted")
    };

    internal static async Task<K6NativeDataset> RunNativeControlAsync(OwnedAppHostFixture fixture, string topology, CancellationToken cancellationToken)
    {
        DateTimeOffset seedStartedAt = DateTimeOffset.UtcNow;
        if (topology is not ("Split" or "Standalone"))
            throw new InvalidOperationException("load_topology_not_admitted");
        using var transport = CreateNativeTransport();
        using var api = CreateNativeClient(fixture, topology, transport);
        using (var initial = await ReadNativeJsonAsync(api, HttpMethod.Post, "/api/auth/local/login", new
        {
            identifier = fixture.BootstrapIdentifier, password = fixture.BootstrapPassword
        }, cancellationToken))
        {
            if (initial.RootElement.GetProperty("success").GetBoolean())
                throw new InvalidOperationException("load_bootstrap_replacement_required");
            string challenge = initial.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString()
                ?? throw new InvalidOperationException("load_bootstrap_challenge_missing");
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", challenge);
            using var replaced = await SendNativeAsync(api, HttpMethod.Post, "/api/auth/local/credential-replacement",
                new { newPassword = fixture.BootstrapReplacementPassword }, HttpStatusCode.NoContent, cancellationToken);
        }
        api.DefaultRequestHeaders.Authorization = null;
        using (var login = await ReadNativeJsonAsync(api, HttpMethod.Post, "/api/auth/local/login", new
        {
            identifier = fixture.BootstrapIdentifier, password = fixture.BootstrapReplacementPassword
        }, cancellationToken))
        {
            if (!login.RootElement.GetProperty("success").GetBoolean())
                throw new InvalidOperationException("load_ordinary_login_required");
            string token = login.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("load_access_token_missing");
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        string tenantSlug = "load-" + fixture.RunId;
        Guid tenantId;
        using (var tenant = await ReadNativeJsonAsync(api, HttpMethod.Post, "/api/Tenant", new
        {
            fullName = "Owned load community", slug = tenantSlug, isActive = true, assignCurrentUserAsTenantAdmin = true,
            directoryOperatorIdentity = new
            {
                publicName = "Owned load community", legalName = "Owned load community", operatorKindCode = "registered_organization",
                jurisdictionCountryCode = "BE", registrationIdentifier = "load-" + fixture.RunId,
                publicContactEmail = "assurance@example.invalid", legalNoticeUrl = "https://example.invalid/legal",
                termsUrl = "https://example.invalid/terms", privacyUrl = "https://example.invalid/privacy"
            }
        }, cancellationToken))
            tenantId = tenant.RootElement.GetProperty("id").GetGuid();
        api.DefaultRequestHeaders.Add("X-Tenant-Slug", tenantSlug);
        using (var tenant = await ReadNativeJsonAsync(api, HttpMethod.Get, $"/api/Tenant/{tenantId:D}", null, cancellationToken))
            if (!tenant.RootElement.GetProperty("isActive").GetBoolean() || tenant.RootElement.GetProperty("slug").GetString() != tenantSlug)
                throw new InvalidOperationException("load_active_tenant_required");

        string readTitle = "Load search " + fixture.RunId;
        const string writeTitle = "Load write candidate";
        const string controlTitle = "Load control unchanged";
        Guid readId = await CreateNativeEventAsync(api, readTitle, cancellationToken);
        Guid writeId = await CreateNativeEventAsync(api, writeTitle, cancellationToken);
        Guid controlId = await CreateNativeEventAsync(api, controlTitle, cancellationToken);
        using (var read = await ReadNativeJsonAsync(api, HttpMethod.Get, $"/api/Event/{readId:D}/management-detail", null, cancellationToken))
        using (await ReadNativeJsonAsync(api, HttpMethod.Post, $"/api/Event/{readId:D}/publish", new
        {
            expectedConcurrencyStamp = read.RootElement.GetProperty("concurrencyStamp").GetGuid()
        }, cancellationToken)) { }

        using var publicApi = CreateNativeClient(fixture, topology, transport);
        publicApi.DefaultRequestHeaders.Add("X-Tenant-Slug", tenantSlug);
        using (var discovery = await ReadNativeJsonAsync(publicApi, HttpMethod.Get,
            "/api/Event?pageSize=20&searchTerm=" + Uri.EscapeDataString(fixture.RunId), null, cancellationToken))
        {
            var items = discovery.RootElement.GetProperty("_embedded").GetProperty("items");
            if (items.GetArrayLength() != 1 || items[0].GetProperty("event").GetProperty("id").GetGuid() != readId ||
                items[0].GetProperty("event").GetProperty("title").GetString() != readTitle)
                throw new InvalidOperationException("load_public_search_dataset_invalid");
        }

        using var before = await ReadNativeJsonAsync(api, HttpMethod.Get, $"/api/Event/{writeId:D}/management-detail", null, cancellationToken);
        using var controlBefore = await ReadNativeJsonAsync(api, HttpMethod.Get, $"/api/Event/{controlId:D}/management-detail", null, cancellationToken);
        Guid originalStamp = before.RootElement.GetProperty("concurrencyStamp").GetGuid();
        string settledTitle = "Load settled " + fixture.RunId + "-0001";
        using (await SendNativeAsync(api, HttpMethod.Patch, $"/api/Event/{writeId:D}", new { title = new { value = settledTitle } },
            HttpStatusCode.OK, cancellationToken, originalStamp)) { }
        using (await SendNativeAsync(api, HttpMethod.Patch, $"/api/Event/{writeId:D}", new { title = new { value = "Stale write must not persist" } },
            HttpStatusCode.Conflict, cancellationToken, originalStamp)) { }
        using var after = await ReadNativeJsonAsync(api, HttpMethod.Get, $"/api/Event/{writeId:D}/management-detail", null, cancellationToken);
        using var controlAfter = await ReadNativeJsonAsync(api, HttpMethod.Get, $"/api/Event/{controlId:D}/management-detail", null, cancellationToken);
        using var readAfter = await ReadNativeJsonAsync(publicApi, HttpMethod.Get, $"/api/Event/{readId:D}", null, cancellationToken);
        if (before.RootElement.GetProperty("title").GetString() != writeTitle ||
            after.RootElement.GetProperty("title").GetString() != settledTitle ||
            after.RootElement.GetProperty("concurrencyStamp").GetGuid() == originalStamp ||
            controlBefore.RootElement.GetProperty("title").GetString() != controlTitle ||
            controlAfter.RootElement.GetProperty("title").GetString() != controlTitle ||
            after.RootElement.GetProperty("tenantId").GetGuid() != tenantId ||
            controlAfter.RootElement.GetProperty("tenantId").GetGuid() != tenantId ||
            readAfter.RootElement.GetProperty("id").GetGuid() != readId ||
            readAfter.RootElement.GetProperty("title").GetString() != readTitle ||
            controlAfter.RootElement.GetProperty("concurrencyStamp").GetGuid() != controlBefore.RootElement.GetProperty("concurrencyStamp").GetGuid())
            throw new InvalidOperationException("load_native_settled_state_invalid");
        await CaptureRuntimeMetricInventoryAsync(fixture, topology, cancellationToken);
        return new K6NativeDataset(tenantId, tenantSlug, readId, readTitle, writeId, controlId, controlTitle,
            controlAfter.RootElement.GetProperty("concurrencyStamp").GetGuid(), seedStartedAt, DateTimeOffset.UtcNow);
    }

    private static async Task CaptureRuntimeMetricInventoryAsync(OwnedAppHostFixture fixture, string topology,
        CancellationToken cancellationToken)
    {
        K6MetricInventoryStatus status = K6MetricInventoryStatus.EndpointUnavailable;
        K6MetricDefinition[] definitions = [];
        try
        {
            using var transport = CreateNativeTransport();
            using var operations = CreateNativeClient(fixture, topology, transport, "operations");
            operations.Timeout = TimeSpan.FromSeconds(3);
            using var response = await operations.GetAsync("/metrics", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                status = K6MetricInventoryStatus.UnsupportedMediaType;
                if (response.Content.Headers.ContentType?.MediaType is "text/plain" or "application/openmetrics-text")
                {
                    using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                    using var stream = await response.Content.ReadAsStreamAsync(readDeadline.Token);
                    using var body = new MemoryStream();
                    byte[] buffer = new byte[4096];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, readDeadline.Token)) != 0)
                    {
                        if (body.Length + count > 4 * 1024 * 1024)
                            throw new InvalidOperationException("load_runtime_metric_inventory_limit");
                        body.Write(buffer, 0, count);
                    }
                    definitions = ParseRuntimeMetricInventory(Encoding.UTF8.GetString(body.GetBuffer(), 0, checked((int)body.Length)));
                    status = K6MetricInventoryStatus.MetadataOnly;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = K6MetricInventoryStatus.TimedOut;
        }
        catch (HttpRequestException)
        {
            status = K6MetricInventoryStatus.EndpointUnavailable;
        }
        catch (IOException)
        {
            status = K6MetricInventoryStatus.EndpointUnavailable;
        }
        catch (InvalidOperationException)
        {
            status = K6MetricInventoryStatus.InvalidMetadata;
        }
        await WriteRuntimeMetricInventoryAsync(Path.Combine(EvidenceDirectory(fixture), $"runtime-metric-inventory-{topology}.json"),
            status, definitions, cancellationToken);
    }

    internal static K6MetricDefinition[] ParseRuntimeMetricInventory(string exposition)
    {
        if (exposition.Length > 4 * 1024 * 1024)
            throw new InvalidOperationException("load_runtime_metric_inventory_limit");
        var definitions = new SortedDictionary<string, (string? Kind, string? Unit)>(StringComparer.Ordinal);
        using var reader = new StringReader(exposition);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("# TYPE ", StringComparison.Ordinal) && !line.StartsWith("# UNIT ", StringComparison.Ordinal))
                continue;
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 4 || fields[2] is not ("dotnet_process_cpu_time_seconds_total" or
                "dotnet_process_memory_working_set_bytes" or "process_cpu_seconds_total" or
                "process_memory_usage_bytes" or "process_start_time_seconds" or "process_uptime_seconds"))
                continue;
            var previous = definitions.GetValueOrDefault(fields[2]);
            if (fields[1] == "TYPE")
            {
                if (fields[3] is not ("counter" or "gauge" or "histogram" or "summary" or "untyped" or "info" or "stateset") ||
                    previous.Kind is { } kind && kind != fields[3])
                    throw new InvalidOperationException("load_runtime_metric_metadata_invalid");
                previous.Kind = fields[3];
            }
            else
            {
                if (fields[3].Length > 32 || !fields[3].All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ||
                    previous.Unit is { } unit && unit != fields[3])
                    throw new InvalidOperationException("load_runtime_metric_metadata_invalid");
                previous.Unit = fields[3];
            }
            definitions[fields[2]] = previous;
        }
        return definitions.Where(definition => definition.Value.Kind is not null)
            .Select(definition => new K6MetricDefinition(definition.Key, definition.Value.Kind!,
                definition.Value.Unit is "seconds" or "bytes" ? definition.Value.Unit : null)).ToArray();
    }

    internal static Task WriteRuntimeMetricInventoryAsync(string path, K6MetricInventoryStatus status,
        IReadOnlyList<K6MetricDefinition> definitions, CancellationToken cancellationToken) => File.WriteAllTextAsync(path,
        JsonSerializer.Serialize(new
        {
            Schema = 1, Status = status.ToString(), Scope = "runtime metric metadata only", CapacityEvidenceEligible = false,
            UnavailableMeasurements = new[] { "api_cpu_numeric", "api_working_set_numeric", "api_reset_identity", "database_pressure", "queue_backlog" },
            MetricDefinitions = definitions
        }, EvidenceJsonOptions), cancellationToken);

    private static string EvidenceDirectory(OwnedAppHostFixture fixture)
    {
        string directory = Path.Combine(Path.GetTempPath(), "testing-assurance-modernization", "continuation", "load", fixture.RunId);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }

    private static HttpClientHandler CreateNativeTransport() =>
        new() { AllowAutoRedirect = false, UseProxy = false, UseCookies = false };

    private static HttpClient CreateNativeClient(OwnedAppHostFixture fixture, string topology, HttpMessageHandler transport,
        string endpoint = "http")
    {
        if (topology is not ("Split" or "Standalone") || endpoint is not ("http" or "operations"))
            throw new InvalidOperationException("load_owned_endpoint_not_admitted");
        using var bound = fixture.CreateHttpClient(topology == "Split" ? "explore-api" : "event-standalone", endpoint);
        var origin = bound.BaseAddress ?? throw new InvalidOperationException("load_owned_endpoint_missing");
        if (origin.Scheme != "http" || !origin.IsLoopback || origin.IsDefaultPort || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new InvalidOperationException("load_owned_endpoint_not_admitted");
        HttpClient? client = null;
        try
        {
            client = new HttpClient(transport, disposeHandler: false);
            client.BaseAddress = new UriBuilder(origin) { Host = IPAddress.Loopback.ToString() }.Uri;
            client.Timeout = TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            return client;
        }
        catch
        {
            client?.Dispose();
            throw;
        }
    }

    private static GlobalExploreDbContextFactory CreateObservationFactory(OwnedAppHostFixture fixture)
    {
        Uri endpoint = fixture.Application.GetEndpoint("postgres", "tcp");
        var authority = fixture.EnvironmentAuthority;
        return new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global, Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = endpoint.Host, Port = endpoint.Port, Database = authority["DATABASE_NAME"],
            Schema = authority["DATABASE_SCHEMA"] ?? throw new InvalidOperationException("load_owned_observer_authority_missing"),
            Username = authority["DATABASE_GLOBAL_USERNAME"],
            Password = authority["DATABASE_GLOBAL_PASSWORD"], TlsMode = PrimaryDatabaseTlsMode.Disabled
        }, poolSize: 2);
    }

    private static async Task<(K6ObservationStatus Status, K6DatabasePressure? Value)> ReadDatabasePressureAsync(
        GlobalExploreDbContextFactory factory, string expectedDatabase, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await using var context = await factory.CreateDbContextAsync(deadline.Token);
            // This fixed provider statistics primitive has no EF entity equivalent. Native Global login admission still runs.
            await context.Database.OpenConnectionAsync(deadline.Token);
            if (!Version.TryParse(context.Database.GetDbConnection().ServerVersion, out var serverVersion))
                throw new InvalidOperationException("load_database_version_unavailable");
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandTimeout = 2;
            command.CommandText = """
                SELECT current_database() = @expected_database, numbackends, xact_commit, xact_rollback,
                       blks_read, blks_hit, temp_files, temp_bytes, deadlocks, stats_reset
                FROM pg_catalog.pg_stat_database WHERE datname = current_database()
                """;
            var expected = command.CreateParameter();
            expected.ParameterName = "expected_database";
            expected.Value = expectedDatabase;
            command.Parameters.Add(expected);
            await using var rows = await command.ExecuteReaderAsync(deadline.Token);
            if (!await rows.ReadAsync(deadline.Token) || !rows.GetBoolean(0))
                throw new InvalidOperationException("load_database_observation_scope_invalid");
            var value = new K6DatabasePressure(serverVersion.Major, serverVersion.Minor, rows.GetInt32(1), rows.GetInt64(2), rows.GetInt64(3),
                rows.GetInt64(4), rows.GetInt64(5), rows.GetInt64(6), rows.GetInt64(7), rows.GetInt64(8),
                rows.IsDBNull(9) ? null : rows.GetFieldValue<DateTime>(9));
            if (await rows.ReadAsync(deadline.Token) || value.Backends < 0 ||
                new[] { value.Commits, value.Rollbacks, value.BlocksRead, value.BufferHits, value.TempFiles,
                    value.TempBytes, value.Deadlocks }.Any(number => number < 0))
                throw new InvalidOperationException("load_database_observation_invalid");
            return (K6ObservationStatus.Available, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (K6ObservationStatus.TimedOut, null);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            return (K6ObservationStatus.Unavailable, null);
        }
    }

    private static async Task<(K6ObservationStatus Status, K6QueuePressure? Value)> ReadQueuePressureAsync(
        GlobalExploreDbContextFactory factory, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            DateTime now = DateTime.UtcNow;
            var queue = await new QueueDrainHealthRepository(factory).GetSnapshotAsync(now, now.AddMinutes(-5), deadline.Token);
            var email = await new EmailDispatchHealthSnapshotReader(factory).ReadAsync(now, now.AddMinutes(-5), 1, deadline.Token);
            // Project aggregate counts only; never serialize the native tenant backlog sample or authority settings.
            var value = new K6QueuePressure(queue.IntegrationDue, queue.IntegrationStale, queue.IntegrationAmbiguous,
                queue.IncomingDue, queue.IncomingStale, queue.BulkReplayQueued, queue.BulkReplayExecuting,
                queue.ProviderPublicationDue, queue.ProviderPublicationStale, queue.ProviderPublicationUnknown,
                queue.PdsDue, queue.PdsStale, queue.PdsDeadLettered, email.DueDispatchCount, email.RetryScheduledCount,
                email.StaleProcessingCount, email.DeadLetteredCount, email.UnknownCount, email.ParkedCount);
            return (K6ObservationStatus.Available, value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (K6ObservationStatus.TimedOut, null);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            return (K6ObservationStatus.Unavailable, null);
        }
    }

    private static async Task<Guid> CreateNativeEventAsync(HttpClient api, string title, CancellationToken cancellationToken)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddDays(7);
        using var created = await ReadNativeJsonAsync(api, HttpMethod.Post, "/api/Event", new
        {
            title, description = "Owned load correctness fixture.", eventFormatId = 2, eventTimeZoneId = "UTC",
            participationConfiguration = new { participationHandlingModeId = 1, advanceRegistrationObligationId = 1 },
            sessions = new[] { new { tempKey = "load-session", title = "Load control session", startTime = start, endTime = start.AddHours(1), endTimeType = 0 } }
        }, cancellationToken, HttpStatusCode.Created);
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<JsonDocument> ReadNativeJsonAsync(HttpClient api, HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendNativeAsync(api, method, path, body, expected, cancellationToken);
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendNativeAsync(HttpClient api, HttpMethod method, string path, object? body,
        HttpStatusCode expected, CancellationToken cancellationToken, Guid? concurrencyStamp = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (method != HttpMethod.Get)
            request.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        if (concurrencyStamp is { } stamp)
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
        var response = await api.SendAsync(request, cancellationToken);
        if (response.StatusCode == expected)
            return response;
        int actual = (int)response.StatusCode;
        response.Dispose();
        throw new InvalidOperationException($"load_native_control_status_invalid:{method.Method}:{(int)expected}:{actual}");
    }

    internal static async Task RunProgrammeAsync(OwnedAppHostFixture fixture, string topology, CancellationToken cancellationToken)
    {
        var dataset = await RunNativeControlAsync(fixture, topology, cancellationToken);
        string executable = Environment.GetEnvironmentVariable("ISLAMU_K6_PATH") ?? "";
        var versionInfo = StartInfo(executable);
        versionInfo.ArgumentList.Add("version");
        string version = await ReadVersionAsync(versionInfo, cancellationToken);
        string script = Path.Combine(AppContext.BaseDirectory, "Workloads", "owned-runtime.js");
        string workloadDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script, cancellationToken)));
        string candidateAssemblyDigest;
        using (var assembly = File.OpenRead(typeof(K6LoadRunner).Assembly.Location))
            candidateAssemblyDigest = Convert.ToHexString(await SHA256.HashDataAsync(assembly, cancellationToken));
        var baselineKeys = new Dictionary<K6Journey, string>();
        using var transport = CreateNativeTransport();
        using var publicApi = CreateNativeClient(fixture, topology, transport);
        publicApi.DefaultRequestHeaders.Add("X-Tenant-Slug", dataset.TenantSlug);
        var ownedOrigin = new UriBuilder(publicApi.BaseAddress ?? throw new InvalidOperationException("load_owned_endpoint_missing"))
            { Host = IPAddress.Loopback.ToString(), Path = "/" }.Uri;
        var warmup = new List<double>();
        for (int index = 0; index < 3; index++)
        {
            long start = Stopwatch.GetTimestamp();
            await VerifyPublicDatasetAsync(publicApi, dataset, fixture.RunId, cancellationToken);
            warmup.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        string directory = Path.Combine(fixture.Root, "k6-events");
        Directory.CreateDirectory(directory);
        string configuration = Path.Combine(directory, "config.json");
        await File.WriteAllTextAsync(configuration, "{}", cancellationToken);
        var factory = CreateObservationFactory(fixture);
        long programmeStartedTimestamp = Stopwatch.GetTimestamp();
        DateTimeOffset programmeObservedStartAtUtc = DateTimeOffset.UtcNow;
        var observations = new List<K6PressureObservation>();
        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task sampler = SamplePressureAsync(factory, fixture.EnvironmentAuthority["DATABASE_NAME"]
            ?? throw new InvalidOperationException("load_owned_observer_authority_missing"), programmeStartedTimestamp, observations, sampling.Token);
        bool completed = false;
        Exception? failure = null;
        try
        {
            foreach (var journey in new[] { K6Journey.EventRead, K6Journey.EventSearch })
            {
                foreach (var profile in new[] { K6LoadProfile.LowRate, K6LoadProfile.Steady })
                    await RunEventProfileAsync(profile, journey, 0, null);
                var spike = await RunEventProfileAsync(K6LoadProfile.Spike, journey, 0, null);
                var recovery = await RunEventProfileAsync(K6LoadProfile.Recovery, journey, 0, null);
                await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(fixture), $"spike-recovery-{topology}-{JourneyName(journey)}.json"),
                    JsonSerializer.Serialize(new
                    {
                        Schema = 1, Transition = "spike_then_recovery_with_observed_gap", Journey = JourneyName(journey),
                        GapMilliseconds = Stopwatch.GetElapsedTime(spike.ExitedTimestamp, recovery.StartedTimestamp).TotalMilliseconds,
                        Observation = "parent-observed process exit to next process-start return; sampler remains active",
                        CapacityEvidenceEligible = false
                    }, EvidenceJsonOptions), cancellationToken);
                await RunEventProfileAsync(K6LoadProfile.Soak, journey, 0, null);
            }

            // The configured native profile intentionally has a five-minute JWT lifetime. Renew through genuine Local login.
            using var writer = CreateNativeClient(fixture, topology, transport);
            writer.DefaultRequestHeaders.Add("X-Tenant-Slug", dataset.TenantSlug);
            await LoginAdministratorAsync(writer, fixture, cancellationToken);
            await VerifySettledDatasetAsync(writer, publicApi, dataset, "Load settled " + fixture.RunId + "-0001",
                fixture.RunId, cancellationToken);
            string token = writer.DefaultRequestHeaders.Authorization?.Parameter
                ?? throw new InvalidOperationException("load_access_token_missing");
            await RunEventProfileAsync(K6LoadProfile.ConstrainedWrite, K6Journey.EventWrite, 0, token);
            await VerifySettledDatasetAsync(writer, publicApi, dataset, "Load serial " + fixture.RunId + "-0020",
                fixture.RunId, cancellationToken);

            // Repetitions retain candidates with identical settings; this does not promote them or infer host quiescence.
            for (int repetition = 1; repetition <= 3; repetition++)
                foreach (var journey in new[] { K6Journey.EventRead, K6Journey.EventSearch })
                    await RunEventProfileAsync(K6LoadProfile.Steady, journey, repetition, null);
            await LoginAdministratorAsync(writer, fixture, cancellationToken);
            await VerifySettledDatasetAsync(writer, publicApi, dataset, "Load serial " + fixture.RunId + "-0020",
                fixture.RunId, cancellationToken);
            completed = true;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            sampling.Cancel();
            try { await sampler; }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException("load_pressure_finalization_failed", failure, exception);
            }
            try
            {
                await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(fixture), $"pressure-{topology}.json"),
                JsonSerializer.Serialize(new
                {
                    Schema = 1, Completed = completed, CapacityEvidenceEligible = false,
                    ProgrammeObservedStartAtUtc = programmeObservedStartAtUtc,
                    Scope = "continuous run-owned Global statistics and native queue health; API numeric series remain unpinned",
                    UnavailableMeasurements = new[] { "api_cpu_numeric", "api_working_set_numeric", "api_reset_identity" },
                    CounterUnits = "transactions, PostgreSQL blocks, files, bytes, deadlocks; cumulative since StatisticsResetAtUtc",
                    GaugeUnits = "backends and queue item counts", StaleQueueAgeMinutes = 5,
                    Sampling = "actual monotonic offsets; up to three seconds each for DB and queue observations, then one-second delay",
                    SamplingLimitReached = observations.Count >= 900, Observations = observations
                }, EvidenceJsonOptions), CancellationToken.None);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException("load_pressure_finalization_failed", failure, exception);
            }
        }
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();

        async Task<K6ProcessMeasurement> RunEventProfileAsync(K6LoadProfile profile, K6Journey journey, int repetition, string? token)
        {
            ValidateJourneyBudget(journey, profile);
            var budget = BudgetFor(profile);
            string relative = journey == K6Journey.EventSearch
                ? "/api/Event?pageSize=20&searchTerm=" + Uri.EscapeDataString(fixture.RunId)
                : $"/api/Event/{(journey == K6Journey.EventWrite ? dataset.WriteId : dataset.ReadId):D}";
            var target = ValidateEventTarget(ownedOrigin, new Uri(ownedOrigin, relative), dataset.ReadId, dataset.WriteId, fixture.RunId, journey);
            string name = $"{JourneyName(journey)}-{budget.Name}-{repetition}";
            string summary = Path.Combine(directory, name + ".json");
            var startInfo = StartInfo(executable);
            foreach (string argument in new[] { "run", "--config", configuration, "--quiet", "--no-color", "--no-usage-report", "--include-system-env-vars=true", script })
                startInfo.ArgumentList.Add(argument);
            startInfo.WorkingDirectory = directory;
            startInfo.Environment["LOAD_ORIGIN"] = ownedOrigin.AbsoluteUri.TrimEnd('/');
            startInfo.Environment["LOAD_TARGET"] = target.AbsoluteUri;
            startInfo.Environment["LOAD_JOURNEY"] = JourneyName(journey);
            startInfo.Environment["LOAD_RUN_ID"] = fixture.RunId;
            startInfo.Environment["LOAD_TENANT"] = dataset.TenantSlug;
            startInfo.Environment["LOAD_READ_ID"] = dataset.ReadId.ToString("D");
            startInfo.Environment["LOAD_WRITE_ID"] = dataset.WriteId.ToString("D");
            startInfo.Environment["LOAD_PROFILE"] = budget.Name;
            startInfo.Environment["LOAD_SUMMARY"] = summary;
            if (token is not null)
                startInfo.Environment["LOAD_TOKEN"] = token;
            K6ProcessMeasurement measurement;
            try
            {
                await ValidateExecutableAsync(executable, cancellationToken);
                measurement = await RunWithRejectedEvidenceAsync(startInfo, summary,
                    Path.Combine(EvidenceDirectory(fixture), $"rejected-{topology}-{name}.json"), profile, journey, budget.ProcessDeadline, cancellationToken);
            }
            finally
            {
                startInfo.Environment.Remove("LOAD_TOKEN");
            }
            ValidateGenerator(measurement.GeneratorSamples, measurement.GeneratorPeakCpuRatio, measurement.GeneratorPeakRssBytes);
            await VerifyPublicDatasetAsync(publicApi, dataset, fixture.RunId, cancellationToken);
            var comparison = new
            {
                CandidateAssemblyDigest = candidateAssemblyDigest, ExecutableDigest = AdmittedExecutableDigest,
                WorkloadDigest = workloadDigest, Topology = topology, ProviderProfile = "owned PostgreSQL 18 native purpose logins",
                Journey = JourneyName(journey), Profile = budget.Name, budget.DurationSeconds, budget.OfferedRate, budget.MaximumVirtualUsers,
                DatasetGeneration = "three InformationOnly events; sessions UTC now plus seven days; one published read, distinct draft writer and control",
                WarmupGeneration = "three paired typed public event reads and searches, excluded from measured requests",
                CacheProfile = "production cache policy retained",
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                LogicalProcessors = Environment.ProcessorCount, TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
            };
            string comparisonDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(comparison)));
            if (repetition > 0)
            {
                if (baselineKeys.TryGetValue(journey, out string? firstKey) && firstKey != comparisonDigest)
                    throw new InvalidOperationException("load_baseline_candidates_not_comparable");
                baselineKeys[journey] = comparisonDigest;
            }
            await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(fixture), $"event-load-{topology}-{name}.json"),
                JsonSerializer.Serialize(new
                {
                    Schema = 2, Candidate = true, CapacityEvidenceEligible = false, BaselineRepetition = repetition,
                    StartedOffsetMilliseconds = Stopwatch.GetElapsedTime(programmeStartedTimestamp, measurement.StartedTimestamp).TotalMilliseconds,
                    ExitedOffsetMilliseconds = Stopwatch.GetElapsedTime(programmeStartedTimestamp, measurement.ExitedTimestamp).TotalMilliseconds,
                    ComparisonDigest = comparisonDigest, Comparison = comparison,
                    ToolVersion = version, ExecutableDigest = AdmittedExecutableDigest, WorkloadDigest = workloadDigest,
                    WorkloadId = "owned-information-only-events-v1", Topology = topology, Provider = "PostgreSql",
                    Journey = JourneyName(journey), Profile = budget.Name, budget.DurationSeconds, budget.OfferedRate,
                    budget.MaximumVirtualUsers, WarmupMilliseconds = warmup,
                    Dataset = new { Events = 3, Published = 1, InformationOnly = 3, RegistrationOrMoneyOperations = 0,
                        dataset.SeedStartedAtUtc, dataset.SeedCompletedAtUtc },
                    CacheProfile = "three typed public read/search warmups; production cache policy retained",
                    LatencyScope = journey == K6Journey.EventWrite ? "all GET/PATCH/GET request durations; not end-to-end transaction duration" : "single public read or search request duration",
                    Authentication = journey == K6Journey.EventWrite ? "fresh real Local login, configured five-minute JWT lifetime" : "anonymous",
                    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    LogicalProcessors = Environment.ProcessorCount, TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                    BuildInformationalVersion = typeof(K6LoadRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    measurement.Summary, measurement.WallMilliseconds, measurement.GeneratorSamples,
                    measurement.GeneratorPeakCpuRatio, measurement.GeneratorPeakRssBytes,
                    GeneratorCpuBudgetRatio = 0.8, GeneratorMemoryBudgetBytes = MemoryBudgetBytes,
                    HostQuiescence = "unverified; candidates cannot establish capacity", RecordedAtUtc = DateTimeOffset.UtcNow
                }, EvidenceJsonOptions), cancellationToken);
            return measurement;
        }
    }

    private static async Task SamplePressureAsync(GlobalExploreDbContextFactory factory, string expectedDatabase, long programmeStartedTimestamp,
        List<K6PressureObservation> observations, CancellationToken cancellationToken)
    {
        try
        {
            while (observations.Count < 900)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double startedOffset = Stopwatch.GetElapsedTime(programmeStartedTimestamp).TotalMilliseconds;
                DateTimeOffset observedAt = DateTimeOffset.UtcNow;
                var databaseTask = ReadDatabasePressureAsync(factory, expectedDatabase, cancellationToken);
                var queueTask = ReadQueuePressureAsync(factory, cancellationToken);
                await Task.WhenAll(databaseTask, queueTask);
                var database = await databaseTask;
                var queue = await queueTask;
                observations.Add(new K6PressureObservation(startedOffset, Stopwatch.GetElapsedTime(programmeStartedTimestamp).TotalMilliseconds, observedAt,
                    database.Status, database.Value, queue.Status, queue.Value));
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private static async Task LoginAdministratorAsync(HttpClient api, OwnedAppHostFixture fixture, CancellationToken cancellationToken)
    {
        api.DefaultRequestHeaders.Authorization = null;
        using var login = await ReadNativeJsonAsync(api, HttpMethod.Post, "/api/auth/local/login", new
        {
            identifier = fixture.BootstrapIdentifier, password = fixture.BootstrapReplacementPassword
        }, cancellationToken);
        if (!login.RootElement.GetProperty("success").GetBoolean())
            throw new InvalidOperationException("load_ordinary_login_required");
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            login.RootElement.GetProperty("token").GetString() ?? throw new InvalidOperationException("load_access_token_missing"));
    }

    private static async Task VerifyPublicDatasetAsync(HttpClient publicApi, K6NativeDataset dataset, string searchTerm,
        CancellationToken cancellationToken)
    {
        using var read = await ReadNativeJsonAsync(publicApi, HttpMethod.Get, $"/api/Event/{dataset.ReadId:D}", null, cancellationToken);
        using var search = await ReadNativeJsonAsync(publicApi, HttpMethod.Get,
            "/api/Event?pageSize=20&searchTerm=" + Uri.EscapeDataString(searchTerm), null, cancellationToken);
        var items = search.RootElement.GetProperty("_embedded").GetProperty("items");
        if (read.RootElement.GetProperty("id").GetGuid() != dataset.ReadId ||
            read.RootElement.GetProperty("title").GetString() != dataset.ReadTitle || items.GetArrayLength() != 1 ||
            items[0].GetProperty("event").GetProperty("id").GetGuid() != dataset.ReadId ||
            items[0].GetProperty("event").GetProperty("title").GetString() != dataset.ReadTitle)
            throw new InvalidOperationException("load_public_dataset_changed");
    }

    private static async Task VerifySettledDatasetAsync(HttpClient observer, HttpClient publicApi, K6NativeDataset dataset,
        string expectedWriteTitle, string searchTerm, CancellationToken cancellationToken)
    {
        using var written = await ReadNativeJsonAsync(observer, HttpMethod.Get,
            $"/api/Event/{dataset.WriteId:D}/management-detail", null, cancellationToken);
        using var control = await ReadNativeJsonAsync(observer, HttpMethod.Get,
            $"/api/Event/{dataset.ControlId:D}/management-detail", null, cancellationToken);
        if (written.RootElement.GetProperty("title").GetString() != expectedWriteTitle ||
            written.RootElement.GetProperty("tenantId").GetGuid() != dataset.TenantId ||
            control.RootElement.GetProperty("title").GetString() != dataset.ControlTitle ||
            control.RootElement.GetProperty("tenantId").GetGuid() != dataset.TenantId ||
            control.RootElement.GetProperty("concurrencyStamp").GetGuid() != dataset.ControlStamp)
            throw new InvalidOperationException("load_independent_settlement_invalid");
        await VerifyPublicDatasetAsync(publicApi, dataset, searchTerm, cancellationToken);
    }

    internal static async Task RunAsync(OwnedAppHostFixture fixture, string topology, CancellationToken cancellationToken)
    {
        if (topology is not ("Split" or "Standalone"))
            throw new InvalidOperationException("load_topology_not_admitted");
        string executable = Environment.GetEnvironmentVariable("ISLAMU_K6_PATH") ?? "";
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("load_k6_absolute_executable_required");
        var versionStartInfo = StartInfo(executable);
        versionStartInfo.ArgumentList.Add("version");
        string version = await ReadVersionAsync(versionStartInfo, cancellationToken);
        string script = Path.Combine(AppContext.BaseDirectory, "Workloads", "owned-runtime.js");
        if (!File.Exists(script))
            throw new InvalidOperationException("load_reviewed_workload_missing");
        string workloadDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script, cancellationToken)));
        string executableDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, cancellationToken)));
        using var nativeTransport = CreateNativeTransport();
        using var client = CreateNativeClient(fixture, topology, nativeTransport);
        Uri origin = client.BaseAddress ?? throw new InvalidOperationException("load_owned_endpoint_missing");
        // Literal loopback addresses avoid DNS rebinding and make the admitted origin immutable.
        var target = new UriBuilder(origin) { Host = IPAddress.Loopback.ToString(), Path = "/api/language" }.Uri;
        var ownedOrigin = new UriBuilder(origin) { Host = IPAddress.Loopback.ToString(), Path = "/" }.Uri;
        ValidateTarget(ownedOrigin, target);
        using var transport = CreateNativeTransport();
        using var warmupClient = new HttpClient(transport)
            { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        var warmup = new List<double>();
        string? expectedDigest = null;
        for (int index = 0; index < 3; index++)
        {
            var timer = Stopwatch.StartNew();
            using var response = await warmupClient.GetAsync(target, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new InvalidOperationException("load_lookup_preflight_failed");
            ValidateLanguageResponse(body);
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            if (expectedDigest is not null && expectedDigest != digest)
                throw new InvalidOperationException("load_lookup_preflight_unstable");
            expectedDigest = digest;
            warmup.Add(timer.Elapsed.TotalMilliseconds);
        }

        string ownedDirectory = Path.Combine(fixture.Root, "k6");
        Directory.CreateDirectory(ownedDirectory);
        string configurationPath = Path.Combine(ownedDirectory, "config.json");
        await File.WriteAllTextAsync(configurationPath, "{}", cancellationToken);
        foreach (var profile in new[] { K6LoadProfile.ClosedLookup, K6LoadProfile.OpenLookup })
        {
            var budget = BudgetFor(profile);
            string mode = budget.Mode;
            string summaryPath = Path.Combine(ownedDirectory, $"{mode}.json");
            var startInfo = StartInfo(executable);
            foreach (string argument in new[] { "run", "--config", configurationPath, "--quiet", "--no-color", "--no-usage-report", "--include-system-env-vars=true", script })
                startInfo.ArgumentList.Add(argument);
            startInfo.WorkingDirectory = ownedDirectory;
            startInfo.Environment["LOAD_ORIGIN"] = ownedOrigin.AbsoluteUri.TrimEnd('/');
            startInfo.Environment["LOAD_TARGET"] = target.AbsoluteUri;
            startInfo.Environment["LOAD_DIGEST"] = expectedDigest!;
            startInfo.Environment["LOAD_PROFILE"] = budget.Name;
            startInfo.Environment["LOAD_JOURNEY"] = JourneyName(K6Journey.LanguageLookup);
            startInfo.Environment["LOAD_SUMMARY"] = summaryPath;
            await ValidateExecutableAsync(executable, cancellationToken);
            var measurement = await RunWithRejectedEvidenceAsync(startInfo, summaryPath,
                Path.Combine(EvidenceDirectory(fixture), $"rejected-{topology}-{mode}.json"), profile, K6Journey.LanguageLookup, budget.ProcessDeadline, cancellationToken);
            ValidateGenerator(measurement.GeneratorSamples, measurement.GeneratorPeakCpuRatio, measurement.GeneratorPeakRssBytes);
            string evidenceDirectory = EvidenceDirectory(fixture);
            var evidence = new
            {
                Schema = 2, Candidate = true, CapacityEvidenceEligible = false, WorkloadId, WorkloadDigest = workloadDigest,
                ToolVersion = version, ExecutableDigest = executableDigest, Topology = topology,
                Provider = "PostgreSql", Profile = budget.Name, Mode = mode, budget.DurationSeconds,
                VirtualUsers = mode == "closed" ? budget.MaximumVirtualUsers : (int?)null,
                OfferedRate = mode == "open" ? budget.OfferedRate : (int?)null,
                budget.MaximumVirtualUsers,
                WarmupMilliseconds = warmup, measurement.Summary,
                measurement.WallMilliseconds, measurement.GeneratorSamples,
                measurement.GeneratorPeakCpuRatio, measurement.GeneratorPeakRssBytes,
                GeneratorCpuBudgetRatio = 0.8, GeneratorMemoryBudgetBytes = MemoryBudgetBytes,
                LogicalProcessors = Environment.ProcessorCount, TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                BuildInformationalVersion = typeof(K6LoadRunner).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                RecordedAtUtc = DateTimeOffset.UtcNow,
                CacheProfile = "three stable warmup requests followed by cached public language lookup reads",
                Scope = "public cached lookup reads; no authenticated writes, settlement, SLO or capacity claim"
            };
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, $"load-{topology}-{mode}.json"),
                JsonSerializer.Serialize(evidence, EvidenceJsonOptions), cancellationToken);
        }
    }

    internal static async Task<K6ProcessMeasurement> RunOwnedProcessAsync(ProcessStartInfo startInfo,
        string summaryPath, K6LoadProfile profile, TimeSpan timeout, CancellationToken cancellationToken,
        K6Journey journey = K6Journey.LanguageLookup)
    {
        ValidateJourneyBudget(journey, profile);
        var budget = BudgetFor(profile);
        if (timeout <= TimeSpan.Zero || timeout > budget.ProcessDeadline)
            throw new InvalidOperationException("load_deadline_not_admitted");
        using var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var wall = Stopwatch.StartNew();
        process.Start();
        long startedTimestamp = Stopwatch.GetTimestamp();
        long exitedTimestamp;
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        int samples = 0;
        long peakRss = 0;
        double peakCpuRatio = 0;
        double previousSeconds = 0;
        TimeSpan previousCpu = TimeSpan.Zero;
        try
        {
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
                process.Refresh();
                if (process.HasExited)
                    break;
                double seconds = wall.Elapsed.TotalSeconds;
                TimeSpan cpu = process.TotalProcessorTime;
                peakCpuRatio = Math.Max(peakCpuRatio,
                    (cpu - previousCpu).TotalSeconds / (seconds - previousSeconds) / Environment.ProcessorCount);
                peakRss = Math.Max(peakRss, process.WorkingSet64);
                if (peakRss > MemoryBudgetBytes)
                    throw new InvalidOperationException("load_generator_pressure_invalidates_measurement");
                samples++;
                previousCpu = cpu;
                previousSeconds = seconds;
            }
            await process.WaitForExitAsync(deadline.Token);
            exitedTimestamp = Stopwatch.GetTimestamp();
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        wall.Stop();
        if (process.ExitCode != 0 || !File.Exists(summaryPath) || new FileInfo(summaryPath).Length > 4096)
            throw new InvalidOperationException("load_process_or_summary_failed");
        var summary = ParseSummary(await File.ReadAllTextAsync(summaryPath, cancellationToken), profile, journey);
        return new K6ProcessMeasurement(summary, wall.Elapsed.TotalMilliseconds, samples, peakCpuRatio, peakRss,
            startedTimestamp, exitedTimestamp);
    }

    internal static Uri ValidateTarget(Uri ownedOrigin, Uri target)
    {
        if (ownedOrigin.Scheme != "http" || ownedOrigin.Host != "127.0.0.1" || ownedOrigin.IsDefaultPort ||
            target.Scheme != ownedOrigin.Scheme || target.Host != ownedOrigin.Host || target.Port != ownedOrigin.Port ||
            target.AbsolutePath != "/api/language" || target.Query.Length != 0 || target.Fragment.Length != 0 || target.UserInfo.Length != 0)
            throw new InvalidOperationException("load_target_not_admitted");
        return target;
    }

    internal static void ValidateLanguageResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                throw new InvalidOperationException("load_lookup_contract_invalid");
            var ids = new HashSet<int>();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                int id = row.GetProperty("id").GetInt32();
                if (id <= 0 || !ids.Add(id) || string.IsNullOrWhiteSpace(row.GetProperty("masterCode").GetString()) ||
                    string.IsNullOrWhiteSpace(row.GetProperty("fullName").GetString()))
                    throw new InvalidOperationException("load_lookup_contract_invalid");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("load_lookup_contract_invalid");
        }
    }

    internal static async Task<K6ProcessMeasurement> RunWithRejectedEvidenceAsync(ProcessStartInfo startInfo,
        string summaryPath, string evidencePath, K6LoadProfile profile, K6Journey journey, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await RunOwnedProcessAsync(startInfo, summaryPath, profile, timeout, cancellationToken, journey);
        }
        catch (Exception failure)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await RetainRejectedSummaryAsync(summaryPath, evidencePath, profile, journey, deadline.Token);
            }
            catch (Exception evidenceFailure)
            {
                throw new AggregateException("load_rejected_evidence_failed", failure, evidenceFailure);
            }
            throw;
        }
    }

    internal static K6Summary ParseSummary(string json, K6LoadProfile expectedProfile,
        K6Journey expectedJourney = K6Journey.LanguageLookup)
    {
        ValidateJourneyBudget(expectedJourney, expectedProfile);
        var budget = BudgetFor(expectedProfile);
        try
        {
            var summary = ReadSummaryProjection(json, expectedProfile, expectedJourney);
            long attempts = summary.Attempts;
            long requests = attempts * (expectedJourney == K6Journey.EventWrite ? 3 : 1);
            if (attempts <= 0 || attempts > 1000 ||
                (budget.OfferedRate > 0 && attempts != budget.ExpectedAttempts) ||
                summary.Correct != attempts || summary.Iterations != attempts || summary.Responses != requests || summary.HttpRequests != requests ||
                summary.CorrectnessRate != 1 || summary.HttpFailureRate != 0 || summary.DroppedIterations != 0 ||
                summary.P50Milliseconds > summary.P95Milliseconds || summary.P95Milliseconds > summary.P99Milliseconds)
                throw new InvalidOperationException("load_measurement_invalid");
            return summary;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidOperationException("load_summary_invalid");
        }
    }

    private static K6Summary ReadSummaryProjection(string json, K6LoadProfile profile, K6Journey journey)
    {
        ValidateJourneyBudget(journey, profile);
        var budget = BudgetFor(profile);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("load_summary_invalid");
        string[] names = root.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != SummaryFields.Length || !names.Order().SequenceEqual(SummaryFields.Order()) ||
            root.GetProperty("schema").GetInt32() != 2 || root.GetProperty("mode").GetString() != budget.Mode ||
            root.GetProperty("profile").GetString() != budget.Name || root.GetProperty("journey").GetString() != JourneyName(journey))
            throw new InvalidOperationException("load_summary_invalid");
        long[] counts = new[] { "attempts", "responses", "correct", "iterations", "httpRequests", "dropped" }
            .Select(name => root.GetProperty(name).GetInt64()).ToArray();
        double[] values = new[] { "correctnessRate", "httpFailureRate", "p50Milliseconds", "p95Milliseconds", "p99Milliseconds" }
            .Select(name => root.GetProperty(name).GetDouble()).ToArray();
        if (counts.Any(value => value < 0 || value > 10000) || values.Any(value => !double.IsFinite(value) || value < 0) ||
            values[0] > 1 || values[1] > 1)
            throw new InvalidOperationException("load_summary_invalid");
        return new K6Summary(JourneyName(journey), budget.Name, budget.Mode, counts[0], counts[1], counts[2], counts[3], counts[4],
            values[0], values[1], counts[5], values[2], values[3], values[4]);
    }

    internal static async Task RetainRejectedSummaryAsync(string summaryPath, string evidencePath, K6LoadProfile profile,
        K6Journey journey, CancellationToken cancellationToken)
    {
        string status = "Missing";
        K6Summary? summary = null;
        try
        {
            using var stream = File.OpenRead(summaryPath);
            byte[] buffer = new byte[4097];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0) break;
                length += read;
            }
            status = length > 4096 ? "Oversized" : "Invalid";
            if (length <= 4096)
            {
                summary = ReadSummaryProjection(Encoding.UTF8.GetString(buffer, 0, length), profile, journey);
                status = "Available";
            }
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            status = "Missing";
        }
        catch (IOException)
        {
            status = "Unreadable";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            status = "Invalid";
        }
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new
        {
            Schema = 1, Admission = "Rejected", CapacityEvidenceEligible = false, SummaryStatus = status, Summary = summary
        }, EvidenceJsonOptions), cancellationToken);
    }

    internal static void ValidateGenerator(int samples, double peakCpuRatio, long peakRss)
    {
        if (samples == 0 || !double.IsFinite(peakCpuRatio) || peakCpuRatio < 0 || peakCpuRatio > 0.8 || peakRss <= 0 || peakRss > MemoryBudgetBytes)
            throw new InvalidOperationException("load_generator_pressure_invalidates_measurement");
    }

    internal static ProcessStartInfo StartInfo(string executable)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        info.Environment.Clear();
        info.Environment["K6_NO_USAGE_REPORT"] = "true";
        info.Environment["NO_PROXY"] = "*";
        info.Environment[OperatingSystem.IsWindows() ? "TEMP" : "TMPDIR"] = Path.GetTempPath();
        return info;
    }

    internal static async Task<string> ReadVersionAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        await ValidateExecutableAsync(startInfo.FileName, cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            string version = await output;
            if (process.ExitCode != 0 || !version.StartsWith("k6 v2.3.0 ", StringComparison.Ordinal))
                throw new InvalidOperationException("load_k6_version_not_admitted");
            return "2.3.0";
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await error.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }

    private static async Task ValidateExecutableAsync(string executable, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
            System.Runtime.InteropServices.Architecture.X64 || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("load_k6_digest_not_admitted");
        using var stream = File.OpenRead(executable);
        if (Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)) != AdmittedExecutableDigest)
            throw new InvalidOperationException("load_k6_digest_not_admitted");
    }
}

internal sealed record K6Summary(string Journey, string Profile, string Mode, long Attempts, long Responses, long Correct,
    long Iterations, long HttpRequests, double CorrectnessRate, double HttpFailureRate,
    long DroppedIterations, double P50Milliseconds, double P95Milliseconds, double P99Milliseconds);

public enum K6LoadProfile { ClosedLookup, OpenLookup, LowRate, Steady, Spike, Recovery, Soak, ConstrainedWrite }

internal enum K6Journey { LanguageLookup, EventRead, EventSearch, EventWrite }

internal sealed record K6LoadBudget(string Name, int DurationSeconds, int OfferedRate, int MaximumVirtualUsers)
{
    internal string Mode => OfferedRate == 0 ? "closed" : "open";
    internal int ExpectedAttempts => DurationSeconds * OfferedRate;
    internal TimeSpan ProcessDeadline => TimeSpan.FromSeconds(DurationSeconds + 10);
}

internal sealed record K6MetricDefinition(string Name, string Kind, string? Unit);

internal enum K6MetricInventoryStatus { MetadataOnly, EndpointUnavailable, UnsupportedMediaType, InvalidMetadata, TimedOut }

internal enum K6ObservationStatus { Available, Unavailable, TimedOut, RuntimeSeriesUnpinned }

internal sealed record K6DatabasePressure(int ServerVersionMajor, int ServerVersionMinor, int Backends, long Commits, long Rollbacks, long BlocksRead,
    long BufferHits, long TempFiles, long TempBytes, long Deadlocks, DateTime? StatisticsResetAtUtc);

internal sealed record K6PressureObservation(double StartedOffsetMilliseconds, double CompletedOffsetMilliseconds, DateTimeOffset ObservedAtUtc,
    K6ObservationStatus DatabaseStatus, K6DatabasePressure? Database,
    K6ObservationStatus QueueStatus, K6QueuePressure? Queue);

internal sealed record K6QueuePressure(int IntegrationDue, int IntegrationStale, int IntegrationAmbiguous,
    int IncomingDue, int IncomingStale, int BulkReplayQueued, int BulkReplayExecuting, int ProviderPublicationDue,
    int ProviderPublicationStale, int ProviderPublicationUnknown, int PdsDue, int PdsStale, int PdsDeadLettered,
    int EmailDue, int EmailRetryScheduled, int EmailStale, int EmailDeadLettered, int EmailUnknown, int EmailParked);

internal sealed record K6NativeDataset(Guid TenantId, string TenantSlug, Guid ReadId, string ReadTitle,
    Guid WriteId, Guid ControlId, string ControlTitle, Guid ControlStamp, DateTimeOffset SeedStartedAtUtc,
    DateTimeOffset SeedCompletedAtUtc);

internal sealed record K6ProcessMeasurement(K6Summary Summary, double WallMilliseconds,
    int GeneratorSamples, double GeneratorPeakCpuRatio, long GeneratorPeakRssBytes,
    long StartedTimestamp, long ExitedTimestamp);
