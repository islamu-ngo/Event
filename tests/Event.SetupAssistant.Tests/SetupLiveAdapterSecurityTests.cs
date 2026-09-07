namespace Event.SetupAssistant.Tests;

using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using ISLAMU.Event.SetupAssistant.SetupLive;
using ISLAMU.Wire.Contracts.SetupLive;

public sealed class SetupLiveAdapterSecurityTests
{
    private static readonly Uri Target = new("https://setup-target.example/");
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid EnrollmentId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string AuthenticationToken = $"auth-{Guid.CreateVersion7():N}";
    private static readonly string[] AllScopes =
        ["target.read", "secret_binding.readiness", "secret_binding.write"];

    [Test]
    public async Task EnrollmentBindsExactTargetAndTenantWithoutExposingCapability()
    {
        string capability = Capability(3);
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            capability,
            Link(SetupLiveHalRelations.Revoke, "DELETE"),
            Link(SetupLiveHalRelations.RotateCapability, "POST"),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));

        SetupLiveEnrollmentSnapshot snapshot = await adapter.EnrollAsync(
            SetupClientChallenge.FromBytes(Enumerable.Repeat((byte)7, 32).ToArray()),
            [
                SetupEnrollmentScope.TargetRead,
                SetupEnrollmentScope.SecretBindingReadiness,
                SetupEnrollmentScope.SecretBindingWrite
            ],
            CancellationToken.None);

        CapturedRequest request = handler.Requests.Single();
        using JsonDocument body = JsonDocument.Parse(request.Body);
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.Uri).IsEqualTo(
            new Uri(Target, $"api/tenants/{TenantId:D}/setup/enrollments"));
        await Assert.That(request.ContentType).IsEqualTo(SetupLiveContractMetadata.CreateRequestMediaType);
        await Assert.That(request.Accept).IsEqualTo(SetupLiveContractMetadata.SuccessMediaType);
        await AssertUuid7(request.Header(SetupLiveContractMetadata.IdempotencyHeader));
        await Assert.That(body.RootElement.GetProperty("requestedScopes")[0].GetString())
            .IsEqualTo("target.read");
        await Assert.That(snapshot.TargetBaseAddress).IsEqualTo(Target);
        await Assert.That(snapshot.TenantId).IsEqualTo(TenantId);
        await Assert.That(snapshot.EnrollmentId).IsEqualTo(EnrollmentId);
        await Assert.That(adapter.HasAuthority).IsTrue();
        await Assert.That(snapshot.ToString()).DoesNotContain(capability);
        await Assert.That(adapter.ToString()).DoesNotContain(capability);
        await AssertCanaryPlacement(request, capability);

        adapter.ClearAuthority();
        await Assert.That(adapter.HasAuthority).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => adapter.RefreshAsync(CancellationToken.None));
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DisposalClearsAuthorityBeforeAnyFurtherTransport()
    {
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(31),
            Link(SetupLiveHalRelations.Self)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);
        await Assert.That(adapter.HasAuthority).IsTrue();

        adapter.Dispose();

        await Assert.That(adapter.HasAuthority).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => adapter.RefreshAsync(CancellationToken.None));
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task RepeatedEnrollmentsUseDistinctRfcUuid7Keys()
    {
        using var firstHandler = new RecordingHandler();
        firstHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(32)));
        using var firstClient = new HttpClient(firstHandler, disposeHandler: false) { BaseAddress = Target };
        using var firstAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            firstClient,
            TokenProvider(),
            new ManualTimeProvider(Now));
        using var secondHandler = new RecordingHandler();
        secondHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(33)));
        using var secondClient = new HttpClient(secondHandler, disposeHandler: false) { BaseAddress = Target };
        using var secondAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            secondClient,
            TokenProvider(),
            new ManualTimeProvider(Now));

        await EnrollAsync(firstAdapter);
        await EnrollAsync(secondAdapter);

        string firstKey = firstHandler.Requests.Single().Header(
            SetupLiveContractMetadata.IdempotencyHeader);
        string secondKey = secondHandler.Requests.Single().Header(
            SetupLiveContractMetadata.IdempotencyHeader);
        await AssertUuid7(firstKey);
        await AssertUuid7(secondKey);
        await Assert.That(firstKey).IsNotEqualTo(secondKey);
    }

    [Test]
    public async Task ArbitraryRemotePlaintextHttpIsRejectedBeforeClientUse()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://remote-target.example/")
        };

        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(
            new SetupLiveAdapter(client.BaseAddress, TenantId, TokenProvider())));
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task ProductionTransportDisablesAutomaticRedirects()
    {
        using SocketsHttpHandler transport = SetupLiveAdapter.CreateTransport();

        await Assert.That(transport.AllowAutoRedirect).IsFalse();
    }

    [Test]
    public async Task FreshAuthenticationIsAttachedOnlyAsBearerPerRequest()
    {
        string[] tokenValues = Enumerable.Range(0, 7)
            .Select(index => $"auth-{index}-{Guid.CreateVersion7():N}")
            .ToArray();
        var tokens = new Queue<SetupLiveAccessToken>(
            tokenValues.Select(value =>
                SetupLiveAccessToken.Create(value, Now.AddMinutes(5))));
        SetupLiveAccessTokenProvider provider = _ =>
            ValueTask.FromResult<SetupLiveAccessToken?>(tokens.Dequeue());
        using var telemetry = new SensitiveTelemetryCapture();
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(34),
            Link(SetupLiveHalRelations.RotateCapability, "POST")));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(10),
            Capability(35),
            2,
            "issued",
            Link(SetupLiveHalRelations.Self),
            Link(SetupLiveHalRelations.SecretBindingReadiness),
            Link(SetupLiveHalRelations.Revoke, "DELETE")));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(10),
            capability: null,
            generation: 2,
            issuance: "already_issued",
            Link(SetupLiveHalRelations.SecretBindingReadiness),
            Link(SetupLiveHalRelations.Revoke, "DELETE")));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        Guid operationId = Guid.CreateVersion7();
        handler.Enqueue(() => OperationResponse(
            operationId,
            enrollmentGeneration: 2));
        handler.Enqueue(() => OperationResponse(
            operationId,
            HttpStatusCode.OK,
            enrollmentGeneration: 2));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(10),
            capability: null,
            state: "revoked",
            generation: 2,
            issuance: "already_issued"));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(
            Target,
            TenantId,
            client,
            provider,
            new ManualTimeProvider(Now));

        await EnrollAsync(adapter);
        _ = await adapter.RotateCapabilityAsync(CancellationToken.None);
        _ = await adapter.RefreshAsync(CancellationToken.None);
        _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var secret = new MemoryStream([38], writable: false);
        _ = await adapter.WriteSecretBindingAsync(
            "setup.signing",
            secret,
            CancellationToken.None);
        _ = await adapter.GetSecretBindingOperationAsync(
            operationId,
            CancellationToken.None);
        _ = await adapter.RevokeAsync(CancellationToken.None);

        await Assert.That(handler.Requests).Count().IsEqualTo(tokenValues.Length);
        for (int index = 0; index < tokenValues.Length; index++)
        {
            await Assert.That(handler.Requests[index].Header("Authorization"))
                .IsEqualTo($"Bearer {tokenValues[index]}");
            await AssertCanaryPlacement(
                handler.Requests[index],
                tokenValues[index],
                "Authorization");
            await Assert.That(telemetry.Text).DoesNotContain(tokenValues[index]);
        }

        var writeTokens = new Queue<SetupLiveAccessToken?>(
        [
            SetupLiveAccessToken.Create(AuthenticationToken, Now.AddMinutes(5)),
            SetupLiveAccessToken.Create(AuthenticationToken, Now.AddMinutes(5)),
            null
        ]);
        SetupLiveAccessTokenProvider writeProvider = _ =>
            ValueTask.FromResult(writeTokens.Dequeue());
        using var writeHandler = new RecordingHandler();
        writeHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(39),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        writeHandler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        using var writeClient = new HttpClient(writeHandler, disposeHandler: false) { BaseAddress = Target };
        using var writeAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            writeClient,
            writeProvider,
            new ManualTimeProvider(Now));
        await EnrollAsync(writeAdapter);
        _ = await writeAdapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var unreadSecret = new MemoryStream([40], writable: false);

        await Assert.ThrowsAsync<SetupLiveAuthenticationUnavailableException>(() =>
            writeAdapter.WriteSecretBindingAsync(
                "setup.signing",
                unreadSecret,
                CancellationToken.None));

        await Assert.That(unreadSecret.Position).IsEqualTo(0);
        await Assert.That(writeHandler.Requests).Count().IsEqualTo(2);
        await Assert.That(writeAdapter.HasAuthority).IsFalse();
    }

    [Test]
    public async Task MissingOrExpiredAuthenticationPreventsTransport()
    {
        SetupLiveAccessTokenProvider[] unavailableProviders =
        [
            _ => ValueTask.FromResult<SetupLiveAccessToken?>(null),
            TokenProvider(expiresAt: Now)
        ];

        foreach (SetupLiveAccessTokenProvider provider in unavailableProviders)
        {
            using var handler = new RecordingHandler();
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                provider,
                new ManualTimeProvider(Now));

            await Assert.ThrowsAsync<SetupLiveAuthenticationUnavailableException>(
                () => EnrollAsync(adapter));
            await Assert.That(adapter.HasAuthority).IsFalse();
            await Assert.That(handler.Requests).IsEmpty();
        }
    }

    [Test]
    public async Task ExpiredAuthenticationClearsEnrollmentAuthorityBeforeTransport()
    {
        var tokens = new Queue<SetupLiveAccessToken>(
        [
            SetupLiveAccessToken.Create(AuthenticationToken, Now.AddMinutes(5)),
            SetupLiveAccessToken.Create(AuthenticationToken, Now)
        ]);
        SetupLiveAccessTokenProvider provider = _ =>
            ValueTask.FromResult<SetupLiveAccessToken?>(tokens.Dequeue());
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(35),
            Link(SetupLiveHalRelations.Self)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(
            Target,
            TenantId,
            client,
            provider,
            new ManualTimeProvider(Now));
        await EnrollAsync(adapter);

        await Assert.ThrowsAsync<SetupLiveAuthenticationUnavailableException>(
            () => adapter.RefreshAsync(CancellationToken.None));

        await Assert.That(adapter.HasAuthority).IsFalse();
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ProviderFailuresAreBoundedAndLocalDenialsNeverResolveAuthentication()
    {
        string providerCanary = $"oauth-provider-{Guid.CreateVersion7():N}";
        int providerCalls = 0;
        SetupLiveAccessTokenProvider failingProvider = _ =>
        {
            providerCalls++;
            if (providerCalls == 1)
            {
                return ValueTask.FromResult<SetupLiveAccessToken?>(
                    SetupLiveAccessToken.Create(
                        AuthenticationToken,
                        Now.AddMinutes(5)));
            }
            throw new InvalidOperationException(providerCanary);
        };
        using var failingHandler = new RecordingHandler();
        failingHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(41),
            Link(SetupLiveHalRelations.Self)));
        using var failingClient = new HttpClient(failingHandler, disposeHandler: false) { BaseAddress = Target };
        using var failingAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            failingClient,
            failingProvider,
            new ManualTimeProvider(Now));
        await EnrollAsync(failingAdapter);

        SetupLiveAuthenticationUnavailableException exception =
            await Assert.ThrowsAsync<SetupLiveAuthenticationUnavailableException>(
                () => failingAdapter.RefreshAsync(CancellationToken.None))
            ?? throw new InvalidOperationException("Expected bounded authentication failure.");

        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(exception.ToString()).DoesNotContain(providerCanary);
        await Assert.That(failingAdapter.HasAuthority).IsFalse();
        await Assert.That(failingHandler.Requests).Count().IsEqualTo(1);

        foreach (string localDenial in new[] { "affordance", "expiry", "clear", "dispose" })
        {
            int localProviderCalls = 0;
            SetupLiveAccessTokenProvider provider = _ =>
            {
                localProviderCalls++;
                return ValueTask.FromResult<SetupLiveAccessToken?>(
                    SetupLiveAccessToken.Create(
                        AuthenticationToken,
                        Now.AddMinutes(5)));
            };
            var clock = new ManualTimeProvider(Now);
            using var handler = new RecordingHandler();
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddSeconds(30),
                Capability(42)));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                provider,
                clock);
            await EnrollAsync(adapter);

            switch (localDenial)
            {
                case "affordance":
                    await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(
                        () => adapter.RevokeAsync(CancellationToken.None));
                    break;
                case "expiry":
                    clock.Advance(TimeSpan.FromMinutes(1));
                    await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                        () => adapter.RefreshAsync(CancellationToken.None));
                    break;
                case "clear":
                    adapter.ClearAuthority();
                    await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                        () => adapter.RefreshAsync(CancellationToken.None));
                    break;
                default:
                    adapter.Dispose();
                    await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                        () => adapter.RefreshAsync(CancellationToken.None));
                    break;
            }

            await Assert.That(localProviderCalls).IsEqualTo(1);
            await Assert.That(handler.Requests).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task CrossOriginRedirectCannotForwardCapabilityOrSecretBody()
    {
        string capability = Capability(24);
        string secretCanary = $"redirect-secret-{Guid.CreateVersion7():N}";
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            capability,
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        handler.Enqueue(() => RedirectResponse(
            new Uri("https://redirect-attacker.example/collect")));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);
        _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var body = new MemoryStream(
            Encoding.UTF8.GetBytes(secretCanary),
            writable: false);

        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(() =>
            adapter.WriteSecretBindingAsync(
                "setup.signing",
                body,
                CancellationToken.None));

        await Assert.That(handler.Requests).Count().IsEqualTo(3);
        await Assert.That(handler.Requests[2].Uri.Host).IsEqualTo(Target.Host);
        await Assert.That(handler.Requests.Any(request =>
            request.Uri.Host == "redirect-attacker.example")).IsFalse();
        await Assert.That(adapter.HasAuthority).IsFalse();
    }

    [Test]
    public async Task EnrollmentRejectsServerScopeEscalationAndKeepsAuthorityCleared()
    {
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(4),
            Link(SetupLiveHalRelations.Self)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));

        await Assert.ThrowsAsync<SetupLiveContractViolationException>(() =>
            adapter.EnrollAsync(
                SetupClientChallenge.FromBytes(Enumerable.Repeat((byte)5, 32).ToArray()),
                [SetupEnrollmentScope.TargetRead, SetupEnrollmentScope.SecretBindingReadiness],
                CancellationToken.None));

        await Assert.That(adapter.HasAuthority).IsFalse();
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => adapter.RefreshAsync(CancellationToken.None));
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task InvalidEnrollmentCapabilityHeadersNeverCreateAuthority()
    {
        IReadOnlyList<string>[] invalidHeaders =
        [
            [],
            ["malformed"],
            [Capability(25), Capability(26)]
        ];

        foreach (IReadOnlyList<string> headers in invalidHeaders)
        {
            using var handler = new RecordingHandler();
            handler.Enqueue(() => EnrollmentResponseWithCapabilities(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                headers,
                Link(SetupLiveHalRelations.Self)));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));

            await Assert.ThrowsAsync<SetupLiveContractViolationException>(() =>
                EnrollAsync(adapter));
            await Assert.That(adapter.HasAuthority).IsFalse();
            await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                () => adapter.RefreshAsync(CancellationToken.None));
            await Assert.That(handler.Requests).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task DuplicateEnrollmentWithoutCapabilityRemainsAuthorityFree()
    {
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(5),
            capability: null,
            Link(SetupLiveHalRelations.Self)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));

        SetupLiveEnrollmentSnapshot snapshot = await EnrollAsync(adapter);

        await Assert.That(snapshot.EnrollmentId).IsEqualTo(EnrollmentId);
        await Assert.That(adapter.HasAuthority).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => adapter.RefreshAsync(CancellationToken.None));
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments(SetupLiveHalRelations.Self)]
    [Arguments(SetupLiveHalRelations.Revoke)]
    [Arguments(SetupLiveHalRelations.RotateCapability)]
    [Arguments(SetupLiveHalRelations.SecretBindingReadiness)]
    public async Task WrongMethodEnrollmentAffordanceNeverDispatches(string relation)
    {
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(6),
            Link(relation, "PATCH")));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(async () =>
        {
            switch (relation)
            {
                case SetupLiveHalRelations.Self:
                    _ = await adapter.RefreshAsync(CancellationToken.None);
                    break;
                case SetupLiveHalRelations.Revoke:
                    _ = await adapter.RevokeAsync(CancellationToken.None);
                    break;
                case SetupLiveHalRelations.RotateCapability:
                    _ = await adapter.RotateCapabilityAsync(CancellationToken.None);
                    break;
                default:
                    _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
                    break;
            }
        });
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments(SetupLiveHalRelations.Self)]
    [Arguments(SetupLiveHalRelations.Revoke)]
    [Arguments(SetupLiveHalRelations.RotateCapability)]
    [Arguments(SetupLiveHalRelations.SecretBindingReadiness)]
    public async Task MissingEnrollmentAffordanceNeverDispatches(string relation)
    {
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(27)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(async () =>
        {
            switch (relation)
            {
                case SetupLiveHalRelations.Self:
                    _ = await adapter.RefreshAsync(CancellationToken.None);
                    break;
                case SetupLiveHalRelations.Revoke:
                    _ = await adapter.RevokeAsync(CancellationToken.None);
                    break;
                case SetupLiveHalRelations.RotateCapability:
                    _ = await adapter.RotateCapabilityAsync(CancellationToken.None);
                    break;
                default:
                    _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
                    break;
            }
        });
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task HalAloneGatesRevocationAndSuccessfulRevocationClearsAuthority()
    {
        using var deniedHandler = new RecordingHandler();
        deniedHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(5)));
        using var deniedClient = new HttpClient(deniedHandler, disposeHandler: false) { BaseAddress = Target };
        using var denied = new SetupLiveAdapter(Target, TenantId, deniedClient, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(denied);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(
            () => denied.RevokeAsync(CancellationToken.None));
        await Assert.That(deniedHandler.Requests).Count().IsEqualTo(1);

        string capability = Capability(11);
        using var allowedHandler = new RecordingHandler();
        allowedHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            capability,
            Link(SetupLiveHalRelations.Revoke, "DELETE")));
        allowedHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(5),
            capability: null,
            state: "revoked"));
        using var allowedClient = new HttpClient(allowedHandler, disposeHandler: false) { BaseAddress = Target };
        using var allowed = new SetupLiveAdapter(Target, TenantId, allowedClient, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(allowed);

        SetupLiveEnrollmentSnapshot revoked = await allowed.RevokeAsync(
            CancellationToken.None);

        CapturedRequest request = allowedHandler.Requests[1];
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Delete);
        await Assert.That(request.Uri).IsEqualTo(new Uri(
            Target,
            $"api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}"));
        await Assert.That(request.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(capability);
        await AssertCanaryPlacement(
            request,
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertUuid7(request.Header(SetupLiveContractMetadata.IdempotencyHeader));
        await Assert.That(revoked.State).IsEqualTo(SetupEnrollmentState.Revoked);
        await Assert.That(allowed.HasAuthority).IsFalse();
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => allowed.RefreshAsync(CancellationToken.None));
        await Assert.That(allowedHandler.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task RotationReplacesCapabilityAndMutationKeysAreAlwaysFresh()
    {
        string oldCapability = Capability(7);
        string newCapability = Capability(8);
        string newestCapability = Capability(9);
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            oldCapability,
            Link(SetupLiveHalRelations.RotateCapability, "POST")));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(10),
            newCapability,
            2,
            "issued",
            Link(SetupLiveHalRelations.Self),
            Link(SetupLiveHalRelations.RotateCapability, "POST"),
            Link(SetupLiveHalRelations.Revoke, "DELETE")));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(15),
            newestCapability,
            3,
            "issued",
            Link(SetupLiveHalRelations.Self),
            Link(SetupLiveHalRelations.Revoke, "DELETE")));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(15),
            capability: null,
            generation: 3,
            issuance: "already_issued",
            Link(SetupLiveHalRelations.Self),
            Link(SetupLiveHalRelations.Revoke, "DELETE")));
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(15),
            capability: null,
            state: "revoked",
            generation: 3,
            issuance: "already_issued"));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);

        _ = await adapter.RotateCapabilityAsync(CancellationToken.None);
        _ = await adapter.RotateCapabilityAsync(CancellationToken.None);
        _ = await adapter.RefreshAsync(CancellationToken.None);
        _ = await adapter.RevokeAsync(CancellationToken.None);

        CapturedRequest firstRotate = handler.Requests[1];
        CapturedRequest secondRotate = handler.Requests[2];
        CapturedRequest refresh = handler.Requests[3];
        CapturedRequest revoke = handler.Requests[4];
        string enrollmentKey = handler.Requests[0].Header(SetupLiveContractMetadata.IdempotencyHeader);
        string firstRotateKey = firstRotate.Header(SetupLiveContractMetadata.IdempotencyHeader);
        string secondRotateKey = secondRotate.Header(SetupLiveContractMetadata.IdempotencyHeader);
        string revokeKey = revoke.Header(SetupLiveContractMetadata.IdempotencyHeader);
        await Assert.That(firstRotate.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(oldCapability);
        await Assert.That(firstRotate.Uri).IsEqualTo(new Uri(
            Target,
            $"api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}/capability-rotations"));
        await Assert.That(firstRotate.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(secondRotate.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(secondRotate.Uri).IsEqualTo(firstRotate.Uri);
        await Assert.That(secondRotate.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(newCapability);
        await Assert.That(refresh.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(newestCapability);
        await Assert.That(refresh.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(refresh.Uri).IsEqualTo(new Uri(
            Target,
            $"api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}"));
        await Assert.That(refresh.Headers.ContainsKey(
            SetupLiveContractMetadata.IdempotencyHeader)).IsFalse();
        await Assert.That(revoke.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(newestCapability);
        await Assert.That(string.Join(',', refresh.Headers.Values.SelectMany(values => values)))
            .DoesNotContain(oldCapability);
        await AssertUuid7(enrollmentKey);
        await AssertUuid7(firstRotateKey);
        await AssertUuid7(secondRotateKey);
        await AssertUuid7(revokeKey);
        await Assert.That(new[]
        {
            enrollmentKey,
            firstRotateKey,
            secondRotateKey,
            revokeKey
        }.Distinct()).Count().IsEqualTo(4);
        await AssertCanaryPlacement(
            firstRotate,
            oldCapability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(
            secondRotate,
            newCapability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(
            refresh,
            newestCapability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(
            revoke,
            newestCapability,
            SetupLiveContractMetadata.CapabilityHeader);
    }

    [Test]
    public async Task InvalidRotatedCapabilityHeadersClearOldAuthority()
    {
        IReadOnlyList<string>[] invalidHeaders =
        [
            [],
            ["malformed"],
            [Capability(28), Capability(29)]
        ];

        foreach (IReadOnlyList<string> headers in invalidHeaders)
        {
            using var handler = new RecordingHandler();
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                Capability(30),
                Link(SetupLiveHalRelations.RotateCapability, "POST")));
            handler.Enqueue(() => EnrollmentResponseWithCapabilities(
                HttpStatusCode.OK,
                Now.AddMinutes(10),
                headers,
                2,
                "issued",
                Link(SetupLiveHalRelations.Self)));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
            await EnrollAsync(adapter);

            await Assert.ThrowsAsync<SetupLiveContractViolationException>(
                () => adapter.RotateCapabilityAsync(CancellationToken.None));
            await Assert.That(adapter.HasAuthority).IsFalse();
            await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                () => adapter.RefreshAsync(CancellationToken.None));
            await Assert.That(handler.Requests).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task EnrollmentGenerationTransitionsFailClosed()
    {
        using (var handler = new RecordingHandler())
        {
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                Capability(43),
                2,
                "issued"));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                TokenProvider(),
                new ManualTimeProvider(Now));

            await Assert.ThrowsAsync<SetupLiveContractViolationException>(
                () => EnrollAsync(adapter));
            await Assert.That(adapter.HasAuthority).IsFalse();
            await Assert.That(handler.Requests).Count().IsEqualTo(1);
        }

        (long Generation, DateTimeOffset ExpiresAt, string Issuance)[] invalidRotations =
        [
            (1, Now.AddMinutes(10), "issued"),
            (3, Now.AddMinutes(10), "issued"),
            (2, Now.AddMinutes(5), "issued"),
            (2, Now.AddMinutes(10), "already_issued")
        ];
        foreach ((long generation, DateTimeOffset expiresAt, string issuance)
            in invalidRotations)
        {
            using var handler = new RecordingHandler();
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                Capability(44),
                Link(SetupLiveHalRelations.RotateCapability, "POST")));
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.OK,
                expiresAt,
                Capability(45),
                generation,
                issuance,
                Link(SetupLiveHalRelations.Self)));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                TokenProvider(),
                new ManualTimeProvider(Now));
            await EnrollAsync(adapter);

            await Assert.ThrowsAsync<SetupLiveContractViolationException>(
                () => adapter.RotateCapabilityAsync(CancellationToken.None));
            await Assert.That(adapter.HasAuthority).IsFalse();
            await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                () => adapter.RefreshAsync(CancellationToken.None));
            await Assert.That(handler.Requests).Count().IsEqualTo(2);
        }

        using (var handler = new RecordingHandler())
        {
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                Capability(46),
                Link(SetupLiveHalRelations.Revoke, "DELETE")));
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.OK,
                Now.AddMinutes(5),
                null,
                "revoked",
                2,
                "already_issued"));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                TokenProvider(),
                new ManualTimeProvider(Now));
            await EnrollAsync(adapter);

            await Assert.ThrowsAsync<SetupLiveContractViolationException>(
                () => adapter.RevokeAsync(CancellationToken.None));
            await Assert.That(adapter.HasAuthority).IsFalse();
            await Assert.That(handler.Requests).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task PostDispatchMutationCancellationAndTimeoutClearAuthority()
    {
        foreach (string mutation in new[] { "rotate", "revoke", "write" })
        {
            var received = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new RecordingHandler();
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                Capability(52),
                mutation switch
                {
                    "rotate" => Link(SetupLiveHalRelations.RotateCapability, "POST"),
                    "revoke" => Link(SetupLiveHalRelations.Revoke, "DELETE"),
                    _ => Link(SetupLiveHalRelations.SecretBindingReadiness)
                }));
            if (mutation == "write")
                handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
            handler.Enqueue(async cancellationToken =>
            {
                received.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                TokenProvider(),
                new ManualTimeProvider(Now));
            await EnrollAsync(adapter);
            if (mutation == "write")
                _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            using var body = new MemoryStream([53], writable: false);
            Task request = mutation switch
            {
                "rotate" => adapter.RotateCapabilityAsync(cancellation.Token),
                "revoke" => adapter.RevokeAsync(cancellation.Token),
                _ => adapter.WriteSecretBindingAsync(
                    "setup.signing",
                    body,
                    cancellation.Token)
            };
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(() => request);
            await Assert.That(adapter.HasAuthority).IsFalse();
            int dispatchedRequests = mutation == "write" ? 3 : 2;
            await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                () => adapter.RefreshAsync(CancellationToken.None));
            await Assert.That(handler.Requests).Count().IsEqualTo(dispatchedRequests);
        }

        using var timeoutHandler = new RecordingHandler();
        timeoutHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(54),
            Link(SetupLiveHalRelations.RotateCapability, "POST")));
        timeoutHandler.Enqueue(_ => Task.FromException<HttpResponseMessage>(
            new TaskCanceledException("simulated transport timeout")));
        using var timeoutClient = new HttpClient(timeoutHandler, disposeHandler: false) { BaseAddress = Target };
        using var timeoutAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            timeoutClient,
            TokenProvider(),
            new ManualTimeProvider(Now));
        await EnrollAsync(timeoutAdapter);

        SetupLiveAuthorityUnavailableException timeoutException =
            await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                () => timeoutAdapter.RotateCapabilityAsync(CancellationToken.None))
            ?? throw new InvalidOperationException("Expected bounded timeout failure.");
        await Assert.That(timeoutException.InnerException).IsNull();
        await Assert.That(timeoutException.ToString())
            .DoesNotContain("simulated transport timeout");
        await Assert.That(timeoutAdapter.HasAuthority).IsFalse();
        await Assert.That(timeoutHandler.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task ExpiryClearsAuthorityBeforeAnyFurtherRequest()
    {
        var clock = new ManualTimeProvider(Now);
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddSeconds(30),
            Capability(13),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), clock);
        await EnrollAsync(adapter);
        clock.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
            () => adapter.GetSecretBindingReadinessAsync(
                CancellationToken.None));

        await Assert.That(adapter.HasAuthority).IsFalse();
        await Assert.That(handler.Requests).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ReadinessHalGatesWriteAndGeneratedClientSendsOnlyRequiredAuthority()
    {
        string capability = Capability(17);
        string secretCanary = $"secret-{Guid.CreateVersion7():N}";
        byte[] secret = Encoding.UTF8.GetBytes(secretCanary);
        Guid operationId = Guid.CreateVersion7();
        Guid secondOperationId = Guid.CreateVersion7();
        using var telemetry = new SensitiveTelemetryCapture();
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            capability,
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: false));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        handler.Enqueue(() => OperationResponse(operationId));
        handler.Enqueue(() => OperationResponse(operationId, HttpStatusCode.OK));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        handler.Enqueue(() => OperationResponse(secondOperationId));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);
        IReadOnlyList<SetupLiveSecretBindingReadiness> readiness =
            await adapter.GetSecretBindingReadinessAsync(
                CancellationToken.None);
        using var deniedBody = new MemoryStream(secret, writable: false);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
            adapter.WriteSecretBindingAsync(
                "setup.signing",
                deniedBody,
                CancellationToken.None));
        await Assert.That(deniedBody.Position).IsEqualTo(0);
        await Assert.That(handler.Requests).Count().IsEqualTo(2);

        readiness = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var body = new MemoryStream(secret, writable: false);

        SetupLiveSecretBindingOperation operation =
            await adapter.WriteSecretBindingAsync(
                "setup.signing",
                body,
                CancellationToken.None);
        SetupLiveSecretBindingOperation settled =
            await adapter.GetSecretBindingOperationAsync(
                operationId,
                CancellationToken.None);
        _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var secondBody = new MemoryStream(secret, writable: false);
        SetupLiveSecretBindingOperation secondOperation =
            await adapter.WriteSecretBindingAsync(
                "setup.signing",
                secondBody,
                CancellationToken.None);

        CapturedRequest request = handler.Requests[3];
        CapturedRequest firstReadiness = handler.Requests[1];
        CapturedRequest secondReadiness = handler.Requests[2];
        CapturedRequest thirdReadiness = handler.Requests[5];
        CapturedRequest secondWrite = handler.Requests[6];
        await Assert.That(readiness.Single().CanWrite).IsTrue();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Put);
        await Assert.That(request.Uri).IsEqualTo(new Uri(
            Target,
            $"api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}/secret-bindings/setup.signing"));
        await Assert.That(request.ContentType).IsEqualTo(SetupLiveContractMetadata.SecretWriteRequestMediaType);
        await Assert.That(request.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(capability);
        await AssertCanaryPlacement(
            request,
            capability,
            SetupLiveContractMetadata.CapabilityHeader,
            allowBody: false);
        await AssertCanaryPlacement(request, secretCanary, allowBody: true);
        string enrollmentKey = handler.Requests[0].Header(
            SetupLiveContractMetadata.IdempotencyHeader);
        string writeKey = request.Header(SetupLiveContractMetadata.IdempotencyHeader);
        string secondWriteKey = secondWrite.Header(
            SetupLiveContractMetadata.IdempotencyHeader);
        await AssertUuid7(enrollmentKey);
        await AssertUuid7(writeKey);
        await AssertUuid7(secondWriteKey);
        await Assert.That(new[] { enrollmentKey, writeKey, secondWriteKey }.Distinct())
            .Count().IsEqualTo(3);
        await Assert.That(request.Body).IsEquivalentTo(secret);
        await Assert.That(operation.OperationId).IsEqualTo(operationId);
        await Assert.That(settled.OperationId).IsEqualTo(operationId);
        await Assert.That(secondOperation.OperationId).IsEqualTo(secondOperationId);
        CapturedRequest operationRead = handler.Requests[4];
        await Assert.That(operationRead.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(operationRead.Uri).IsEqualTo(new Uri(
            Target,
            $"api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}/secret-binding-operations/{operationId:D}"));
        await Assert.That(operationRead.Header(SetupLiveContractMetadata.CapabilityHeader))
            .IsEqualTo(capability);
        await Assert.That(operationRead.Headers.ContainsKey(
            SetupLiveContractMetadata.IdempotencyHeader)).IsFalse();
        foreach (CapturedRequest readinessRequest in new[]
        {
            firstReadiness,
            secondReadiness,
            thirdReadiness
        })
        {
            await Assert.That(readinessRequest.Method).IsEqualTo(HttpMethod.Get);
            await Assert.That(readinessRequest.Uri).IsEqualTo(new Uri(
                Target,
                $"api/tenants/{TenantId:D}/setup/enrollments/{EnrollmentId:D}/secret-bindings/readiness"));
            await Assert.That(readinessRequest.Headers.ContainsKey(
                SetupLiveContractMetadata.IdempotencyHeader)).IsFalse();
        }
        await AssertCanaryPlacement(
            firstReadiness,
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(
            secondReadiness,
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(
            thirdReadiness,
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(
            secondWrite,
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await AssertCanaryPlacement(secondWrite, secretCanary, allowBody: true);
        await AssertCanaryPlacement(
            operationRead,
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await Assert.That(operation.ToString()).DoesNotContain(capability);
        await Assert.That(operation.ToString()).DoesNotContain(secretCanary);
        await Assert.That(telemetry.Text).DoesNotContain(secretCanary);
    }

    [Test]
    public async Task RefreshAndMalformedReadinessClearNestedAffordances()
    {
        Guid operationId = Guid.CreateVersion7();
        using var refreshHandler = new RecordingHandler();
        refreshHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(47),
            Link(SetupLiveHalRelations.Self),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        refreshHandler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        refreshHandler.Enqueue(() => OperationResponse(operationId));
        refreshHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.OK,
            Now.AddMinutes(5),
            capability: null,
            Link(SetupLiveHalRelations.Self)));
        using var refreshClient = new HttpClient(refreshHandler, disposeHandler: false) { BaseAddress = Target };
        using var refreshAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            refreshClient,
            TokenProvider(),
            new ManualTimeProvider(Now));
        await EnrollAsync(refreshAdapter);
        _ = await refreshAdapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using (var firstBody = new MemoryStream([48], writable: false))
        {
            _ = await refreshAdapter.WriteSecretBindingAsync(
                "setup.signing",
                firstBody,
                CancellationToken.None);
        }
        _ = await refreshAdapter.RefreshAsync(CancellationToken.None);
        using var blockedBody = new MemoryStream([49], writable: false);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
            refreshAdapter.WriteSecretBindingAsync(
                "setup.signing",
                blockedBody,
                CancellationToken.None));
        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
            refreshAdapter.GetSecretBindingOperationAsync(
                operationId,
                CancellationToken.None));
        await Assert.That(blockedBody.Position).IsEqualTo(0);
        await Assert.That(refreshHandler.Requests).Count().IsEqualTo(4);

        using var malformedHandler = new RecordingHandler();
        malformedHandler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(50),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        malformedHandler.Enqueue(() => JsonResponse(HttpStatusCode.OK, new
        {
            _links = new Dictionary<string, object>(),
            _embedded = new { items = new object?[] { null } }
        }));
        using var malformedClient = new HttpClient(malformedHandler, disposeHandler: false) { BaseAddress = Target };
        using var malformedAdapter = new SetupLiveAdapter(
            Target,
            TenantId,
            malformedClient,
            TokenProvider(),
            new ManualTimeProvider(Now));
        await EnrollAsync(malformedAdapter);

        await Assert.ThrowsAsync<SetupLiveContractViolationException>(
            () => malformedAdapter.GetSecretBindingReadinessAsync(
                CancellationToken.None));
        await Assert.That(malformedAdapter.HasAuthority).IsFalse();
        using var unreadBody = new MemoryStream([51], writable: false);
        await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(() =>
            malformedAdapter.WriteSecretBindingAsync(
                "setup.signing",
                unreadBody,
                CancellationToken.None));
        await Assert.That(unreadBody.Position).IsEqualTo(0);
        await Assert.That(malformedHandler.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task WrongMethodWriteAffordanceNeverReadsOrDispatches()
    {
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(20),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        handler.Enqueue(() => ReadinessResponse(
            "setup.signing",
            canWrite: true,
            writeMethod: "PATCH"));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);
        _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var body = new MemoryStream([31], writable: false);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
            adapter.WriteSecretBindingAsync(
                "setup.signing",
                body,
                CancellationToken.None));

        await Assert.That(body.Position).IsEqualTo(0);
        await Assert.That(handler.Requests).Count().IsEqualTo(2);
    }

    [Test]
    public async Task WriteRequiresExactReadyBindingEvenWhenAnotherWriteLinkExists()
    {
        (string ResponseKey, string State, string RequestedKey)[] cases =
        [
            ("setup.signing", "ready", "setup.encryption"),
            ("setup.signing", "unavailable", "setup.signing")
        ];

        foreach ((string responseKey, string state, string requestedKey) in cases)
        {
            using var handler = new RecordingHandler();
            handler.Enqueue(() => EnrollmentResponse(
                HttpStatusCode.Created,
                Now.AddMinutes(5),
                Capability(36),
                Link(SetupLiveHalRelations.SecretBindingReadiness)));
            handler.Enqueue(() => ReadinessResponse(
                responseKey,
                canWrite: true,
                state: state));
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
            using var adapter = new SetupLiveAdapter(
                Target,
                TenantId,
                client,
                TokenProvider(),
                new ManualTimeProvider(Now));
            await EnrollAsync(adapter);
            IReadOnlyList<SetupLiveSecretBindingReadiness> readiness =
                await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
            using var body = new MemoryStream([37], writable: false);

            await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
                adapter.WriteSecretBindingAsync(
                    requestedKey,
                    body,
                    CancellationToken.None));

            if (state == "unavailable")
                await Assert.That(readiness.Single().CanWrite).IsFalse();
            await Assert.That(body.Position).IsEqualTo(0);
            await Assert.That(handler.Requests).Count().IsEqualTo(2);
        }
    }

    [Test]
    public async Task MissingOperationReadAffordanceNeverDispatches()
    {
        Guid operationId = Guid.CreateVersion7();
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(21),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        handler.Enqueue(() => OperationResponse(
            operationId,
            HttpStatusCode.Accepted,
            canRead: false));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);
        _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var body = new MemoryStream([41], writable: false);
        _ = await adapter.WriteSecretBindingAsync(
            "setup.signing",
            body,
            CancellationToken.None);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
            adapter.GetSecretBindingOperationAsync(operationId, CancellationToken.None));
        await Assert.That(handler.Requests).Count().IsEqualTo(3);
    }

    [Test]
    public async Task WrongMethodOperationReadAffordanceNeverDispatches()
    {
        Guid operationId = Guid.CreateVersion7();
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            Capability(23),
            Link(SetupLiveHalRelations.SecretBindingReadiness)));
        handler.Enqueue(() => ReadinessResponse("setup.signing", canWrite: true));
        handler.Enqueue(() => OperationResponse(
            operationId,
            operationMethod: "POST"));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);
        _ = await adapter.GetSecretBindingReadinessAsync(CancellationToken.None);
        using var body = new MemoryStream([43], writable: false);
        _ = await adapter.WriteSecretBindingAsync(
            "setup.signing",
            body,
            CancellationToken.None);

        await Assert.ThrowsAsync<SetupLiveAffordanceUnavailableException>(() =>
            adapter.GetSecretBindingOperationAsync(operationId, CancellationToken.None));
        await Assert.That(handler.Requests).Count().IsEqualTo(3);
    }

    [Test]
    public async Task ProblemDetailsCannotEscapeThroughExceptionOrTelemetry()
    {
        string capability = Capability(22);
        string problemCanary = $"problem-{Guid.CreateVersion7():N}";
        string providerCanary = $"provider-coordinate-{Guid.CreateVersion7():N}";
        using var telemetry = new SensitiveTelemetryCapture();
        using var handler = new RecordingHandler();
        handler.Enqueue(() => EnrollmentResponse(
            HttpStatusCode.Created,
            Now.AddMinutes(5),
            capability,
            Link(SetupLiveHalRelations.Self)));
        handler.Enqueue(() => ProblemResponse(capability, problemCanary, providerCanary));
        using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = Target };
        using var adapter = new SetupLiveAdapter(Target, TenantId, client, TokenProvider(), new ManualTimeProvider(Now));
        await EnrollAsync(adapter);

        SetupLiveAuthorityUnavailableException exception =
            await Assert.ThrowsAsync<SetupLiveAuthorityUnavailableException>(
                () => adapter.RefreshAsync(CancellationToken.None))
            ?? throw new InvalidOperationException("Expected bounded authority failure.");

        await Assert.That(exception.ToString()).DoesNotContain(capability);
        await Assert.That(exception.ToString()).DoesNotContain(problemCanary);
        await Assert.That(exception.ToString()).DoesNotContain(providerCanary);
        await Assert.That(exception.ToString()).DoesNotContain(AuthenticationToken);
        await Assert.That(telemetry.Text).DoesNotContain(capability);
        await Assert.That(telemetry.Text).DoesNotContain(problemCanary);
        await Assert.That(telemetry.Text).DoesNotContain(providerCanary);
        await Assert.That(telemetry.Text).DoesNotContain(AuthenticationToken);
        await AssertCanaryPlacement(
            handler.Requests[0],
            AuthenticationToken,
            "Authorization");
        await AssertCanaryPlacement(
            handler.Requests[1],
            AuthenticationToken,
            "Authorization");
        await AssertCanaryPlacement(
            handler.Requests[1],
            capability,
            SetupLiveContractMetadata.CapabilityHeader);
        await Assert.That(adapter.HasAuthority).IsFalse();
    }

    private static Task<SetupLiveEnrollmentSnapshot> EnrollAsync(SetupLiveAdapter adapter) =>
        adapter.EnrollAsync(
            SetupClientChallenge.FromBytes(Enumerable.Repeat((byte)19, 32).ToArray()),
            [
                SetupEnrollmentScope.TargetRead,
                SetupEnrollmentScope.SecretBindingReadiness,
                SetupEnrollmentScope.SecretBindingWrite
            ],
            CancellationToken.None);

    private static HttpResponseMessage EnrollmentResponse(
        HttpStatusCode status,
        DateTimeOffset expiresAt,
        string? capability,
        params TestLink[] relations) => EnrollmentResponse(
        status,
        expiresAt,
        capability,
        "active",
        1,
        status == HttpStatusCode.Created ? "issued" : "already_issued",
        relations);

    private static HttpResponseMessage EnrollmentResponse(
        HttpStatusCode status,
        DateTimeOffset expiresAt,
        string? capability,
        string state,
        params TestLink[] relations) => EnrollmentResponse(
            status,
            expiresAt,
            capability,
            state,
            1,
            status == HttpStatusCode.Created ? "issued" : "already_issued",
            relations);

    private static HttpResponseMessage EnrollmentResponse(
        HttpStatusCode status,
        DateTimeOffset expiresAt,
        string? capability,
        long generation,
        string issuance,
        params TestLink[] relations) => EnrollmentResponse(
            status,
            expiresAt,
            capability,
            "active",
            generation,
            issuance,
            relations);

    private static HttpResponseMessage EnrollmentResponse(
        HttpStatusCode status,
        DateTimeOffset expiresAt,
        string? capability,
        string state,
        long generation,
        string issuance,
        params TestLink[] relations)
    {
        var links = relations.ToDictionary(
            link => link.Relation,
            link => new
            {
                href = link.Href,
                method = link.Method
            },
            StringComparer.Ordinal);
        HttpResponseMessage response = JsonResponse(status, new
        {
            enrollmentId = EnrollmentId,
            state,
            generation,
            expiresAt,
            scopes = AllScopes,
            issuance,
            _links = links
        });
        if (capability is not null)
        {
            response.Headers.TryAddWithoutValidation(
                SetupLiveContractMetadata.CapabilityHeader,
                capability);
        }
        return response;
    }

    private static HttpResponseMessage EnrollmentResponseWithCapabilities(
        HttpStatusCode status,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> capabilities,
        params TestLink[] relations) => EnrollmentResponseWithCapabilities(
            status,
            expiresAt,
            capabilities,
            1,
            status == HttpStatusCode.Created ? "issued" : "already_issued",
            relations);

    private static HttpResponseMessage EnrollmentResponseWithCapabilities(
        HttpStatusCode status,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> capabilities,
        long generation,
        string issuance,
        params TestLink[] relations)
    {
        HttpResponseMessage response = EnrollmentResponse(
            status,
            expiresAt,
            null,
            generation,
            issuance,
            relations);
        if (capabilities.Count > 0)
        {
            response.Headers.TryAddWithoutValidation(
                SetupLiveContractMetadata.CapabilityHeader,
                capabilities);
        }
        return response;
    }

    private static HttpResponseMessage ReadinessResponse(
        string bindingKey,
        bool canWrite,
        string writeMethod = "PUT",
        string state = "ready") =>
        JsonResponse(HttpStatusCode.OK, new
        {
            _links = new Dictionary<string, object>
            {
                [SetupLiveHalRelations.Self] = new { href = "readiness", method = "GET" }
            },
            _embedded = new
            {
                items = new[]
                {
                    new
                    {
                        bindingKey,
                        state,
                        _links = canWrite
                            ? new Dictionary<string, object>
                            {
                                [SetupLiveHalRelations.WriteSecretBinding] =
                                    new { href = $"secret-bindings/{bindingKey}", method = writeMethod }
                            }
                            : new Dictionary<string, object>()
                    }
                }
            }
        });

    private static HttpResponseMessage OperationResponse(
        Guid operationId,
        HttpStatusCode status = HttpStatusCode.Accepted,
        bool canRead = true,
        string? operationMethod = null,
        long enrollmentGeneration = 1) =>
        JsonResponse(status, new
        {
            operationId,
            state = status == HttpStatusCode.Accepted ? "accepted" : "succeeded",
            outcome = status == HttpStatusCode.Accepted ? "accepted" : "ready",
            enrollmentGeneration,
            createdAt = Now,
            settledAt = status == HttpStatusCode.Accepted
                ? (DateTimeOffset?)null
                : Now.AddSeconds(1),
            _links = canRead
                ? new Dictionary<string, object>
                {
                    [SetupLiveHalRelations.SecretBindingOperation] = new
                    {
                        href = $"https://non-routing-operation-canary.invalid/{Guid.CreateVersion7():N}",
                        method = operationMethod
                    }
                }
                : new Dictionary<string, object>()
        });

    private static HttpResponseMessage ProblemResponse(
        string capability,
        string canary,
        string providerCanary) => new(HttpStatusCode.NotFound)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                type = SetupLiveProblemContracts.UnavailableType,
                title = SetupLiveProblemContracts.UnavailableTitle,
                status = SetupLiveProblemContracts.UnavailableStatus,
                detail = $"{capability}:{canary}:{providerCanary}"
            }),
            Encoding.UTF8,
            SetupLiveContractMetadata.ErrorMediaType)
    };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            SetupLiveContractMetadata.SuccessMediaType)
    };

    private static HttpResponseMessage RedirectResponse(Uri location) =>
        new(HttpStatusCode.TemporaryRedirect)
        {
            Headers = { Location = location }
        };

    private static string Capability(byte value) =>
        SetupEnrollmentCapability.FromBytes(Enumerable.Repeat(value, 32).ToArray())
            .ToHeaderValue();

    private static SetupLiveAccessTokenProvider TokenProvider(
        string? token = null,
        DateTimeOffset? expiresAt = null) =>
        _ => ValueTask.FromResult<SetupLiveAccessToken?>(
            SetupLiveAccessToken.Create(
                token ?? AuthenticationToken,
                expiresAt ?? Now.AddHours(1)));

    private static TestLink Link(string relation, string? method = null) =>
        new(
            relation,
            $"https://non-routing-hal-canary.invalid/{Guid.CreateVersion7():N}",
            method);

    private static async Task AssertUuid7(string value)
    {
        await Assert.That(Guid.TryParse(value, out Guid parsed)).IsTrue();
        string canonical = parsed.ToString("D");
        await Assert.That(canonical[14]).IsEqualTo('7');
        await Assert.That(canonical[19] is '8' or '9' or 'a' or 'b').IsTrue();
    }

    private static async Task AssertCanaryPlacement(
        CapturedRequest request,
        string canary,
        string? allowedHeader = null,
        bool allowBody = false)
    {
        await Assert.That(request.Uri.AbsoluteUri).DoesNotContain(canary);
        foreach ((string name, string[] values) in request.Headers)
        {
            if (!string.Equals(name, allowedHeader, StringComparison.OrdinalIgnoreCase))
                await Assert.That(string.Join(',', values)).DoesNotContain(canary);
        }
        if (!allowBody)
        {
            await Assert.That(Encoding.UTF8.GetString(request.Body))
                .DoesNotContain(canary);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan duration) => Now += duration;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>>
            _responses = new();

        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(Func<HttpResponseMessage> response) =>
            _responses.Enqueue(_ => Task.FromResult(response()));

        public void Enqueue(
            Func<CancellationToken, Task<HttpResponseMessage>> response) =>
            _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Concat(request.Content is null
                    ? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()
                    : request.Content.Headers)
                    .ToDictionary(
                        header => header.Key,
                        header => header.Value.ToArray(),
                        StringComparer.OrdinalIgnoreCase),
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Accept.SingleOrDefault()?.MediaType,
                body));
            if (_responses.Count == 0)
                throw new InvalidOperationException("Unexpected Setup live request.");
            HttpResponseMessage response = await _responses.Dequeue()(cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class SensitiveTelemetryCapture : IDisposable
    {
        private readonly List<string> _values = [];
        private readonly ActivityListener _activities;
        private readonly MeterListener _meters = new();

        public SensitiveTelemetryCapture()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    _values.Add(activity.DisplayName);
                    _values.AddRange(activity.Tags.Select(tag => $"{tag.Key}:{tag.Value}"));
                    _values.AddRange(activity.Events.SelectMany(@event =>
                        @event.Tags.Select(tag => $"{tag.Key}:{tag.Value}")));
                }
            };
            ActivitySource.AddActivityListener(_activities);
            _meters.InstrumentPublished = (instrument, listener) =>
                listener.EnableMeasurementEvents(instrument);
            _meters.SetMeasurementEventCallback<long>(Capture);
            _meters.SetMeasurementEventCallback<double>(Capture);
            _meters.Start();
        }

        public string Text => string.Join('\n', _values);

        public void Dispose()
        {
            _activities.Dispose();
            _meters.Dispose();
        }

        private void Capture<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where T : struct
        {
            _values.Add(instrument.Name);
            foreach ((string key, object? value) in tags)
                _values.Add($"{key}:{value}");
        }
    }

    private sealed record TestLink(string Relation, string Href, string? Method);

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string[]> Headers,
        string? ContentType,
        string? Accept,
        byte[] Body)
    {
        public string Header(string name) => Headers[name].Single();
    }
}
