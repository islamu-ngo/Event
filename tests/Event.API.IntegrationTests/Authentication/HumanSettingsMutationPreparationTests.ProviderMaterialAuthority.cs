using System.Data;
using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Infrastructure.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("binding")]
    [Arguments("profile")]
    [Arguments("profile-installed")]
    public Task ProviderMaterials_LocalApiRetainsOriginalSnapshot(string change) =>
        WithProviderProfileAsync(change == "profile-installed" ? null : Guid.CreateVersion7().ToString("D"), async () =>
        {
            var observer = new NativeProviderObserver();
            Guid replacementRevision = Guid.CreateVersion7();
            using var replacementConfiguration = (ConfigurationRoot)new ConfigurationBuilder().Build();
            await using var replacementAuthority = SecretAuthorityAcquisition.OpenLocal(
                new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api)
                { AuthenticationProfileRevision = replacementRevision }, replacementConfiguration);
            await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
                configureServices: services => RegisterProviderMaterialObservation(services, observer));
            await SeedPlatformAdministratorAsync(fixture);
            await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
            await RequireApiCredentialBoundaryAsync(request.Services);
            var currentAuthority = request.Services.GetRequiredService<SecretAuthorityAcquisition>();
            var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
            var before = await service.ReadConfigurationAsync();
            await using var original = await service.ObserveConfigurationAsync(NativeProviderCommand(fixture), default);
            await Assert.That(original.AuthenticationProposed.PrimaryProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
            await Assert.That(original.AuthenticationProposed.GoogleSsoEnabled).IsFalse();
            await Assert.That(original.OriginalOwner is EfCoreUnitOfWork).IsTrue();
            DbConnection connection = observer.Context!.Database.GetDbConnection();
            DbTransaction? writtenTransaction = null;
            bool completeImage = false;
            bool changed = false;
            string bindingKey = SecretDefinitionRegistry.Keys.Keycloak.Endpoint;
            observer.AfterWrite = async (database, actual, token) =>
            {
                await Assert.That(actual).IsSameReferenceAs(original);
                await Assert.That(database).IsSameReferenceAs(observer.Context);
                await Assert.That(database.Database.GetDbConnection()).IsSameReferenceAs(connection);
                writtenTransaction = database.Database.CurrentTransaction?.GetDbTransaction()
                    ?? throw new InvalidOperationException("Provider material control lost its original transaction.");
                await Assert.That(writtenTransaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                foreach (var effect in original.Effects)
                {
                    var row = await database.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == effect.Key, token);
                    await Assert.That(ProviderConfigurationRow.From(row)).IsEqualTo(effect);
                }
                completeImage = true;
                if (change == "binding")
                {
                    await Assert.That(await database.SecretBindings.AnyAsync(row => row.SettingKey == bindingKey
                        && row.SettingScopeId == 1 && row.ScopeId == null && row.Qualifier == "", token)).IsFalse();
                    var binding = SecretBinding.CreateEnvironmentVariable(bindingKey, SecretScope.Instance, null,
                        SecretDefinitionRegistry.GetRequired(bindingKey).DefaultEnvironmentVariableName);
                    database.SecretBindings.Add(binding);
                    await Assert.That(await database.SaveChangesAsync(token)).IsEqualTo(1);
                    await Assert.That(await database.SecretBindings.AsNoTracking().AnyAsync(row => row.Id == binding.Id
                        && row.SettingKey == bindingKey, token)).IsTrue();
                    changed = true;
                }
                else if (change is "profile" or "profile-installed")
                {
                    var previous = new AuthenticationCredentialProfileReader(currentAuthority).ObserveCurrent();
                    Guid originalGeneration = currentAuthority.GenerationId;
                    await Assert.That(previous.Revision is null).IsEqualTo(change == "profile-installed");
                    await currentAuthority.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                    var replacement = new AuthenticationCredentialProfileReader(replacementAuthority).ObserveCurrent();
                    await Assert.That(replacement.Revision).IsEqualTo(replacementRevision);
                    await Assert.That(replacement).IsNotEqualTo(previous);
                    await Assert.That(replacementAuthority.GenerationId).IsNotEqualTo(originalGeneration);
                    changed = true;
                }
                await Assert.That(database.Database.CurrentTransaction?.GetDbTransaction()).IsSameReferenceAs(writtenTransaction);
            };
            observer.ObserveCommitted = () =>
            {
                using var independent = fixture.Native.CreateDatabase();
                return observer.Context!.Database.CurrentTransaction is null
                    && independent.SystemSettings.Any(row => row.SettingKey == GovernanceSettingKeys.Authentication.GoogleClientId
                        && row.Value == "\"provider-native-after\"");
            };
            if (change == "unchanged") await service.ApplyConfigurationAsync(original, default);
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
            await Assert.That(observer.Applied).IsTrue();
            await Assert.That(completeImage).IsTrue();
            await Assert.That(writtenTransaction).IsNotNull();
            await Assert.That(changed).IsEqualTo(change != "unchanged");
            await Assert.That(observer.Context!.Database.CurrentTransaction).IsNull();
            await Assert.That(observer.ActivatedAfterCommit).IsNull();
            await using var final = fixture.Native.CreateDatabase();
            foreach (var effect in original.Effects)
            {
                var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == effect.Key);
                await Assert.That(row is null ? null : ProviderConfigurationRow.From(row))
                    .IsEqualTo(change == "unchanged" ? effect : original.Before.System[effect.Key]);
            }
            if (change == "binding")
                await Assert.That(await final.SecretBindings.AnyAsync(row => row.SettingKey == bindingKey
                    && row.SettingScopeId == 1 && row.ScopeId == null && row.Qualifier == "")).IsFalse();
            var after = await service.ReadConfigurationAsync();
            await Assert.That(after.GoogleClientId).IsEqualTo(change == "unchanged" ? "provider-native-after" : before.GoogleClientId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
        });

    [Test]
    [Arguments("keycloak")]
    [Arguments("google")]
    public Task ProviderMaterials_RequestedRemoteApiDoesNotBorrowBffCredentials(string provider) =>
        WithProviderProfileAsync(Guid.CreateVersion7().ToString("D"), async () =>
        {
            var observer = new NativeProviderObserver();
            await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
                configureServices: services => RegisterProviderMaterialObservation(services, observer));
            await SeedPlatformAdministratorAsync(fixture);
            await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                { ["Authentication:Provider"] = "", ["AUTHENTICATION_PROVIDER"] = "" })));
            await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null);
            await RequireApiCredentialBoundaryAsync(request.Services);
            var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
            await using var original = await service.ObserveConfigurationAsync(new UpdateAuthProviderConfigurationCommand
            {
                UserId = fixture.Binding.LocalSubjectId,
                Patch = new()
                {
                    Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()
                    {
                        PrimaryProviderId = (int)(provider == "keycloak" ? AuthenticationProviderKind.Keycloak : AuthenticationProviderKind.Local),
                        KeycloakAuthority = $"https://issuer-{Guid.CreateVersion7():N}.example.test/realms/native",
                        KeycloakClientId = Guid.CreateVersion7().ToString("N"),
                        GoogleSsoEnabled = provider == "google",
                        GoogleClientId = Guid.CreateVersion7().ToString("N")
                    })
                }
            }, default);
            var validation = await original.ValidateAsync();
            await Assert.That(validation.IsValid).IsFalse();
            string property = provider == "keycloak" ? "KeycloakClientSecretOwnership" : "GoogleClientSecretOwnership";
            await Assert.That(validation.Errors.Any(error => error.PropertyName == property)).IsTrue();
            await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => service.ApplyConfigurationAsync(original, default));
            await Assert.That(observer.Applied).IsFalse();
            await Assert.That(observer.ActivatedAfterCommit).IsNull();
            await Assert.That(observer.Context!.Database.CurrentTransaction).IsNull();
            await using var final = fixture.Native.CreateDatabase();
            foreach (var effect in original.Effects)
            {
                var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == effect.Key);
                await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(original.Before.System[effect.Key]);
            }
        });

    private static async Task RequireApiCredentialBoundaryAsync(IServiceProvider services)
    {
        var authority = services.GetRequiredService<SecretAuthorityAcquisition>();
        await Assert.That(authority.Selection.Consumer).IsEqualTo(SecretConsumerRole.Api);
        foreach (string key in new[]
            { SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret, SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret })
            await Assert.That(SecretAcquisitionPolicy.AllowsRoot(authority.Selection.Consumer,
                SecretDefinitionRegistry.GetRequired(key).DefaultInfisicalPath)).IsFalse();
    }

    private static void RegisterProviderMaterialObservation(IServiceCollection services, NativeProviderObserver observer)
    {
        RegisterNativeProvider(services, observer);
        var original = services.Last(item => item.ServiceType == typeof(IAuthenticationProviderModeCacheInvalidator));
        services.RemoveAll<IAuthenticationProviderModeCacheInvalidator>();
        services.AddScoped<IAuthenticationProviderModeCacheInvalidator>(provider => new ProviderActivationObserver(
            (IAuthenticationProviderModeCacheInvalidator)original.ImplementationFactory!(provider), observer));
    }

    private static async Task WithProviderProfileAsync(string? revision, Func<Task> test)
    {
        const string variable = "AUTHENTICATION_CREDENTIAL_PROFILE_REVISION";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, revision);
            await test();
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
    }
}
