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
using Aspire.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task SessionAuthorityIsStableWithinItsOwnerAndIsolatedFromOtherOwners()
    {
        await using var first = new OwnedAppHostFixture("Split");
        await using var second = new OwnedAppHostFixture("Split");
        await Assert.That(first.EnvironmentAuthority["BFF_SESSION_NAMESPACE"] == first.RunId).IsTrue();
        await Assert.That(second.EnvironmentAuthority["BFF_SESSION_NAMESPACE"] == second.RunId).IsTrue();
        await Assert.That(first.EnvironmentAuthority["BFF_SESSION_NAMESPACE"]
            == second.EnvironmentAuthority["BFF_SESSION_NAMESPACE"]).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(first.EnvironmentAuthority["BFF_SESSION_REDIS_PASSWORD"])).IsFalse();
        await Assert.That(first.EnvironmentAuthority["BFF_SESSION_REDIS_PASSWORD"]
            == second.EnvironmentAuthority["BFF_SESSION_REDIS_PASSWORD"]).IsFalse();
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
    [Arguments(OwnedBffPeerProtection.IndependentValidAuthority)]
    [Arguments(OwnedBffPeerProtection.WrongPartition)]
    [Arguments(OwnedBffPeerProtection.GenerationBelowFloor)]
    [Arguments(OwnedBffPeerProtection.ExpiredAdmission)]
    [Arguments(OwnedBffPeerProtection.RevokedActiveKey)]
    public async Task RealPeerProjectionDistinguishesSharedAndIndependentValidAuthorities(OwnedBffPeerProtection protection)
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
                else if (protection == OwnedBffPeerProtection.IndependentValidAuthority)
                {
                    await Assert.That(peer.Ring == bff.Ring || peer.Admission == bff.Admission).IsFalse();
                    using var peerProvider = ExternalDataProtectionProvider.Create(peer, "bff", TimeProvider.System);
                    var peerProtector = peerProvider.CreateProtector("owned-peer-session");
                    await Assert.That(() => peerProtector.Unprotect(cookie)).Throws<CryptographicException>();
                    string ownCookie = peerProtector.Protect("independent-peer-session");
                    await Assert.That(peerProtector.Unprotect(ownCookie) == "independent-peer-session").IsTrue();
                    await Assert.That(() => bffProtector.Unprotect(ownCookie)).Throws<CryptographicException>();
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
    [Test]
    public async Task SelectedManagedProjectsReceiveTheirExistingPrivateFixtureOwner()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var earlyExit = new InvalidOperationException("private_host_owner_projection_complete");
        try
        {
            await using var unexpected = await OwnedAppHostFixture.CreateAsync("Split",
            false, "SingleTenant", null, async builder =>
            {
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
                string? owner = builder.Configuration["ISLAMU_TEST_RUN_ID"];
                string? root = builder.Configuration["ISLAMU_TEST_ROOT"];
                await Assert.That(Guid.TryParseExact(owner, "N", out _)).IsTrue();
                await Assert.That(root is not null && Directory.Exists(root)).IsTrue();
                await Assert.That(File.GetUnixFileMode(root!)).IsEqualTo(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                await Assert.That(File.GetUnixFileMode(Path.Combine(root!, TestingResourceIsolation.MarkerName)))
                    .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var resources = builder.Resources.OfType<ProjectResource>().ToArray();
                await Assert.That(resources.Length > 0).IsTrue();
                foreach (var resource in resources)
                {
                    var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
                    foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
                        await callback.Callback(context);
                    await Assert.That(context.EnvironmentVariables.TryGetValue("ISLAMU_TEST_RUN_ID", out var actualOwner)
                        && actualOwner is string value && value == owner).IsTrue();
                    await Assert.That(context.EnvironmentVariables.TryGetValue("ISLAMU_TEST_ROOT", out var actualRoot)
                        && actualRoot is string path && path == root).IsTrue();
                }
                throw earlyExit;
            }, deadline.Token);
            throw new InvalidOperationException("private_host_owner_projection_callback_missing");
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, earlyExit)) { }
    }

    [Test]
    public async Task ActualHostRootHasOneLiveProcessDescriptorUntilStopped()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
        foreach (bool isWebHost in new[] { false, true })
        {
            using var owner = new PrivateHostRoot();
            string webRoot = Path.Combine(owner.Root, "actual-web-root");
            Directory.CreateDirectory(webRoot);
            await using var web = isWebHost ? Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(
                new Microsoft.AspNetCore.Builder.WebApplicationOptions
                {
                    EnvironmentName = "Assurance", ContentRootPath = owner.ContentRoot, WebRootPath = webRoot,
                    ApplicationName = typeof(Microsoft.Extensions.Hosting.Extensions).Assembly.GetName().Name
                }).Build() : null;
            var builder = owner.Builder();
            if (web is not null)
                builder.Services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(web.Environment);
            builder.AddServiceDefaults();
            using var host = builder.Build();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StartAsync(deadline.Token);
            string record = AssertSinglePrivateRecord(owner);
            using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(record)))
            {
                var value = document.RootElement;
                await Assert.That(value.GetProperty("Schema").GetInt32()).IsEqualTo(1);
                await Assert.That(value.GetProperty("RunId").GetString()).IsEqualTo(owner.Id);
                await Assert.That(value.GetProperty("Pid").GetInt32()).IsEqualTo(Environment.ProcessId);
                string stat = File.ReadAllText("/proc/self/stat");
                string start = stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19];
                await Assert.That(value.GetProperty("Start").GetUInt64().ToString(
                    System.Globalization.CultureInfo.InvariantCulture)).IsEqualTo(start);
                await Assert.That(value.GetProperty("ContentRoot").GetString()).IsEqualTo(owner.ContentRoot);
                await Assert.That(owner.ContentRoot == Directory.GetCurrentDirectory()).IsFalse();
                await Assert.That(value.TryGetProperty("ApplicationName", out var applicationName)
                    && applicationName.GetString() == builder.Environment.ApplicationName
                    && value.TryGetProperty("EntryAssembly", out var entry)
                    && entry.GetString() == System.Reflection.Assembly.GetEntryAssembly()?.Location
                    && !string.IsNullOrEmpty(entry.GetString())
                    && value.TryGetProperty("IsWebHost", out var capability) && capability.GetBoolean() == isWebHost
                    && value.TryGetProperty("WebRoot", out var actualWebRoot)
                    && (isWebHost ? actualWebRoot.GetString() == web!.Environment.WebRootPath
                        : actualWebRoot.ValueKind == System.Text.Json.JsonValueKind.Null)).IsTrue()
                    .Because("The private host record must carry actual entry and registered web-environment facts.");
                if (web is not null)
                    await Assert.That(!ReferenceEquals(builder.Environment, web.Environment)
                        && builder.Environment.ApplicationName != web.Environment.ApplicationName).IsTrue();
            }
            OpenHostDescriptor[] descriptors = OpenRecordDescriptors(record);
            await Assert.That(descriptors.Length).IsEqualTo(1);
            await host.StopAsync(deadline.Token);
            await Assert.That(OpenRecordDescriptors(record, descriptors[0]).Length).IsEqualTo(0);
            await host.StopAsync(deadline.Token);
            await Assert.That(OpenRecordDescriptors(record, descriptors[0]).Length).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("absent")]
    [Arguments("missing-root")]
    [Arguments("missing-owner")]
    [Arguments("wrong-environment")]
    [Arguments("wrong-marker")]
    [Arguments("linked-marker")]
    [Arguments("root-mode")]
    [Arguments("marker-mode")]
    [Arguments("outside-root")]
    public async Task OrdinaryHostIsUnchangedAndUnsafePrivateContextCannotStart(string variant)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
        using var owner = new PrivateHostRoot();
        if (variant == "wrong-environment")
        {
            foreach (string environment in new[] { "Testing", "Staging", "Development" })
            foreach (string context in new[] { "complete", "partial", "malformed" })
            {
                var ordinary = owner.Builder(environment);
                ordinary.Configuration["DOTNET_ENVIRONMENT"] = "Assurance";
                ordinary.Configuration["ASPNETCORE_ENVIRONMENT"] = "Assurance";
                if (context == "partial") ordinary.Configuration["ISLAMU_TEST_ROOT"] = null;
                if (context == "malformed")
                {
                    ordinary.Configuration["ISLAMU_TEST_ROOT"] = "not-an-absolute-private-root";
                    ordinary.Configuration["ISLAMU_TEST_RUN_ID"] = "not-an-owner";
                }
                ordinary.AddServiceDefaults();
                using var ordinaryHost = ordinary.Build();
                await Assert.That(ordinaryHost.Services.GetRequiredService<IHostEnvironment>().EnvironmentName)
                    .IsEqualTo(environment);
                using var ordinaryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await ordinaryHost.StartAsync(ordinaryDeadline.Token);
                await ordinaryHost.StopAsync(ordinaryDeadline.Token);
                await Assert.That(Directory.EnumerateFiles(owner.Root, "managed-host-root-*").Any()).IsFalse();
            }
            return;
        }
        var builder = owner.Builder();
        builder.Configuration["DOTNET_ENVIRONMENT"] = "Testing";
        string marker = Path.Combine(owner.Root, TestingResourceIsolation.MarkerName);
        switch (variant)
        {
            case "absent": builder.Configuration["ISLAMU_TEST_ROOT"] = null; builder.Configuration["ISLAMU_TEST_RUN_ID"] = null; break;
            case "missing-root": builder.Configuration["ISLAMU_TEST_ROOT"] = null; break;
            case "missing-owner": builder.Configuration["ISLAMU_TEST_RUN_ID"] = null; break;
            case "wrong-marker": File.WriteAllText(marker, new string('f', 32)); break;
            case "linked-marker":
                File.Move(marker, marker + ".retained");
                File.CreateSymbolicLink(marker, marker + ".retained");
                break;
            case "root-mode": File.SetUnixFileMode(owner.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.UserExecute | UnixFileMode.GroupRead); break;
            case "marker-mode": File.SetUnixFileMode(marker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead); break;
            case "outside-root": builder.Configuration["ISLAMU_TEST_ROOT"] = owner.ContentRoot; break;
        }
        builder.AddServiceDefaults();
        using var host = builder.Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.That(host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName).IsEqualTo("Assurance");
        if (variant == "absent")
        {
            await host.StartAsync(deadline.Token);
            await host.StopAsync(deadline.Token);
            await Assert.That(Directory.EnumerateFiles(owner.Root, "managed-host-root-*").Any()).IsFalse();
        }
        else
        {
            Exception? failure = null;
            try { await host.StartAsync(deadline.Token); }
            catch (Exception exception) { failure = exception; }
            await Assert.That(failure is InvalidOperationException).IsTrue();
            await Assert.That(failure!.Message).IsEqualTo("managed_host_root_observation_rejected");
            await Assert.That(failure.ToString().Contains(owner.Root, StringComparison.Ordinal)).IsFalse();
            await Assert.That(failure.ToString().Contains(owner.ContentRoot, StringComparison.Ordinal)).IsFalse();
            await Assert.That(failure.InnerException is null && failure.Data.Count == 0).IsTrue();
            await Assert.That(Directory.EnumerateFiles(owner.Root, "managed-host-root-*").Any()).IsFalse();
        }
    }

    [Test]
    public async Task IndependentHostStopFailureSurvivesWhileObservedHandleCloses()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
        using var owner = new PrivateHostRoot();
        var builder = owner.Builder();
        builder.AddServiceDefaults();
        var primary = new InvalidOperationException("independent_host_stop_failure");
        builder.Services.AddSingleton<IHostedService>(new FailingHostStop(primary));
        using var host = builder.Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(deadline.Token);
        string record = AssertSinglePrivateRecord(owner);
        OpenHostDescriptor[] descriptors = OpenRecordDescriptors(record);
        await Assert.That(descriptors.Length).IsEqualTo(1);
        Exception? failure = null;
        try { await host.StopAsync(deadline.Token); }
        catch (Exception exception) { failure = exception; }
        Exception[] failures = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions.ToArray()
            : failure is null ? [] : [failure];
        await Assert.That(failures.Any(exception => ReferenceEquals(exception, primary))).IsTrue();
        await Assert.That(OpenRecordDescriptors(record, descriptors[0]).Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("pull-policy")]
    [Arguments("mount-target")]
    public async Task FinalOwnedContainerModelRejectsPostConfigurationMutation(string variant)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        bool mutated = false;
        Exception? failure = null;
        try
        {
            await using var unexpected = await OwnedAppHostFixture.CreateAsync("Split", false,
                "SingleTenant", null, async builder =>
                {
                    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_model_requires_linux");
                    var owner = new TestingResourceIsolation(builder.Configuration["ISLAMU_TEST_RUN_ID"]!,
                        builder.Configuration["ISLAMU_TEST_ROOT"]!);
                    bool admitted = owner.TryCaptureConfiguredContainerModel(builder, out byte[] bytes);
                    await Assert.That(admitted).IsTrue();
                    await Assert.That(bytes.Length is > 0 and <= 262144).IsTrue();
                    using var document = System.Text.Json.JsonDocument.Parse(bytes);
                    await Assert.That(document.RootElement.GetProperty("Schema").GetInt32()).IsEqualTo(1);
                    await Assert.That(document.RootElement.EnumerateObject().Select(item => item.Name)
                        .Order(StringComparer.Ordinal).SequenceEqual(new[] { "Containers", "Projects", "RunId", "Schema" })).IsTrue()
                        .Because("The final typed resource model must include actual project declarations.");
                    await Assert.That(document.RootElement.GetProperty("RunId").GetString()).IsEqualTo(owner.RunId);
                    var projects = builder.Resources.OfType<ProjectResource>()
                        .OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
                    var declaredProjects = document.RootElement.GetProperty("Projects").EnumerateArray().ToArray();
                    await Assert.That(projects.Length > 0 && declaredProjects.Length == projects.Length).IsTrue();
                    for (int index = 0; index < projects.Length; index++)
                    {
                        string projectPath = projects[index].GetProjectMetadata().ProjectPath;
                        await Assert.That(declaredProjects[index].EnumerateObject().Select(item => item.Name)
                            .Order(StringComparer.Ordinal).SequenceEqual(new[] { "Name", "ProjectPath" })
                            && declaredProjects[index].GetProperty("Name").GetString() == projects[index].Name
                            && declaredProjects[index].GetProperty("ProjectPath").GetString() == projectPath
                            && Path.IsPathFullyQualified(projectPath) && File.Exists(projectPath)).IsTrue();
                    }
                    var removedProject = projects[0];
                    builder.Resources.Remove(removedProject);
                    try
                    {
                        await Assert.That(owner.TryCaptureConfiguredContainerModel(builder, out byte[] reduced)
                            && !reduced.AsSpan().SequenceEqual(bytes)).IsTrue();
                        using var reducedModel = System.Text.Json.JsonDocument.Parse(reduced);
                        var reducedProjects = reducedModel.RootElement.GetProperty("Projects").EnumerateArray().ToArray();
                        await Assert.That(reducedProjects.Length == projects.Length - 1
                            && reducedProjects.All(item => item.GetProperty("Name").GetString() != removedProject.Name)).IsTrue();
                    }
                    finally { builder.Resources.Add(removedProject); }
                    await Assert.That(owner.TryCaptureConfiguredContainerModel(builder, out byte[] restored)
                        && restored.AsSpan().SequenceEqual(bytes)).IsTrue();
                    var containers = builder.Resources.OfType<ContainerResource>().OrderBy(item => item.Name,
                        StringComparer.Ordinal).ToArray();
                    var rows = document.RootElement.GetProperty("Containers").EnumerateArray().ToArray();
                    await Assert.That(containers.Length > 0 && rows.Length == containers.Length).IsTrue();
                    for (int index = 0; index < containers.Length; index++)
                    {
                        var container = containers[index];
                        await Assert.That(container.TryGetContainerImageName(false, out string? image)).IsTrue();
                        var policies = container.Annotations.OfType<ContainerImagePullPolicyAnnotation>().ToArray();
                        await Assert.That(policies.Length == 1 && policies[0].ImagePullPolicy == ImagePullPolicy.Never).IsTrue();
                        await Assert.That(rows[index].GetProperty("Name").GetString()).IsEqualTo(container.Name);
                        await Assert.That(rows[index].GetProperty("Image").GetString()).IsEqualTo(image);
                        await Assert.That(rows[index].GetProperty("PullPolicy").GetString()).IsEqualTo("Never");
                        await Assert.That(rows[index].EnumerateObject().Select(item => item.Name)
                            .Order(StringComparer.Ordinal).SequenceEqual(new[] { "Image", "Mounts", "Name", "PullPolicy" })).IsTrue();
                        var expected = container.Annotations.OfType<ContainerMountAnnotation>()
                            .OrderBy(item => item.Target, StringComparer.Ordinal).ToArray();
                        var mounts = rows[index].GetProperty("Mounts").EnumerateArray().ToArray();
                        await Assert.That(mounts.Length).IsEqualTo(expected.Length);
                        for (int mount = 0; mount < expected.Length; mount++)
                        {
                            await Assert.That(mounts[mount].EnumerateObject().Select(item => item.Name)
                                .Order(StringComparer.Ordinal).SequenceEqual(new[] { "ReadOnly", "Source", "Target", "Type" })).IsTrue();
                            await Assert.That(mounts[mount].GetProperty("Type").GetString()).IsEqualTo(expected[mount].Type.ToString());
                            await Assert.That(mounts[mount].GetProperty("Source").GetString()).IsEqualTo(expected[mount].Source);
                            await Assert.That(mounts[mount].GetProperty("Target").GetString()).IsEqualTo(expected[mount].Target);
                            await Assert.That(mounts[mount].GetProperty("ReadOnly").GetBoolean()).IsEqualTo(expected[mount].IsReadOnly);
                        }
                    }
                    string published = Path.Combine(owner.Root, "container-model.json");
                    await Assert.That(File.Exists(published)).IsFalse();
                    await owner.PublishConfiguredContainerModelAsync(builder, deadline.Token);
                    string metadata = await ModelRecordMetadataAsync(published, deadline.Token);
                    string[] fields = metadata.Split('|');
                    await Assert.That(fields.Length == 6 && fields[0] == "regular file" && fields[1] == "600"
                        && fields[2] == "1" && fields[5] == bytes.Length.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)).IsTrue();
                    await Assert.That(new FileInfo(published).LinkTarget is null).IsTrue();
                    using (var record = new FileStream(published, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] readback = new byte[bytes.Length];
                        await record.ReadExactlyAsync(readback, deadline.Token);
                        await Assert.That(record.ReadByte() == -1 && readback.AsSpan().SequenceEqual(bytes)).IsTrue();
                    }
                    await Assert.That(await ModelRecordMetadataAsync(published, deadline.Token)).IsEqualTo(metadata);
                    File.Delete(published);
                    await Assert.That(File.Exists(published)).IsFalse();
                    if (variant == "pull-policy")
                        containers[0].Annotations.OfType<ContainerImagePullPolicyAnnotation>().Single()
                            .ImagePullPolicy = ImagePullPolicy.Always;
                    else
                    {
                        var container = containers.First(item => item.Annotations.OfType<ContainerMountAnnotation>()
                            .Any(mount => mount.Type == ContainerMountType.Volume));
                        var mount = container.Annotations.OfType<ContainerMountAnnotation>()
                            .First(item => item.Type == ContainerMountType.Volume);
                        container.Annotations.Add(new ContainerMountAnnotation(mount.Source + "-model-conflict",
                            mount.Target, mount.Type, mount.IsReadOnly));
                        await Assert.That(container.Annotations.OfType<ContainerMountAnnotation>()
                            .Count(item => item.Target == mount.Target) > 1).IsTrue();
                    }
                    mutated = true;
                }, deadline.Token);
        }
        catch (Exception exception) { failure = exception; }
        if (!mutated && failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        await Assert.That(mutated).IsTrue();
        await Assert.That(failure is InvalidOperationException
            && failure.Message == "test_resource_final_model_invalid").IsTrue();
    }

    private static async Task<string> ModelRecordMetadataAsync(string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("stat")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        process.StartInfo.Environment["LC_ALL"] = "C";
        foreach (string argument in new[] { "--format=%F|%a|%h|%d|%i|%s", "--", path })
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("model_metadata_unavailable");
        Task<string> output = BoundedModelMetadataAsync(process.StandardOutput, deadline.Token);
        Task<string> error = BoundedModelMetadataAsync(process.StandardError, deadline.Token);
        var failures = new List<Exception>();
        string? value = null;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            value = await output.WaitAsync(deadline.Token);
            string problem = await error.WaitAsync(deadline.Token);
            if (process.ExitCode != 0 || problem.Length != 0) throw new InvalidOperationException("model_metadata_unavailable");
        }
        catch (Exception failure) { failures.Add(failure); }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception failure) { failures.Add(failure); }
        try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
        catch (Exception failure) { failures.Add(failure); }
        try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
        catch (Exception failure) { failures.Add(failure); }
        bool outputCompleted = output.IsCompleted;
        bool errorCompleted = error.IsCompleted;
        if (outputCompleted)
        {
            try { await output; }
            catch (Exception failure)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
            }
        }
        if (errorCompleted)
        {
            try { await error; }
            catch (Exception failure)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
            }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
        return value!.TrimEnd('\n');
    }

    private static async Task<string> BoundedModelMetadataAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[513];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) return new string(buffer, 0, length);
            length += read;
        }
        throw new InvalidOperationException("model_metadata_unbounded");
    }

    private static string AssertSinglePrivateRecord(PrivateHostRoot owner)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
        string[] records = Directory.GetFiles(owner.Root, "managed-host-root-*.json");
        if (records.Length != 1 || File.GetUnixFileMode(records[0]) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            throw new InvalidOperationException("private_host_record_missing_or_unsafe");
        return records[0];
    }

    private sealed record OpenHostDescriptor(string Descriptor, string MountId, string Inode);

    private static OpenHostDescriptor[] OpenRecordDescriptors(string record, OpenHostDescriptor? original = null)
    {
        var matches = new List<OpenHostDescriptor>();
        foreach (string path in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            string? target = new FileInfo(path).LinkTarget;
            bool samePath = target == record || target == record + " (deleted)";
            if (!samePath && original is null) continue;
            try
            {
                string value = File.ReadAllText("/proc/self/fdinfo/" + Path.GetFileName(path));
                if (value.Length > 4096) throw new InvalidOperationException("host_descriptor_identity_unbounded");
                string[] lines = value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string mount = lines.Single(line => line.StartsWith("mnt_id:", StringComparison.Ordinal))[7..].Trim();
                string inode = lines.Single(line => line.StartsWith("ino:", StringComparison.Ordinal))[4..].Trim();
                if (mount.Length is < 1 or > 20 || inode.Length is < 1 or > 20 ||
                    !mount.All(char.IsAsciiDigit) || !inode.All(char.IsAsciiDigit))
                    throw new InvalidOperationException("host_descriptor_identity_invalid");
                if (samePath || original is not null && mount == original.MountId && inode == original.Inode)
                    matches.Add(new(path, mount, inode));
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return matches.ToArray();
    }

    private sealed class FailingHostStop(Exception failure) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.FromException(failure);
    }

    private sealed class PrivateHostRoot : IDisposable
    {
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal string Root { get; }
        internal string ContentRoot { get; }

        internal PrivateHostRoot()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("private_host_observation_requires_linux");
            Root = Path.Combine(Path.GetTempPath(), "islamu-assurance", Id);
            Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            ContentRoot = Path.Combine(Root, "actual-host-content");
            Directory.CreateDirectory(ContentRoot);
            using var marker = new FileStream(Path.Combine(Root, TestingResourceIsolation.MarkerName), new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            marker.Write(Encoding.UTF8.GetBytes(Id));
        }

        internal HostApplicationBuilder Builder(string environment = "Assurance")
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                DisableDefaults = true, EnvironmentName = environment, ContentRootPath = ContentRoot,
                ApplicationName = typeof(AppHostOwnershipTests).Assembly.GetName().Name
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ISLAMU_TEST_ROOT"] = Root, ["ISLAMU_TEST_RUN_ID"] = Id
            });
            return builder;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
