using Explore.AppHost;
using Event.Testing.DataProtection;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.ApplicationModel;

namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
public sealed class AppHostOwnershipTests
{
    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task RealBuilderProjectionRejectsAmbientProviderAuthority(string topology)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var untrusted = new Dictionary<string, string?>
        {
            ["PAYMENTS_STRIPE_MODE"] = "Live", ["STRIPE_PLATFORM_SECRET_KEY"] = canary,
            ["TestingEnvironment:STRIPE_PLATFORM_SECRET_KEY"] = canary,
            ["Authentication:Local:Jwt:SigningKey"] = canary,
            ["Storage:Local:RootPath"] = "foreign-operator-storage"
        };
        var completion = await Assert.That(async () => await OwnedAppHostFixture.CreateAsync(topology,
            false, "SingleTenant", untrusted, async builder =>
            {
                foreach (var resource in builder.Resources.OfType<ProjectResource>())
                {
                    var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
                    foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
                        await callback.Callback(context);
                    await Assert.That(context.EnvironmentVariables.Values.OfType<string>()
                        .Any(value => value == canary || value == "foreign-operator-storage")).IsFalse();
                    if (resource.Name is "explore-api" or "event-standalone")
                        await Assert.That(context.EnvironmentVariables["Payments__Stripe__Mode"] is "Test").IsTrue();
                }
                throw new InvalidOperationException("assurance_projection_complete");
            }, deadline.Token)).Throws<InvalidOperationException>();
        await Assert.That(completion!.Message).IsEqualTo("assurance_projection_complete");
    }

    [Test]
    [Arguments("API")]
    [Arguments("BFF")]
    [Arguments("COMBINED")]
    public async Task ConcurrentOwnersRejectEachOthersCookiesAndTokens(string partition)
    {
        await using var first = new OwnedAppHostFixture("Split");
        await using var second = new OwnedAppHostFixture("Split");
        using var firstProvider = ExternalDataProtectionProvider.Create(Input(first, partition), partition.ToLowerInvariant(), TimeProvider.System);
        using var secondProvider = ExternalDataProtectionProvider.Create(Input(second, partition), partition.ToLowerInvariant(), TimeProvider.System);
        var firstProtector = firstProvider.CreateProtector("assurance-cookie");
        var secondProtector = secondProvider.CreateProtector("assurance-cookie");
        string protectedValue = firstProtector.Protect("owned-session");
        await Assert.That(() => secondProtector.Unprotect(protectedValue)).Throws<CryptographicException>();
        await Assert.That(firstProtector.Unprotect(protectedValue)).IsEqualTo("owned-session");

        var tokens = new JwtSecurityTokenHandler();
        string token = tokens.CreateEncodedJwt(new SecurityTokenDescriptor
        {
            Issuer = "assurance", Audience = "assurance", Expires = DateTime.UtcNow.AddMinutes(5),
            Subject = new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())]),
            SigningCredentials = new(new SymmetricSecurityKey(Key(first)), SecurityAlgorithms.HmacSha256)
        });
        var validation = new TokenValidationParameters
        {
            ValidIssuer = "assurance", ValidAudience = "assurance", ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Key(second)), ClockSkew = TimeSpan.Zero
        };
        await Assert.That(() => tokens.ValidateToken(token, validation, out _)).Throws<SecurityTokenException>();
        validation.IssuerSigningKey = new SymmetricSecurityKey(Key(first));
        await Assert.That(tokens.ValidateToken(token, validation, out _).Identity!.IsAuthenticated).IsTrue();
    }

    [Test]
    public async Task EffectiveProfileCannotChangeAfterOwnershipAdmission()
    {
        using var root = new OwnedRoot();
        var values = root.Configuration();
        _ = TestingResourceIsolation.Admit(Build(values));
        var effective = new ConfigurationBuilder().AddInMemoryCollection(values)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ERASURE_DATABASE_TOPOLOGY"] = "ExternalDatabase" })
            .Build();
        await Assert.That(() => TestingResourceIsolation.ValidateProfile(effective)).Throws<InvalidOperationException>();
    }

    private static byte[] Key(OwnedAppHostFixture owner) =>
        Encoding.UTF8.GetBytes(owner.EnvironmentAuthority["AUTHENTICATION_LOCAL_JWT_KEY"]!);

    private static ExternalDataProtectionInput Input(OwnedAppHostFixture owner, string partition) => new(
        owner.EnvironmentAuthority[$"EVENT_DP_{partition}_RING"]!, owner.EnvironmentAuthority[$"EVENT_DP_{partition}_ADMISSION"]!,
        owner.EnvironmentAuthority[$"EVENT_DP_{partition}_ADMISSION_SHA256"]!, 1);

    [Test]
    public async Task OwnedConfigurationRejectsMissingAuthority()
    {
        using var root = new OwnedRoot();
        using var configuration = new ConfigurationManager();
        string[] arguments = root.Configuration().Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        await Assert.That(() => TestingResourceIsolation.ConfigureOwnedInputs(configuration, arguments))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OwnedConfigurationDiscardsAmbientOperatorInputs()
    {
        using var root = new OwnedRoot();
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string ownedAuthority = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PAYMENTS_STRIPE_MODE"] = "Live", ["STRIPE_PLATFORM_SECRET_KEY"] = canary,
            ["Storage:Local:RootPath"] = root.Path + "-foreign",
            ["Authentication:Local:Jwt:SigningKey"] = canary,
            ["TestingEnvironment:STRIPE_PLATFORM_SECRET_KEY"] = canary
        });
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TestingEnvironment:AUTHENTICATION_LOCAL_JWT_KEY"] = ownedAuthority,
            ["TestingEnvironment:ISLAMU_TEST_RUN_ID"] = root.Id
        });
        string[] arguments = root.Configuration().Select(pair => $"{pair.Key}={pair.Value}").ToArray();
        TestingResourceIsolation.ConfigureOwnedInputs(configuration, arguments);
        await Assert.That(configuration.AsEnumerable().Any(pair => pair.Value == canary)).IsFalse();
        await Assert.That(configuration["PAYMENTS_STRIPE_MODE"]).IsNull();
        await Assert.That(configuration["Storage:Local:RootPath"]).IsNull();
        await Assert.That(configuration["TestingEnvironment:AUTHENTICATION_LOCAL_JWT_KEY"] == ownedAuthority).IsTrue();
        TestingResourceIsolation.ValidateProfile(configuration);
    }

    [Test]
    [Arguments("changed-marker")]
    [Arguments("symbolic-marker")]
    [Arguments("symbolic-root")]
    public async Task CleanupRefusesChangedOwnershipAndRetainsTheDirectory(string tampering)
    {
        var owner = new OwnedAppHostFixture("Split");
        string marker = Path.Combine(owner.Root, TestingResourceIsolation.MarkerName);
        string movedRoot = owner.Root + "-retained";
        string externalMarker = owner.Root + "-marker";
        try
        {
            if (tampering == "changed-marker")
                File.WriteAllText(marker, Guid.NewGuid().ToString("N"));
            else if (tampering == "symbolic-marker")
            {
                File.WriteAllText(externalMarker, owner.RunId);
                File.Delete(marker);
                File.CreateSymbolicLink(marker, externalMarker);
            }
            else
            {
                Directory.Move(owner.Root, movedRoot);
                Directory.CreateSymbolicLink(owner.Root, movedRoot);
            }

            await Assert.That(async () => await owner.DisposeAsync()).Throws<InvalidOperationException>();
            await Assert.That(Directory.Exists(owner.Root)).IsTrue();
            if (tampering == "symbolic-root")
                await Assert.That(File.Exists(Path.Combine(movedRoot, TestingResourceIsolation.MarkerName))).IsTrue();
        }
        finally
        {
            try { await using (owner) { } }
            catch (InvalidOperationException) { }
            if (new DirectoryInfo(owner.Root).LinkTarget is not null)
                Directory.Delete(owner.Root);
            else if (Directory.Exists(owner.Root))
                Directory.Delete(owner.Root, recursive: true);
            if (Directory.Exists(movedRoot))
                Directory.Delete(movedRoot, recursive: true);
            File.Delete(externalMarker);
        }
    }

    [Test]
    public async Task ConcurrentDisposalSharesOneOwnerCleanup()
    {
        var owner = new OwnedAppHostFixture("Split");
        await Task.WhenAll(owner.DisposeAsync().AsTask(), owner.DisposeAsync().AsTask());
        await Assert.That(Directory.Exists(owner.Root)).IsFalse();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"islamu.assurance.owner\":\"foreign\",\"islamu.assurance.resource\":\"postgres\"}")]
    [Arguments("{\"islamu.assurance.owner\":\"owner\",\"islamu.assurance.resource\":\"unrelated\"}")]
    public async Task ForeignContainerCannotBorrowAVolumeOwnersCleanup(string labels)
    {
        await Assert.That(() => OwnedAppHostFixture.RequireOwnedContainer("owner",
            new HashSet<string>(StringComparer.Ordinal) { "postgres" }, labels)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ASecondHostCannotClaimAnAdmittedRoot()
    {
        using var root = new OwnedRoot();
        var admitted = TestingResourceIsolation.Admit(Build(root.Configuration()))!;
        using var claim = admitted.Claim();
        await Assert.That(() => admitted.Claim()).Throws<IOException>();
    }

    [Test]
    public async Task OrdinaryInvocationDoesNotRequireOwnership()
    {
        await Assert.That(TestingResourceIsolation.Admit(new ConfigurationBuilder().Build())).IsNull();
    }

    [Test]
    [Arguments("SECRET_PROVIDER", "UserSecrets")]
    [Arguments("SECRET_PROVIDER", "")]
    [Arguments("DOTNET_ENVIRONMENT", "Production")]
    [Arguments("DOTNET_ENVIRONMENT", "Testing")]
    [Arguments("ISLAMU_ASPIRE_MODE", "local-agent")]
    [Arguments("ISLAMU_ASPIRE_MODE", "local-full")]
    [Arguments("Hosting:Topology", "Combined")]
    [Arguments("ISLAMU_TEST_RUN_ID", "../foreign")]
    [Arguments("ISLAMU_TEST_ROOT", "/")]
    [Arguments("ISLAMU_TEST_ROOT", "relative")]
    public async Task UnsafeAdmissionFailsBeforeStartup(string key, string value)
    {
        using var root = new OwnedRoot();
        var values = root.Configuration();
        values[key] = value;
        await Assert.That(() => TestingResourceIsolation.Admit(Build(values)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ForeignMarkerCannotAuthorizeAnExistingDirectory()
    {
        using var root = new OwnedRoot();
        File.WriteAllText(Path.Combine(root.Path, TestingResourceIsolation.MarkerName), Guid.NewGuid().ToString("N"));
        await Assert.That(() => TestingResourceIsolation.Admit(Build(root.Configuration())))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SymbolicRootCannotRedirectOwnership()
    {
        using var root = new OwnedRoot();
        string link = root.Path + "-link";
        Directory.CreateSymbolicLink(link, System.IO.Path.GetDirectoryName(root.Path)!);
        try
        {
            var values = root.Configuration();
            values["ISLAMU_TEST_ROOT"] = System.IO.Path.Combine(link, root.Id);
            await Assert.That(() => TestingResourceIsolation.Admit(Build(values)))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Test]
    public async Task AdmittedOwnerRetainsItsExactRoot()
    {
        using var root = new OwnedRoot();
        var admitted = TestingResourceIsolation.Admit(Build(root.Configuration()));
        await Assert.That(admitted).IsNotNull();
        await Assert.That(admitted!.Root).IsEqualTo(root.Path);
    }

    [Test]
    public async Task DuplicateExecutionObservationCannotCertifyCompletion()
    {
        var observer = new TestingApplicationExecution();
        observer.Observe(Task.CompletedTask);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.That(() => observer.Observe(running.Task)).Throws<InvalidOperationException>();
        await Assert.That(observer.Completion.IsCompletedSuccessfully).IsFalse();
        await Assert.That(observer.IsSettled).IsFalse();
        await Assert.That(async () => await observer.Completion).Throws<InvalidOperationException>();
        running.SetResult();
    }

    [Test]
    public async Task CancelledObservationWaitCannotCertifyUnobservedExecution()
    {
        var observer = new TestingApplicationExecution();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.That(async () => await observer.Completion.WaitAsync(cancelled.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(observer.Completion.IsCompleted).IsFalse();
        await Assert.That(observer.IsSettled).IsFalse();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.Observe(running.Task);
        await Assert.That(observer.Completion.IsCompleted).IsFalse();
        running.SetResult();
        await observer.Completion;
        await Assert.That(observer.IsSettled).IsTrue();
    }

    [Test]
    public async Task FaultedCancellationObservationRetainsItsFailure()
    {
        var observer = new TestingApplicationExecution();
        var failure = new OperationCanceledException("assurance_faulted_cancellation");
        observer.Observe(Task.FromException(failure));
        var observed = await Assert.That(async () => await observer.WaitForCompletionAsync(TimeSpan.FromSeconds(1)))
            .Throws<OperationCanceledException>();
        await Assert.That(ReferenceEquals(observed, failure)).IsTrue();
        await Assert.That(observer.IsSettled).IsTrue();
    }

    [Test]
    public async Task CancelledExecutionObservationCompletesCleanupWait()
    {
        var observer = new TestingApplicationExecution();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        observer.Observe(Task.FromCanceled(cancelled.Token));
        await observer.WaitForCompletionAsync(TimeSpan.FromSeconds(1));
        await Assert.That(observer.IsSettled).IsTrue();
    }

    [Test]
    [Arguments("Unsupported")]
    [Arguments("99")]
    public async Task UnknownOwnedProfileCannotConstructResources(string profile)
    {
        using var root = new OwnedRoot();
        var values = root.Configuration();
        values["ISLAMU_TEST_PROFILE"] = profile;
        await Assert.That(() => TestingResourceIsolation.Admit(Build(values))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExplicitProfileCannotRunWithoutOwnership()
    {
        var values = new Dictionary<string, string?> { ["ISLAMU_TEST_PROFILE"] = "ExternalIdentitySharedBff" };
        await Assert.That(() => TestingResourceIsolation.Admit(Build(values))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExternalIdentityProfileAdmitsItsDerivedStore()
    {
        using var root = new OwnedRoot();
        var values = ExternalIdentityProfile(root);
        values["IdentityDatabase:Topology"] = "External";
        values["IdentityDatabase:Provider"] = "Sqlite";
        values["IdentityDatabase:Name"] = values["IDENTITY_DATABASE_NAME"];
        var admitted = TestingResourceIsolation.Admit(Build(values));
        await Assert.That(admitted).IsNotNull();
        await Assert.That(admitted!.Profile).IsEqualTo(OwnedAppHostProfile.ExternalIdentitySharedBff);
    }

    [Test]
    [Arguments("IDENTITY_DATABASE_NAME", "../foreign.sqlite")]
    [Arguments("IdentityDatabase:Name", "foreign.sqlite")]
    [Arguments("IDENTITY_DATABASE_PROVIDER", "PostgreSql")]
    [Arguments("IdentityDatabase:Provider", "PostgreSql")]
    [Arguments("IdentityDatabase:Topology", "CoLocated")]
    [Arguments("IdentityDatabase:Host", "foreign-host")]
    [Arguments("IDENTITY_DATABASE_RUNTIME_USERNAME", "foreign-owner")]
    public async Task ExternalIdentityProfileRejectsForeignCoordinates(string key, string value)
    {
        using var root = new OwnedRoot();
        var values = ExternalIdentityProfile(root);
        values[key] = value;
        await Assert.That(() => TestingResourceIsolation.Admit(Build(values))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExternalIdentityProfileRejectsCanonicalCredentialAuthority()
    {
        using var root = new OwnedRoot();
        var values = ExternalIdentityProfile(root);
        values["IdentityDatabase:Runtime:Password"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Assert.That(() => TestingResourceIsolation.Admit(Build(values))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExternalIdentityProfileRejectsSymbolicStore()
    {
        using var root = new OwnedRoot();
        string store = Path.Combine(root.Path, "identity.sqlite");
        File.CreateSymbolicLink(store, Path.Combine(root.Path, "foreign.sqlite"));
        await Assert.That(() => TestingResourceIsolation.ValidateProfile(Build(ExternalIdentityProfile(root))))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RealExternalIdentityProfileProjectsTwoCanonicalBffOwners()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var failure = await Assert.That(async () => await OwnedAppHostFixture.CreateAsync("Split", false,
            "MultiTenant", null, async builder =>
            {
                var resources = builder.Resources.OfType<ProjectResource>().ToArray();
                _ = resources.Single(resource => resource.Name == "explore-blazor-peer");
                string root = builder.Configuration["ISLAMU_TEST_ROOT"]!;
                foreach (var resource in resources)
                {
                    var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
                    foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
                        await callback.Callback(context);
                    if (resource.Name is "explore-api" or "event-migrationservice")
                    {
                        await Assert.That(context.EnvironmentVariables["IDENTITY_DATABASE_TOPOLOGY"] is "External").IsTrue();
                        await Assert.That(context.EnvironmentVariables["IDENTITY_DATABASE_PROVIDER"] is "Sqlite").IsTrue();
                        await Assert.That(context.EnvironmentVariables["IDENTITY_DATABASE_NAME"] is string path
                            && path == Path.Combine(root, "identity.sqlite")).IsTrue();
                    }
                    if (resource.Name is "explore-blazor" or "explore-blazor-peer")
                    {
                        await Assert.That(context.EnvironmentVariables.Keys.Any(key =>
                            key.StartsWith("EVENT_DP_API_", StringComparison.Ordinal)
                            || key.StartsWith("EVENT_DP_COMBINED_", StringComparison.Ordinal))).IsFalse();
                        await Assert.That(context.EnvironmentVariables.Keys.Any(key =>
                            key.StartsWith("IDENTITY_DATABASE_", StringComparison.OrdinalIgnoreCase)
                            || key.Replace("__", ":", StringComparison.Ordinal)
                                .StartsWith("IdentityDatabase:", StringComparison.OrdinalIgnoreCase))).IsFalse();
                        await Assert.That(resource.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.Name == "https")).IsTrue();
                    }
                }
                throw new InvalidOperationException("assurance_external_profile_projection_complete");
            }, deadline.Token, OwnedAppHostProfile.ExternalIdentitySharedBff)).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("assurance_external_profile_projection_complete");
    }

    [Test]
    [Arguments("Standalone", OwnedAppHostProfile.ExternalIdentitySharedBff, OwnedBffPeerProtection.Shared)]
    [Arguments("Split", (OwnedAppHostProfile)99, OwnedBffPeerProtection.Shared)]
    [Arguments("Split", OwnedAppHostProfile.ExternalIdentitySharedBff, (OwnedBffPeerProtection)99)]
    [Arguments("Split", OwnedAppHostProfile.Standard, OwnedBffPeerProtection.WrongPartition)]
    public async Task InvalidProfileSelectionFailsBeforeBuilder(string topology, OwnedAppHostProfile profile,
        OwnedBffPeerProtection protection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        bool builderReached = false;
        await Assert.That(async () => await OwnedAppHostFixture.CreateAsync(topology, false,
            "SingleTenant", null, _ =>
            {
                builderReached = true;
                throw new InvalidOperationException("assurance_unexpected_profile_builder");
            }, deadline.Token, profile, protection)).Throws<InvalidOperationException>();
        await Assert.That(builderReached).IsFalse();
    }

    [Test]
    public async Task RevokedPeerAuthorityRetainsItsOriginalKeyAndRejectsAdmission()
    {
        var input = new ExternalDataProtectionTestAuthority("bff").Input;
        using var baseline = ExternalDataProtectionProvider.Create(input, "bff", TimeProvider.System);
        var protector = baseline.CreateProtector("owned-peer-session");
        string cookie = protector.Protect("owned-peer-session");
        var revoked = ExternalDataProtectionTestAuthority.RevokeActiveKey(input);
        var refusal = await Assert.That(() =>
        {
            using var refused = ExternalDataProtectionProvider.Create(revoked, "bff", TimeProvider.System);
        }).Throws<InvalidOperationException>();
        await Assert.That(refusal!.Message).IsEqualTo("data_protection_admission_rejected");
        var originalAdmission = System.Text.Json.JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
        var revokedAdmission = System.Text.Json.JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(revoked.Admission)!;
        await Assert.That(revokedAdmission.ActiveKeyId).IsEqualTo(originalAdmission.ActiveKeyId);
        await Assert.That(revokedAdmission.Keys.Single(key => key.Id == originalAdmission.ActiveKeyId).Revoked).IsTrue();
        await Assert.That(protector.Unprotect(cookie)).IsEqualTo("owned-peer-session");
    }

    [Test]
    [Arguments(OwnedBffPeerProtection.Shared)]
    [Arguments(OwnedBffPeerProtection.WrongPartition)]
    [Arguments(OwnedBffPeerProtection.GenerationBelowFloor)]
    [Arguments(OwnedBffPeerProtection.ExpiredAdmission)]
    [Arguments(OwnedBffPeerProtection.RevokedActiveKey)]
    public async Task RealPeerProjectionAcceptsOnlySharedBffAuthority(OwnedBffPeerProtection protection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var failure = await Assert.That(async () => await OwnedAppHostFixture.CreateAsync("Split", false,
            "SingleTenant", null, async builder =>
            {
                var contexts = new Dictionary<string, EnvironmentCallbackContext>(StringComparer.Ordinal);
                foreach (var resource in builder.Resources.OfType<ProjectResource>()
                    .Where(resource => resource.Name is "explore-api" or "explore-blazor" or "explore-blazor-peer"))
                {
                    var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
                    foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
                        await callback.Callback(context);
                    contexts.Add(resource.Name, context);
                }
                var bff = ProjectedProtection(contexts["explore-blazor"], "BFF");
                var peer = ProjectedProtection(contexts["explore-blazor-peer"], "BFF");
                var api = ProjectedProtection(contexts["explore-api"], "API");
                using var bffProvider = ExternalDataProtectionProvider.Create(bff, "bff", TimeProvider.System);
                using var apiProvider = ExternalDataProtectionProvider.Create(api, "api", TimeProvider.System);
                var bffProtector = bffProvider.CreateProtector("owned-peer-session");
                string cookie = bffProtector.Protect("owned-peer-session");
                await Assert.That(bff.Ring == api.Ring).IsFalse();
                await Assert.That(() => bffProtector.Unprotect(apiProvider.CreateProtector("owned-peer-session")
                    .Protect("owned-peer-session"))).Throws<CryptographicException>();
                if (protection == OwnedBffPeerProtection.Shared)
                {
                    await Assert.That(peer.Ring == bff.Ring && peer.Admission == bff.Admission).IsTrue();
                    using var peerProvider = ExternalDataProtectionProvider.Create(peer, "bff", TimeProvider.System);
                    await Assert.That(peerProvider.CreateProtector("owned-peer-session").Unprotect(cookie)
                        == "owned-peer-session").IsTrue();
                }
                else
                {
                    var refusal = await Assert.That(() =>
                    {
                        using var refused = ExternalDataProtectionProvider.Create(peer, "bff", TimeProvider.System);
                    }).Throws<InvalidOperationException>();
                    await Assert.That(refusal!.Message).IsEqualTo("data_protection_admission_rejected");
                    if (protection == OwnedBffPeerProtection.WrongPartition)
                        await Assert.That(peer.Ring == api.Ring).IsTrue();
                    else
                    {
                        var original = System.Text.Json.JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(bff.Admission)!;
                        var candidate = System.Text.Json.JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(peer.Admission)!;
                        await Assert.That(candidate.ActiveKeyId == original.ActiveKeyId).IsTrue();
                    }
                }
                throw new InvalidOperationException("assurance_peer_projection_complete");
            }, deadline.Token, OwnedAppHostProfile.ExternalIdentitySharedBff, protection)).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("assurance_peer_projection_complete");
    }

    private static ExternalDataProtectionInput ProjectedProtection(EnvironmentCallbackContext context, string partition)
    {
        string prefix = $"EVENT_DP_{partition}_";
        return new((string)context.EnvironmentVariables[prefix + "RING"],
            (string)context.EnvironmentVariables[prefix + "ADMISSION"],
            (string)context.EnvironmentVariables[prefix + "ADMISSION_SHA256"],
            long.Parse((string)context.EnvironmentVariables[prefix + "MINIMUM_GENERATION"],
                System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Dictionary<string, string?> ExternalIdentityProfile(OwnedRoot root)
    {
        var values = root.Configuration();
        values["ISLAMU_TEST_PROFILE"] = "ExternalIdentitySharedBff";
        values["IDENTITY_DATABASE_TOPOLOGY"] = "External";
        values["IDENTITY_DATABASE_PROVIDER"] = "Sqlite";
        values["IDENTITY_DATABASE_NAME"] = Path.Combine(root.Path, "identity.sqlite");
        return values;
    }

    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class OwnedRoot : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Path { get; }

        public OwnedRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "islamu-assurance", Id);
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, TestingResourceIsolation.MarkerName), Id);
        }

        public Dictionary<string, string?> Configuration() => new()
        {
            ["ISLAMU_TEST_RUN_ID"] = Id,
            ["ISLAMU_TEST_ROOT"] = Path,
            ["SECRET_PROVIDER"] = "Environment",
            ["DOTNET_ENVIRONMENT"] = "Assurance",
            ["ISLAMU_ASPIRE_MODE"] = "local-default",
            ["Hosting:Topology"] = "Split"
        };

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
