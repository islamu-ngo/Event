// Runs the real Staging standalone entrypoint with its native SQLite migrations, bootstrap, and key store.
// Owns disposable deployment environment authority across host restarts and substitutes only external SMTP diagnostics.

using System.Runtime.Loader;
using System.Security.Cryptography;
using Event.Standalone.Hosting;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Standalone.IntegrationTests.Fixtures;

internal sealed class NativeEmailOptionalStandaloneFixture : IDisposable
{
    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);
    private readonly string _directory = Directory.CreateTempSubdirectory("email-optional-standalone-").FullName;

    public Guid Subject { get; } = Guid.CreateVersion7();
    public string InitialPassword { get; } = NewPassword();
    public string DatabasePath => Path.Combine(_directory, "event.db");
    public SmtpDiagnosticTransport Transport { get; } = new();

    public NativeEmailOptionalStandaloneFixture()
    {
        // The fixture is serialized: the production secret authority reads the process environment,
        // not an in-memory configuration provider or a fallback secret resolver.
        foreach (string prefix in new[] { "Database__", "DATABASE_", "IdentityDatabase__", "IDENTITY_DATABASE_",
                     "Keycloak__", "KEYCLOAK_", "Smtp__", "SMTP_", "MAIL_SMTP_", "Authentication__",
                     "AUTHENTICATION_", "Authorization__", "AUTHORIZATION_", "INSTANCE_BOOTSTRAP_",
                     "SecretProvider__", "ConnectionStrings__", "CERBOS_", "Cerbos__" })
        {
            foreach (string name in Environment.GetEnvironmentVariables().Keys.Cast<string>()
                         .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                SetEnvironment(name, null);
        }

        var values = new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["SecretProvider__Provider"] = "Environment",
            ["DATABASE_PROVIDER"] = "Sqlite",
            ["DATABASE_NAME"] = DatabasePath,
            ["AUTHENTICATION_PROVIDER"] = "local",
            ["AUTHORIZATION_PROVIDER"] = "local",
            ["AUTHENTICATION_LOCAL_JWT_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
            ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "local",
            ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Subject.ToString("D"),
            ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
            ["INSTANCE_BOOTSTRAP_LOCAL_PASSWORD"] = InitialPassword,
            ["DEPLOYMENT_MODE"] = "single_tenant",
            ["Deployment__DefaultTenantId"] = Explore.Domain.Constants.PlatformDefaults.DefaultTenantId.ToString("D"),
            ["PrivacyErasure__Authority__Topology"] = "CoLocated",
            ["Storage__Local__RootPath"] = Path.Combine(_directory, "storage"),
            ["SETUP_SECRET_FILE"] = Path.Combine(_directory, "setup-secret"),
            ["HttpsRedirection__Enabled"] = "false",
            ["MCP_ENABLED"] = "false",
            ["OutboxProcessor__Enabled"] = "false",
            ["Webhooks__Enabled"] = "false",
            ["NotificationFanoutProcessor__Enabled"] = "false",
            ["Scheduler__Quartz__Enabled"] = "false",
            ["EmailDispatchProcessor__Enabled"] = "false",
            ["EmailDispatchRabbitMq__Enabled"] = "false",
            ["Cerbos__PolicyBootSync__InitialDelaySeconds"] = "0",
            ["Instance__OperatorIdentity__OperatorId"] = Guid.CreateVersion7().ToString("D"),
            ["Instance__OperatorIdentity__PublicName"] = "Native Standalone Operator",
            ["Instance__OperatorIdentity__LegalName"] = "Native Standalone Operator ASBL",
            ["Instance__OperatorIdentity__IsOfficialInstance"] = "false",
            ["Instance__OperatorIdentity__OfficialOrigin"] = "https://standalone.example.test",
            ["Instance__OperatorIdentity__OperatorKindCode"] = "registered_organization",
            ["Instance__OperatorIdentity__JurisdictionCountryCode"] = "BE",
            ["Instance__OperatorIdentity__RegistrationIdentifier"] = "BE 0123.456.789",
            ["Instance__OperatorIdentity__PublicContactEmail"] = "contact@standalone.example.test",
            ["Instance__OperatorIdentity__WebsiteUrl"] = "https://standalone.example.test",
            ["Instance__OperatorIdentity__LegalNoticeUrl"] = "https://standalone.example.test/legal",
            ["Instance__OperatorIdentity__TermsUrl"] = "https://standalone.example.test/terms",
            ["Instance__OperatorIdentity__PrivacyUrl"] = "https://standalone.example.test/privacy"
        };
        foreach ((string name, string? value) in values)
            SetEnvironment(name, value);

        // Standalone's native package includes these assemblies. WebApplicationFactory does not
        // transitively copy the host's custom migration output target into the test executable.
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string root = FindRepositoryRoot();
        foreach (string assembly in new[] { "Explore.Persistence.Migrations.Sqlite",
                     "Explore.Persistence.DataProtection.Migrations.Sqlite",
                     "Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite" })
        {
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "src", assembly,
                "bin", configuration, "net10.0", assembly + ".dll"));
        }
    }

    public NativeFactory CreateHost(IReadOnlyDictionary<string, string?>? configurationOverrides = null) =>
        new(Transport, configurationOverrides);

    public void SetEnvironment(string name, string? value)
    {
        _previousEnvironment.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    public void Dispose()
    {
        foreach ((string name, string? value) in _previousEnvironment)
            Environment.SetEnvironmentVariable(name, value);
        Directory.Delete(_directory, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the native migration build output.");
    }

    internal sealed class NativeFactory(SmtpDiagnosticTransport transport,
        IReadOnlyDictionary<string, string?>? configurationOverrides) : WebApplicationFactory<StandaloneHostMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.UseStaticWebAssets();
            if (configurationOverrides is not null)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(configurationOverrides));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailConnectionTester>();
                services.AddSingleton<IEmailConnectionTester>(transport);
            });
        }

        public HttpClient OpenClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    internal sealed class SmtpDiagnosticTransport : IEmailConnectionTester
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public Task<EmailResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(EmailResult.Fail("external-transport-diagnostic-canary",
                outcome: SmtpDeliveryOutcome.TransientFailure));
        }
    }
}
