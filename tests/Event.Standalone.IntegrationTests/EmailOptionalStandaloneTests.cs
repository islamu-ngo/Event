// Proves zero-email core HTTP operation and native SQLite restart durability through the real combined host.
// Keeps bootstrap, private Local replacement, delivery revocation, key persistence, and required-authority failures integrated.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Standalone.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Instance;
using Explore.Application.Models.Common;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using MediatR;
using Explore.Blazor.Services;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Standalone.IntegrationTests;

[NotInParallel]
public sealed class EmailOptionalStandaloneTests
{
    private const string SmtpPath = "/api/instance/settings/smtp";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task ZeroEmailAdministratorAndGuardedDisableSurviveNativeHostRestarts()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        string privatePassword = NativeEmailOptionalStandaloneFixture.NewPassword();
        string bearer;
        JsonElement preview;
        LocalIdentityBinding binding;
        Guid bootstrapId;
        string protectedValue;
        int keyCount;

        await using (var host = deployment.CreateHost())
        {
            using HttpClient client = host.OpenClient();
            client.Timeout = RequestTimeout;
            await AssertNativeOwnershipAsync(host);
            await AssertCoreAsync(client, "Healthy", "smtp_disabled");
            await Assert.That(deployment.Transport.Attempts).IsEqualTo(0);

            // Headless bootstrap does not create ordinary session authority. Its only login
            // result is the production purpose-limited first-use challenge, even without email.
            using var login = await client.PostAsJsonAsync("/api/auth/local/login", new
            {
                identifier = deployment.Subject.ToString("D"), password = deployment.InitialPassword
            });
            await AssertStatusAsync(login, HttpStatusCode.OK);
            JsonElement challenge = await BodyAsync(login);
            await Assert.That(challenge.TryGetProperty("token", out _)).IsFalse();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                challenge.GetProperty("replacementChallenge").GetProperty("token").GetString());
            using (var denied = await client.GetAsync("/api/instance/local-identities"))
                await AssertStatusAsync(denied, HttpStatusCode.Forbidden);
            using (var replaced = await client.PostAsJsonAsync("/api/auth/local/credential-replacement", new { newPassword = privatePassword }))
                await AssertStatusAsync(replaced, HttpStatusCode.NoContent);
            client.DefaultRequestHeaders.Authorization = null;
            bearer = await LoginAsync(client, deployment.Subject, privatePassword);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            await AssertAdministratorAsync(client);
            await AssertSupportContactAsync(client, "contact@standalone.example.test");

            await using var scope = host.Services.CreateAsyncScope();
            var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
            binding = (await credentials.ReadLinkedIdentityAsync(deployment.Subject, CancellationToken.None))!;
            await Assert.That(binding.CredentialState).IsEqualTo(LocalCredentialState.Ready);
            var bootstrap = await scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateRepository>().GetCurrent();
            await Assert.That(bootstrap!.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
            bootstrapId = bootstrap.Id;
            protectedValue = host.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("native-standalone-restart-proof").Protect("restart-continuity");
            keyCount = await scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>().DataProtectionKeys.CountAsync();
            await Assert.That(keyCount).IsGreaterThan(0);

            using (var enabled = await client.PatchAsJsonAsync(SmtpPath, new PatchInstanceSmtpSettingsDto
            {
                DeliveryEnabled = OptionalUpdate<bool>.Set(true),
                Configuration = OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Set(new()
                {
                    Host = "smtp.example.test", Port = 587, Security = "StartTls",
                    FromAddress = "events@example.test", FromName = "Events", TimeoutSeconds = 30
                })
            }))
                await AssertStatusAsync(enabled, HttpStatusCode.OK);
            await AssertCoreAsync(client, "Degraded", "smtp_unavailable");
            await Assert.That(deployment.Transport.Attempts).IsGreaterThan(0);
            using var previewResponse = await client.PostAsync(SmtpPath + "/disable-preview", null);
            await AssertStatusAsync(previewResponse, HttpStatusCode.OK);
            preview = await BodyAsync(previewResponse);
        }

