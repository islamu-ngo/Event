using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SecretKit.Core;
using SecretKit.Infisical;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitNativeWriterTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task UnattachedAttemptHasNoDispatchEvidence()
    {
        var attempt = new SecretMutationAttempt();

        await Assert.That(attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AcceptedWithoutAnAppliedAcknowledgmentCannotDeliverOrInvalidate(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        fixture.PatchStatus = StatusCodes.Status202Accepted;
        fixture.PatchReleased.TrySetResult();
        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence is SecretMutationEvidence.Pending
            or SecretMutationEvidence.PossiblyDispatched).IsTrue();
        await Assert.That(fixture.Resolver.InvalidatedKey).IsNull();
        await Assert.That(fixture.Patches).IsEqualTo(1);
    }

    [Test]
    [Arguments(SecretMutationEvidence.NotDispatched)]
    [Arguments(SecretMutationEvidence.PossiblyDispatched)]
    [Arguments(SecretMutationEvidence.Applied)]
    [Arguments(SecretMutationEvidence.Pending)]
    [Arguments(SecretMutationEvidence.Rejected)]
    [Arguments(SecretMutationEvidence.Conflict)]
    public async Task AttemptReadsCurrentProjectionWithoutOwningItsState(SecretMutationEvidence evidence)
    {
        var attempt = new SecretMutationAttempt();
        var observed = SecretMutationEvidence.NotDispatched;
        attempt.AttachProjection(() => observed);
        observed = evidence;

        await Assert.That(attempt.Evidence).IsEqualTo(evidence);
    }

    [Test]
    public async Task SecondAttachmentCannotReplaceOriginalProjection()
    {
        var attempt = new SecretMutationAttempt();
        var observed = SecretMutationEvidence.PossiblyDispatched;
        attempt.AttachProjection(() => observed);

        await Assert.That(() => attempt.AttachProjection(() => SecretMutationEvidence.Rejected))
            .Throws<InvalidOperationException>();
        observed = SecretMutationEvidence.Applied;
        await Assert.That(attempt.Evidence).IsEqualTo(SecretMutationEvidence.Applied);
    }

    [Test]
    [Arguments(SecretDefinitionRegistry.Keys.SetupSigning)]
    [Arguments(SecretDefinitionRegistry.Keys.SetupEncryption)]
    [Arguments(SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret)]
    [Arguments(SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret)]
    public async Task AppliedUpdateUsesOnlyCanonicalPatchAndInvalidatesExactInstanceBinding(string key)
    {
        await using var fixture = await WriterFixture.OpenAsync(key);
        Task<bool> write = fixture.WriteAsync();
        await fixture.PatchEntered.Task.WaitAsync(TestTimeout);
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.PossiblyDispatched);
        fixture.PatchReleased.TrySetResult();

        await Assert.That(await write.WaitAsync(TestTimeout)).IsTrue();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.Applied);
        await Assert.That(fixture.Patches).IsEqualTo(1);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
        await Assert.That(fixture.MatchedTargetAndValue).IsTrue();
        await Assert.That(fixture.Resolver.InvalidatedKey).IsEqualTo(key);
        await Assert.That(fixture.Resolver.InvalidatedScope).IsEqualTo(SecretScope.Instance);
        await Assert.That(fixture.Resolver.InvalidatedScopeId).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ReusingRequestCannotRedispatchItsOriginalReceipt(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        Task<bool> first = fixture.WriteAsync();
        await fixture.PatchEntered.Task.WaitAsync(TestTimeout);

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Patches).IsEqualTo(1);
        fixture.PatchReleased.TrySetResult();
        await Assert.That(await first.WaitAsync(TestTimeout)).IsTrue();
        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Patches).IsEqualTo(1);
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.Applied);
    }

    [Test]
    [Arguments(true, 403, SecretMutationEvidence.Rejected)]
    [Arguments(false, 403, SecretMutationEvidence.Rejected)]
    [Arguments(true, 404, SecretMutationEvidence.Rejected)]
    [Arguments(false, 404, SecretMutationEvidence.Rejected)]
    [Arguments(true, 409, SecretMutationEvidence.Conflict)]
    [Arguments(false, 409, SecretMutationEvidence.Conflict)]
    public async Task ProviderRejectionCannotDeliverOrCreateMissingMaterial(
        bool setup, int status, SecretMutationEvidence expected)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        fixture.PatchStatus = status;
        fixture.PatchReleased.TrySetResult();

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(expected);
        await Assert.That(fixture.Patches).IsEqualTo(1);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
        await Assert.That(fixture.Resolver.InvalidatedKey).IsNull();
        if (!setup)
        {
            await Assert.That(fixture.AuthenticationResult!.Outcome).IsEqualTo(status switch
            {
                403 => AuthenticationRuntimeWriteOutcome.Unauthorized,
                404 => AuthenticationRuntimeWriteOutcome.Missing,
                _ => AuthenticationRuntimeWriteOutcome.Unavailable
            });
            await Assert.That(fixture.AuthenticationResult.Mutation)
                .IsEqualTo(AuthenticationRuntimeSourceMutation.Unknown);
        }
        else if (status == 403)
        {
            await Assert.That(fixture.SetupResult).IsEqualTo(SetupSecretBindingWriteOutcome.Unauthorized);
        }
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LostResponsePreservesPossibleDispatchWithoutReadOrReplay(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        fixture.LoseResponse = true;
        fixture.PatchReleased.TrySetResult();

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.PossiblyDispatched);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Patches).IsEqualTo(1);
        await Assert.That(fixture.Resolver.InvalidatedKey).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CallerCancellationRetainsReceiptAfterActualNativeTaskUnwinds(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        using var cancellation = new CancellationTokenSource();
        Task<bool> write = fixture.WriteAsync(cancellation.Token);
        await fixture.PatchEntered.Task.WaitAsync(TestTimeout);
        cancellation.Cancel();

        await Assert.That(async () => await write.WaitAsync(TestTimeout))
            .Throws<OperationCanceledException>();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.PossiblyDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(1);
        await Assert.That(fixture.Resolver.InvalidatedKey).IsNull();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task OwnerRetirementCancelsBorrowedNativeWorkWithoutAbandoningIt(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        Task<bool> write = fixture.WriteAsync();
        await fixture.PatchEntered.Task.WaitAsync(TestTimeout);
        Task retirement = fixture.Owner.DisposeAsync().AsTask();

        await Assert.That(await write.WaitAsync(TestTimeout)).IsFalse();
        await retirement.WaitAsync(TestTimeout);
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.PossiblyDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CacheFailureCannotUndoAppliedReceiptOrReportDelivery(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        fixture.Resolver.FailInvalidation = true;
        fixture.PatchReleased.TrySetResult();

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.Applied);
        await Assert.That(fixture.Resolver.InvalidatedKey).IsEqualTo(Key(setup));
        if (!setup)
            await Assert.That(fixture.AuthenticationResult!.Mutation)
                .IsEqualTo(AuthenticationRuntimeSourceMutation.ConfirmedWritten);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CancellationAfterAppliedResponseCannotEraseEffectEvidence(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        using var cancellation = new CancellationTokenSource();
        fixture.Resolver.OnInvalidation = () => cancellation.Cancel();
        fixture.PatchReleased.TrySetResult();

        await Assert.That(await fixture.WriteAsync(cancellation.Token)).IsTrue();
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.Applied);
        await Assert.That(fixture.Resolver.InvalidationWasCancelable).IsFalse();
    }

    [Test]
    [Arguments(true, "scope")]
    [Arguments(false, "scope")]
    [Arguments(true, "qualifier")]
    [Arguments(false, "qualifier")]
    [Arguments(true, "environment")]
    [Arguments(false, "environment")]
    [Arguments(true, "path")]
    [Arguments(false, "path")]
    [Arguments(true, "key")]
    [Arguments(false, "key")]
    [Arguments(true, "source")]
    [Arguments(false, "source")]
    [Arguments(true, "missing")]
    [Arguments(false, "missing")]
    public async Task NoncanonicalOrMissingMetadataCannotReachNativeMutation(bool setup, string field)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        switch (field)
        {
            case "scope": fixture.Binding.ScopeId = Guid.CreateVersion7(); break;
            case "qualifier": fixture.Binding.Qualifier = "other"; break;
            case "environment": fixture.Binding.InfisicalEnvironment = "other"; break;
            case "path": fixture.Binding.InfisicalPath = "/other"; break;
            case "key": fixture.Binding.InfisicalKey = "OTHER"; break;
            case "source": fixture.Binding.SourceType = SecretSourceType.EnvironmentVariable; break;
            case "missing": fixture.Bindings.Clear(); break;
        }

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(0);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
    }

    [Test]
    [Arguments(true, "malformed")]
    [Arguments(false, "malformed")]
    [Arguments(false, "empty")]
    [Arguments(false, "oversized")]
    public async Task InvalidUtf8OrCredentialLengthCannotAttachNativeReceipt(bool setup, string form)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        byte[] input = form switch
        {
            "empty" => [],
            "malformed" => [0xff],
            _ => RandomNumberGenerator.GetBytes(16 * 1024 + 1)
        };
        try
        {
            fixture.ReplaceRequest(input);
            await Assert.That(await fixture.WriteAsync()).IsFalse();
            await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
            await Assert.That(fixture.Patches).IsEqualTo(0);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    [Test]
    [Arguments("binding")]
    [Arguments("source")]
    [Arguments("environment")]
    [Arguments("path")]
    [Arguments("key")]
    [Arguments("origin")]
    [Arguments("project")]
    [Arguments("target")]
    public async Task ChangedPreparedSelectionCannotRedirectCanonicalRuntimeWrite(string field)
    {
        await using var fixture = await WriterFixture.OpenAsync(
            SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret);
        fixture.ChangeSelection(field);

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.AuthenticationResult!.Outcome).IsEqualTo(field == "target"
            ? AuthenticationRuntimeWriteOutcome.Missing : AuthenticationRuntimeWriteOutcome.Invalid);
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(0);
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    public async Task LocalAuthorityRequiresOperatorInjectionAndNeverFallsBack(SecretAuthorityKind provider)
    {
        await using var fixture = await WriterFixture.OpenAsync(
            SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret, provider: provider);

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.AuthenticationResult!.Outcome)
            .IsEqualTo(AuthenticationRuntimeWriteOutcome.OperatorInjectionRequired);
        await Assert.That(fixture.AuthenticationResult.OperatorVariable)
            .IsEqualTo(fixture.Definition.DefaultEnvironmentVariableName);
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(0);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task WrongConsumerCannotBorrowAnotherHostsWriteAuthority(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup),
            consumer: setup ? SecretConsumerRole.Bff : SecretConsumerRole.Api);

        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(0);
    }

    [Test]
    [Arguments(SecretDefinitionRegistry.Keys.SetupSigning)]
    [Arguments(SecretDefinitionRegistry.Keys.SetupEncryption)]
    public async Task SetupReadinessAdmitsMetadataWithoutReadingWritingOrLoggingInAgain(string key)
    {
        await using var fixture = await WriterFixture.OpenAsync(key);
        var writer = new SetupSecretBindingWriter(fixture.Bindings, fixture.Owner, fixture.Resolver);

        var readiness = await writer.GetReadinessAsync(fixture.Binding.Id, key, CancellationToken.None);

        await Assert.That(readiness).IsEqualTo(SetupSecretBindingWriteOutcome.Ready);
        await Assert.That(fixture.Logins).IsEqualTo(1);
        await Assert.That(fixture.Patches).IsEqualTo(0);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        fixture.Binding.InfisicalKey = "OTHER";
        await Assert.That(await writer.GetReadinessAsync(fixture.Binding.Id, key, CancellationToken.None))
            .IsEqualTo(SetupSecretBindingWriteOutcome.Invalid);
        await fixture.Owner.DisposeAsync();
        fixture.Binding.InfisicalKey = fixture.Definition.DefaultInfisicalKey;
        await Assert.That(await writer.GetReadinessAsync(fixture.Binding.Id, key, CancellationToken.None))
            .IsEqualTo(SetupSecretBindingWriteOutcome.Unavailable);
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    public async Task LocalSetupAuthorityCannotOfferDeliveryOrRemoteReadiness(SecretAuthorityKind provider)
    {
        await using var fixture = await WriterFixture.OpenAsync(
            SecretDefinitionRegistry.Keys.SetupSigning, provider: provider);
        var writer = new SetupSecretBindingWriter(fixture.Bindings, fixture.Owner, fixture.Resolver);

        await Assert.That(await writer.GetReadinessAsync(
            fixture.Binding.Id, fixture.Definition.Key, CancellationToken.None))
            .IsEqualTo(SetupSecretBindingWriteOutcome.Unavailable);
        await Assert.That(await fixture.WriteAsync()).IsFalse();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(0);
        await Assert.That(fixture.Logins).IsEqualTo(0);
        await Assert.That(fixture.UnexpectedRequests).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PredispatchCancellationLeavesRequestOwnedAttemptUnattached(bool setup)
    {
        await using var fixture = await WriterFixture.OpenAsync(Key(setup));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(async () => await fixture.WriteAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(fixture.Attempt.Evidence).IsEqualTo(SecretMutationEvidence.NotDispatched);
        await Assert.That(fixture.Patches).IsEqualTo(0);
    }

    private static string Key(bool setup) => setup ? SecretDefinitionRegistry.Keys.SetupSigning
        : SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret;

    private sealed class WriterFixture : IAsyncDisposable
    {
        private readonly WebApplication _server;
        private readonly byte[] _credential = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
        private readonly string _token = Guid.NewGuid().ToString("N");
        private readonly string _resourceId = Guid.NewGuid().ToString("N");
        private int _logins;
        private int _patches;
        private int _unexpectedRequests;
        private SetupSecretBindingWriteRequest? _setupRequest;
        private AuthenticationRuntimeSecretWriteRequest? _authenticationRequest;

        private WriterFixture(WebApplication server, string key)
        {
            _server = server;
            Definition = SecretDefinitionRegistry.GetRequired(key);
            Binding = new SecretBinding
            {
                Id = Guid.CreateVersion7(), SettingKey = key, Scope = SecretScope.Instance,
                SourceType = SecretSourceType.Infisical, InfisicalEnvironment = "test",
                InfisicalPath = Definition.DefaultInfisicalPath, InfisicalKey = Definition.DefaultInfisicalKey
            };
            Bindings.Rows.Add(Binding);
        }

        internal SecretDefinition Definition { get; }
        internal SecretBinding Binding { get; }
        internal MemoryBindings Bindings { get; } = new();
        internal RecordingResolver Resolver { get; } = new();
        internal SecretAuthorityAcquisition Owner { get; private set; } = null!;
        internal TaskCompletionSource PatchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PatchReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Patches => Volatile.Read(ref _patches);
        internal int Logins => Volatile.Read(ref _logins);
        internal int UnexpectedRequests => Volatile.Read(ref _unexpectedRequests);
        internal int PatchStatus { get; set; } = StatusCodes.Status200OK;
        internal bool LoseResponse { get; set; }
        internal bool MatchedTargetAndValue { get; private set; }
        internal AuthenticationRuntimeSecretWriteResult? AuthenticationResult { get; private set; }
        internal SetupSecretBindingWriteOutcome? SetupResult { get; private set; }
        internal SecretMutationAttempt Attempt => _setupRequest?.Attempt ?? _authenticationRequest!.Attempt;

        internal static async Task<WriterFixture> OpenAsync(
            string key, SecretConsumerRole consumer = SecretConsumerRole.Combined,
            SecretAuthorityKind provider = SecretAuthorityKind.Infisical)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var fixture = new WriterFixture(builder.Build(), key);
            fixture._server.Run(fixture.HandleAsync);
            try
            {
                await fixture._server.StartAsync();
                if (provider != SecretAuthorityKind.Infisical)
                {
                    fixture.Owner = SecretAuthorityAcquisition.OpenLocal(
                        new(provider, "Testing", consumer), new ConfigurationBuilder().Build());
                }
                else
                {
                    using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
                    var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
                        new(provider, "Testing", consumer),
                        [new(SecretAcquisitionPolicy.Roots(consumer)[0], "ADMITTED", "Admitted")],
                        new(new Uri(fixture._server.Urls.Single()), Guid.NewGuid().ToString("N"), "test",
                            Guid.NewGuid().ToString("N"), InfisicalTokenLifetime.Finite, null,
                            TimeSpan.FromSeconds(30), TestTimeout),
                        bootstrap, new SecretAuthorityLoginAttempt());
                    if (!opened.IsSuccess)
                        throw new InvalidOperationException("native_writer_test_authority_unavailable");
                    fixture.Owner = opened.Value;
                }
                fixture.ReplaceRequest(fixture._credential);
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal void ReplaceRequest(ReadOnlyMemory<byte> credential)
        {
            if (Definition.Key is SecretDefinitionRegistry.Keys.SetupSigning
                or SecretDefinitionRegistry.Keys.SetupEncryption)
            {
                _setupRequest = new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
                    Guid.CreateVersion7(), Binding.Id, Definition.Key, credential);
            }
            else
            {
                InfisicalAuthorityOptions? options = Owner.InfisicalOptions;
                _authenticationRequest = new(new(
                    Definition.Key == SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
                        ? AuthenticationRuntimeSecretTarget.GoogleClientSecret
                        : AuthenticationRuntimeSecretTarget.KeycloakBffClientSecret,
                    Binding.Id, SecretSourceType.Infisical, "test",
                    Definition.DefaultInfisicalPath, Definition.DefaultInfisicalKey,
                    options?.Origin.AbsoluteUri ?? string.Empty, options?.ProjectId ?? string.Empty), credential);
            }
        }

        internal void ChangeSelection(string field)
        {
            AuthenticationRuntimeWriteSelection original = _authenticationRequest!.Selection;
            AuthenticationRuntimeWriteSelection changed = field switch
            {
                "binding" => original with { BindingId = Guid.CreateVersion7() },
                "source" => original with { Source = SecretSourceType.EnvironmentVariable },
                "environment" => original with { Environment = "other" },
                "path" => original with { Path = "/other" },
                "key" => original with { Key = "OTHER" },
                "origin" => original with { AuthorityUrl = new Uri(new Uri(original.AuthorityUrl), "/other").AbsoluteUri },
                "project" => original with { ProjectId = Guid.NewGuid().ToString("N") },
                _ => original with { Target = AuthenticationRuntimeSecretTarget.KeycloakBffClientSecret }
            };
            _authenticationRequest = new(changed, _credential);
        }

        internal async Task<bool> WriteAsync(CancellationToken cancellationToken = default)
        {
            if (_setupRequest is not null)
            {
                SetupResult = await new SetupSecretBindingWriter(Bindings, Owner, Resolver)
                    .WriteAsync(_setupRequest, cancellationToken);
                return SetupResult == SetupSecretBindingWriteOutcome.Ready;
            }
            AuthenticationResult = await new AuthenticationRuntimeSecretWriter(Bindings, Owner, Resolver)
                .WriteAsync(_authenticationRequest!, cancellationToken);
            return AuthenticationResult.Outcome == AuthenticationRuntimeWriteOutcome.Written;
        }

        private async Task HandleAsync(HttpContext context)
        {
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path == "/api/v1/auth/universal-auth/login")
            {
                Interlocked.Increment(ref _logins);
                await context.Response.WriteAsJsonAsync(new
                {
                    accessToken = _token, accessTokenMaxTTL = 3600, expiresIn = 600, tokenType = "Bearer"
                }, context.RequestAborted);
                return;
            }
            if (!HttpMethods.IsPatch(context.Request.Method)
                || context.Request.Path != "/api/v4/secrets/" + Definition.DefaultInfisicalKey)
            {
                Interlocked.Increment(ref _unexpectedRequests);
                context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                return;
            }
            Interlocked.Increment(ref _patches);
            using var body = await JsonDocument.ParseAsync(context.Request.Body,
                cancellationToken: context.RequestAborted);
            JsonElement root = body.RootElement;
            MatchedTargetAndValue = root.GetProperty("projectId").GetString() == Owner.InfisicalOptions!.ProjectId
                && root.GetProperty("environment").GetString() == "test"
                && root.GetProperty("secretPath").GetString() == Definition.DefaultInfisicalPath
                && root.GetProperty("secretValue").GetString() == Encoding.UTF8.GetString(_credential)
                && context.Request.Headers.Authorization == "Bearer " + _token;
            PatchEntered.TrySetResult();
            await PatchReleased.Task.WaitAsync(context.RequestAborted);
            if (LoseResponse)
            {
                context.Abort();
                return;
            }
            context.Response.StatusCode = PatchStatus;
            if (PatchStatus != StatusCodes.Status200OK)
                return;
            await context.Response.WriteAsJsonAsync(new
            {
                secret = new
                {
                    id = _resourceId, workspace = Owner.InfisicalOptions!.ProjectId,
                    environment = "test", secretPath = Definition.DefaultInfisicalPath,
                    secretKey = Definition.DefaultInfisicalKey, type = "shared", version = 2,
                    secretValue = root.GetProperty("secretValue").GetString(), secretValueHidden = false
                }
            }, context.RequestAborted);
        }

        public async ValueTask DisposeAsync()
        {
            PatchReleased.TrySetResult();
            if (Owner is not null)
                await Owner.DisposeAsync();
            CryptographicOperations.ZeroMemory(_credential);
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
    }

    private sealed class RecordingResolver : ISecretResolver
    {
        internal string? InvalidatedKey { get; private set; }
        internal SecretScope? InvalidatedScope { get; private set; }
        internal Guid? InvalidatedScopeId { get; private set; }
        internal bool InvalidationWasCancelable { get; private set; }
        internal bool FailInvalidation { get; set; }
        internal Action? OnInvalidation { get; set; }

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default)
        {
            InvalidatedKey = settingKey;
            InvalidatedScope = scope;
            InvalidatedScopeId = scopeId;
            InvalidationWasCancelable = cancellationToken.CanBeCanceled;
            OnInvalidation?.Invoke();
            if (FailInvalidation)
                throw new InvalidOperationException("native_writer_test_cache_unavailable");
            return Task.CompletedTask;
        }

        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("unexpected_read");
        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope,
            Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("unexpected_read");
        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("unexpected_read");
    }

    private sealed class MemoryBindings : ISecretBindingRepository
    {
        internal List<SecretBinding> Rows { get; } = [];
        internal void Clear() => Rows.Clear();
        public Task<SecretBinding?> GetById(Guid id) => Task.FromResult(Rows.SingleOrDefault(row => row.Id == id));
        public Task<IReadOnlyList<SecretBinding>> GetAll() =>
            Task.FromResult<IReadOnlyList<SecretBinding>>(Rows.ToArray());
        public Task<(IReadOnlyList<SecretBinding> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) =>
            Task.FromResult(((IReadOnlyList<SecretBinding>)Rows.Skip((pageNumber - 1) * pageSize)
                .Take(pageSize).ToArray(), Rows.Count));
        public Task<bool> Exists(Guid id) => Task.FromResult(Rows.Any(row => row.Id == id));
        public Task<SecretBinding> Create(SecretBinding entity)
        {
            Rows.Add(entity);
            return Task.FromResult(entity);
        }
        public Task Update(SecretBinding entity)
        {
            Rows.RemoveAll(row => row.Id == entity.Id);
            Rows.Add(entity);
            return Task.CompletedTask;
        }
        public Task Delete(SecretBinding entity)
        {
            Rows.Remove(entity);
            return Task.CompletedTask;
        }
        public Task<SecretBinding?> GetByKeyAndScopeAsync(string settingKey, SecretScope scope,
            Guid? scopeId, CancellationToken cancellationToken = default) =>
            GetByKeyScopeAndQualifierAsync(settingKey, scope, scopeId, string.Empty, cancellationToken);
        public Task<SecretBinding?> GetByKeyScopeAndQualifierAsync(string settingKey, SecretScope scope,
            Guid? scopeId, string qualifier, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Rows.SingleOrDefault(row => row.SettingKey == settingKey
                && row.Scope == scope && row.ScopeId == scopeId && row.Qualifier == qualifier));
        }
        public Task<IReadOnlyList<SecretBinding>> GetByScopeAsync(SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretBinding>>(Rows.Where(row => row.Scope == scope
                && row.ScopeId == scopeId).ToArray());
        public Task<SecretBinding?> GetByTenantAndIdAsync(Guid tenantId, Guid bindingId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.Id == bindingId
                && row.Scope == SecretScope.Tenant && row.ScopeId == tenantId));
        public Task<IReadOnlyList<SecretBinding>> GetAllForKeyAsync(string settingKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretBinding>>(Rows.Where(row => row.SettingKey == settingKey).ToArray());
        public async Task<bool> ExistsForScopeAsync(string settingKey, SecretScope scope, Guid? scopeId,
            CancellationToken cancellationToken = default) =>
            await GetByKeyAndScopeAsync(settingKey, scope, scopeId, cancellationToken) is not null;
        public async Task<bool> ExistsForScopeAndQualifierAsync(string settingKey, SecretScope scope,
            Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            await GetByKeyScopeAndQualifierAsync(settingKey, scope, scopeId, qualifier, cancellationToken) is not null;
    }
}
