using Explore.AppHost;
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