        // No bootstrap password remains in the selected Environment authority. A decoy in a
        // different configuration source must neither reset nor recreate the completed identity.
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", null);
        deployment.SetEnvironment("MAIL_SMTP_HOST", "bootstrap-prefill.example.test");
        var decoy = new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_LOCAL_PASSWORD"] = NativeEmailOptionalStandaloneFixture.NewPassword()
        };
        await using (var restarted = deployment.CreateHost(decoy))
        {
            using HttpClient client = restarted.OpenClient();
            client.Timeout = RequestTimeout;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            await AssertAdministratorAsync(client);
            await AssertDurableIdentityAsync(restarted, deployment.Subject, binding, bootstrapId);
            await AssertSupportContactAsync(client, "contact@standalone.example.test");
            string recovered = restarted.Services.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("native-standalone-restart-proof").Unprotect(protectedValue);
            await Assert.That(recovered).IsEqualTo("restart-continuity");

            // The pre-restart HTTP confirmation is purpose-protected by the native database key
            // ring. Success proves key reload through the real privileged write, not just a local round trip.
            using var disabled = await client.PostAsJsonAsync(SmtpPath + "/disable", new
            {
                expectedRevision = preview.GetProperty("expectedRevision").GetInt64(),
                confirmationToken = preview.GetProperty("confirmationToken").GetString(),
                acknowledgement = "DISABLE EMAIL DELIVERY"
            });
            await AssertStatusAsync(disabled, HttpStatusCode.OK);
            await AssertCoreAsync(client, "Healthy", "smtp_disabled");
        }

        int attemptsBeforeDisabledRestart = deployment.Transport.Attempts;
        await using (var restartedDisabled = deployment.CreateHost(decoy))
        {
            using HttpClient client = restartedDisabled.OpenClient();
            client.Timeout = RequestTimeout;
            await AssertNativeOwnershipAsync(restartedDisabled);
            await AssertCoreAsync(client, "Healthy", "smtp_disabled");
            await Assert.That(deployment.Transport.Attempts).IsEqualTo(attemptsBeforeDisabledRestart);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                await LoginAsync(client, deployment.Subject, privatePassword));
            await AssertAdministratorAsync(client);
            await AssertDurableIdentityAsync(restartedDisabled, deployment.Subject, binding, bootstrapId);
            using var settingsResponse = await client.GetAsync(SmtpPath);
            await AssertStatusAsync(settingsResponse, HttpStatusCode.OK);
            JsonElement settings = await BodyAsync(settingsResponse);
            await Assert.That(settings.GetProperty("deliveryEnabled").GetBoolean()).IsFalse();
            await Assert.That(settings.GetProperty("fromAddress").GetString()).IsEqualTo("events@example.test");
            await AssertSupportContactAsync(client, "contact@standalone.example.test");
            await using var scope = restartedDisabled.Services.CreateAsyncScope();
            await Assert.That(await scope.ServiceProvider.GetRequiredService<DataProtectionKeyContext>()
                .DataProtectionKeys.CountAsync()).IsEqualTo(keyCount);
            using var oldPassword = await client.PostAsJsonAsync("/api/auth/local/login", new
            {
                identifier = deployment.Subject.ToString("D"), password = deployment.InitialPassword
            });
            await AssertStatusAsync(oldPassword, HttpStatusCode.Unauthorized);
        }
        await Assert.That(File.Exists(deployment.DatabasePath)).IsTrue();
    }

    [Test]
    public async Task DefaultDirectoryBrowsingRemainsAvailableWithoutEmail()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var host = deployment.CreateHost();
        using var client = host.OpenClient();
        client.Timeout = RequestTimeout;
        using var response = await client.GetAsync("/api/Event");
        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    [Test]
    public async Task PublicSupportContactRoundTripsWithoutChangingExplicitSmtpPolicy()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        string setupSecret = NativeEmailOptionalStandaloneFixture.NewPassword();
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_MODE", "Interactive");
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_ADMIN_PROVIDER", null);
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", null);
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_BINDING_GENERATION", null);
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", null);
        deployment.SetEnvironment("SETUP_SECRET", setupSecret);

        await using (var host = deployment.CreateHost())
        {
            using var client = host.OpenClient();
            client.Timeout = RequestTimeout;
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IInstanceSmtpSettingService>().ApplySettingsAsync(new InstanceSmtpSettingsDto
            {
                Host = "smtp.example.test", Port = 587, Security = "StartTls",
                FromAddress = "sender@example.test", FromName = "Sender", TimeoutSeconds = 30
            }, enableDelivery: true);
            // The Local setup-secret surface intentionally does not authorize general profile/SMTP
            // HTTP routes. Exercise the real command here, without inventing new provider authority.
            _ = await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync();
            foreach (string contact in new[] { "support@example.test", "changed@example.test" })
            {
                await SaveSupportContactAsync(services, " " + contact + " ");
                await AssertProfileAndSenderAsync(services, contact);
            }
        }
        await using (var restarted = deployment.CreateHost())
        {
            using var client = restarted.OpenClient();
            client.Timeout = RequestTimeout;
            await using var scope = restarted.Services.CreateAsyncScope();
            await AssertProfileAndSenderAsync(scope.ServiceProvider, "changed@example.test");
            await SaveSupportContactAsync(scope.ServiceProvider, null);
            await AssertProfileAndSenderAsync(scope.ServiceProvider, null);
        }
        await using (var restartedCleared = deployment.CreateHost())
        {
            using var client = restartedCleared.OpenClient();
            await using var scope = restartedCleared.Services.CreateAsyncScope();
            await AssertProfileAndSenderAsync(scope.ServiceProvider, null);
        }
    }

    [Test]
    public async Task MissingSelectedBootstrapSecretCannotFallBackToConfiguration()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        deployment.SetEnvironment("INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", null);
        await using var host = deployment.CreateHost(new Dictionary<string, string?>
        {
            ["INSTANCE_BOOTSTRAP_LOCAL_PASSWORD"] = NativeEmailOptionalStandaloneFixture.NewPassword()
        });
        Exception? failure = null;
        try { using var client = host.OpenClient(); }
        catch (Exception exception) { failure = exception; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(HasFailureCode(failure!, "local_bootstrap_secret_unavailable")).IsTrue();
        await Assert.That(deployment.Transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task RequiredDatabaseFailurePreventsHttpStartupWithoutSmtp()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        // A directory cannot be opened as an SQLite database; no connection-string secret or
        // repository substitute is involved in this required-dependency failure.
        deployment.SetEnvironment("DATABASE_NAME", Path.GetDirectoryName(deployment.DatabasePath));
        await using var host = deployment.CreateHost();
        await Assert.That(() => host.OpenClient()).Throws<Microsoft.Data.Sqlite.SqliteException>();
        await Assert.That(deployment.Transport.Attempts).IsEqualTo(0);
    }

    [Test]
    public async Task LostPersistedPolicyAuthorityIsUnhealthyRatherThanIntentionallyDisabled()
    {
        using var deployment = new NativeEmailOptionalStandaloneFixture();
        await using var host = deployment.CreateHost();
        using HttpClient client = host.OpenClient();
        client.Timeout = RequestTimeout;
        await AssertCoreAsync(client, "Healthy", "smtp_disabled");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await database.Database.ExecuteSqlRawAsync("DROP TABLE ie_system_settings");
        }
        using var health = await client.GetAsync("/health");
        await AssertStatusAsync(health, HttpStatusCode.ServiceUnavailable);
        JsonElement body = await BodyAsync(health);
        await Assert.That(Check(body, "database").GetProperty("status").GetString()).IsEqualTo("Healthy");
        await Assert.That(Check(body, "smtp").GetProperty("status").GetString()).IsEqualTo("Unhealthy");
        await Assert.That(Check(body, "cerbos").GetProperty("status").GetString()).IsEqualTo("Unhealthy");
        await Assert.That(deployment.Transport.Attempts).IsEqualTo(0);
    }

    private static async Task AssertNativeOwnershipAsync(NativeEmailOptionalStandaloneFixture.NativeFactory host)
    {
        await Assert.That(host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName).IsEqualTo("Staging");
        await Assert.That(host.Services.GetRequiredService<IDynamicAuthSchemeManager>().GetActivePrimaryProvider()).IsEqualTo("local");
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(database.Database.ProviderName).IsEqualTo("Microsoft.EntityFrameworkCore.Sqlite");
        await Assert.That((await database.Database.GetAppliedMigrationsAsync()).Any()).IsTrue();
        await Assert.That(await host.Services.GetRequiredService<ISetupSecretProvider>().IsSetupModeActiveAsync()).IsFalse();
    }

    private static async Task AssertDurableIdentityAsync(NativeEmailOptionalStandaloneFixture.NativeFactory host,
        Guid subject, LocalIdentityBinding binding, Guid bootstrapId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        await Assert.That(await credentials.ReadLinkedIdentityAsync(subject, CancellationToken.None)).IsEqualTo(binding);
        var identities = await credentials.ListAsync(new LocalIdentityListRequest(1, 10), CancellationToken.None);
        await Assert.That(identities.TotalCount).IsEqualTo(1);
        await Assert.That(identities.Items.Single().Email).IsNull();
        var bootstrap = await scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateRepository>().GetCurrent();
        await Assert.That(bootstrap!.Id).IsEqualTo(bootstrapId);
        await Assert.That(bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
    }

    private static async Task AssertCoreAsync(HttpClient client, string smtpStatus, string smtpCode)
    {
        using var health = await client.GetAsync("/health");
        JsonElement body = await BodyAsync(health);
        // Only bounded health names/statuses enter assertion diagnostics, never provider error text.
        string unhealthy = string.Join(",", body.GetProperty("checks").EnumerateArray()
            .Where(check => check.GetProperty("status").GetString() == "Unhealthy")
            .Select(check => check.GetProperty("name").GetString()));
        await Assert.That(unhealthy).IsEqualTo(string.Empty);
        await AssertStatusAsync(health, HttpStatusCode.OK);
        await Assert.That(Check(body, "smtp").GetProperty("status").GetString()).IsEqualTo(smtpStatus);
        await Assert.That(Check(body, "smtp").GetProperty("description").GetString()).IsEqualTo(smtpCode);
        await Assert.That(body.GetRawText().Contains("external-transport-diagnostic-canary", StringComparison.Ordinal)).IsFalse();
        foreach (string path in new[] { "/alive", "/api/EventType", "/auth/status" })
        {
            using var response = await client.GetAsync(path);
            await AssertStatusAsync(response, HttpStatusCode.OK);
        }
    }

    private static async Task AssertSupportContactAsync(HttpClient client, string? expected)
    {
        using var response = await client.GetAsync("/api/instance/settings/branding");
        await AssertStatusAsync(response, HttpStatusCode.OK);
        JsonElement body = await BodyAsync(response);
        string? contact = body.TryGetProperty("supportEmail", out var value) ? value.GetString() : null;
        await Assert.That(contact).IsEqualTo(expected);
    }

    private static async Task SaveSupportContactAsync(IServiceProvider services, string? contact)
    {
        var saved = await services.GetRequiredService<ISender>().Send(new SaveInstanceOnboardingProfileCommand
        {
            Profile = new SelfHostOnboardingProfileDto
            {
                SiteName = "Public directory", SupportEmail = contact, Locale = "en", TimeZone = "UTC"
            }
        });
        await Assert.That(saved.IsSuccess).IsTrue();
    }

    private static async Task AssertProfileAndSenderAsync(IServiceProvider services, string? contact)
    {
        var branding = (await services.GetRequiredService<IInstanceGovernanceSettingService>().ReadSettingsAsync()).Branding;
        await Assert.That(branding.SupportEmail).IsEqualTo(contact);
        var smtp = await services.GetRequiredService<IInstanceSmtpSettingService>().ReadSettingsAsync();
        await Assert.That(smtp).IsEqualTo(new InstanceSmtpSettingsDto
        {
            DeliveryEnabled = true, Host = "smtp.example.test", Port = 587, Security = "StartTls",
            FromAddress = "sender@example.test", FromName = "Sender", TimeoutSeconds = 30,
            SkipCertificateValidation = false
        });
    }

    private static async Task AssertAdministratorAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/instance/local-identities");
        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    private static async Task<string> LoginAsync(HttpClient client, Guid subject, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", new { identifier = subject.ToString("D"), password });
        await AssertStatusAsync(response, HttpStatusCode.OK);
        JsonElement body = await BodyAsync(response);
        await Assert.That(body.TryGetProperty("replacementChallenge", out _)).IsFalse();
        return body.GetProperty("token").GetString()!;
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected) =>
        await Assert.That(response.StatusCode).IsEqualTo(expected);

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using JsonDocument body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.Clone();
    }

    private static JsonElement Check(JsonElement body, string name) => body.GetProperty("checks").EnumerateArray()
        .Single(check => check.GetProperty("name").GetString() == name);

    private static bool HasFailureCode(Exception failure, string code) =>
        failure.Message.Contains(code, StringComparison.Ordinal)
        || failure.InnerException is not null && HasFailureCode(failure.InnerException, code);
}
