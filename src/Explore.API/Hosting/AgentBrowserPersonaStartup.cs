using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using static Explore.Persistence.Seed.AgentBrowserPersonaCatalog;

namespace Explore.API.Hosting;

/// <summary>Pre-traffic development composition of the native Local credential lifecycle.</summary>
public static class AgentBrowserPersonaStartup
{
    public const long AdvisoryLockKey = 0x4147454E54425257;

    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration,
        IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(configuration, environment)) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        CancellationToken token = deadline.Token;
        await using var connectionScope = services.CreateAsyncScope();
        var database = connectionScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (!database.Database.IsNpgsql()) throw Failure("database_provider");
        await using var connection = new NpgsqlConnection(database.Database.GetConnectionString());
        await connection.OpenAsync(token);
        if (connection.Database != "islamu_event_agent") throw Failure("database_name");
        bool locked = false;
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            locked = (bool)(await command.ExecuteScalarAsync(token))!;
            if (!locked) throw Failure("provisioning_contended");
            await ProvisionAsync(services, configuration, token);
        }
        finally
        {
            try
            {
                if (locked && connection.State == System.Data.ConnectionState.Open)
                {
                    await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection) { CommandTimeout = 5 };
                    release.Parameters.AddWithValue("key", AdvisoryLockKey);
                    await release.ExecuteScalarAsync(CancellationToken.None);
                }
            }
            finally { await connection.CloseAsync(); }
        }
    }

    private static async Task ProvisionAsync(IServiceProvider services, IConfiguration configuration, CancellationToken token)
    {
        InstanceBootstrapState? marker;
        var states = new Dictionary<Guid, LocalCredentialOperationStatus?>();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            marker = await db.InstanceBootstrapStates.AsNoTracking().SingleOrDefaultAsync(token);
            if (configuration["INSTANCE_BOOTSTRAP_MODE"] != "ConfiguredAdministrator"
                || configuration["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] != "local"
                || configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] != Administrator.SubjectId.ToString("D"))
                throw Failure("bootstrap_selector");
            if (marker is not null)
            {
                var provider = ActivatorUtilities.CreateInstance<ConfiguredAdministratorBootstrapProvider>(scope.ServiceProvider);
                var binding = await provider.GetVerifiedBindingAsync(new ProviderAccountKey(AuthenticationProviderKind.Local, Administrator.SubjectId.ToString("D")), token);
                if (binding is null || marker.Status is not (InstanceBootstrapStatus.Pending or InstanceBootstrapStatus.Completed)
                    || (marker.Status == InstanceBootstrapStatus.Completed && marker.CompletedByUserId != Administrator.SubjectId))
                    throw Failure("bootstrap_ownership");
            }
            await Binder(scope.ServiceProvider).EnsureOwnershipAsync(marker, token);
        }
        foreach (var persona in All)
            states[persona.SubjectId] = await ClassifyAsync(services, persona, persona.OperationId ?? marker?.Id, token);
        if (marker?.Status == InstanceBootstrapStatus.Completed && states[Administrator.SubjectId] is null)
            throw Failure("completed_receipt_missing");

        var pinned = new Dictionary<string, string>();
        await using (var scope = services.CreateAsyncScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<ISecretResolver>();
            string signing = await RequireSecretAsync(resolver, SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, token);
            ValidateSigningAuthority(scope.ServiceProvider, configuration, signing);
            pinned.Add(SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, signing);
            if (states.Values.Any(state => !Ready(state)))
            {
                string final = await RequireSecretAsync(resolver, SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword, token);
                pinned.Add(SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword, final);
                string? bootstrap = null;
                if (!Ready(states[Administrator.SubjectId]))
                {
                    bootstrap = await RequireSecretAsync(resolver, SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword, token);
                    if (bootstrap == final) throw Failure("passwords_equal");
                    pinned.Add(SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword, bootstrap);
                }
                foreach (var persona in All.Where(persona => !Ready(states[persona.SubjectId])))
                {
                    string temporary = persona == Administrator ? bootstrap! : TemporaryPassword(final, persona.OperationId!.Value);
                    if (temporary == final) throw Failure("passwords_equal");
                    await ValidatePasswordAsync(scope.ServiceProvider, persona, final, token);
                    await ValidatePasswordAsync(scope.ServiceProvider, persona, temporary, token);
                    if (states[persona.SubjectId] is not null)
                    {
                        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
                        var subject = await manager.FindByIdAsync(persona.SubjectId.ToString("D"));
                        if (subject is null || !await manager.CheckPasswordAsync(subject, temporary))
                            throw Failure("temporary_credential_drift");
                    }
                    if (states[persona.SubjectId] is null)
                    {
                        var request = Creation(persona, persona.OperationId ?? marker?.Id ?? Guid.CreateVersion7());
                        if (!await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                            .ValidateBootstrapCreationAsync(request, temporary, token))
                            throw Failure("creation_preflight");
                    }
                }
            }
        }
        await EnsureSecretsUnchangedAsync(services, pinned, token);
        if (marker is null)
        {
            await using var scope = services.CreateAsyncScope();
            var provider = ActivatorUtilities.CreateInstance<ConfiguredAdministratorBootstrapProvider>(scope.ServiceProvider);
            await ActivatorUtilities.CreateInstance<ConfiguredAdministratorBootstrapStartupRunner>(scope.ServiceProvider, provider).PrepareAsync(token);
            marker = await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().InstanceBootstrapStates.AsNoTracking().SingleAsync(token);
        }
        await using (var scope = services.CreateAsyncScope())
            await Binder(scope.ServiceProvider).SeedFoundationAsync(marker.Status, token);

        if (marker.Status != InstanceBootstrapStatus.Completed
            || states[Administrator.SubjectId]?.CredentialState == LocalCredentialState.ProvisioningPending)
        {
            await EnsureSecretsUnchangedAsync(services, pinned, token);
            await using var scope = services.CreateAsyncScope();
            var provider = ActivatorUtilities.CreateInstance<ConfiguredAdministratorBootstrapProvider>(scope.ServiceProvider);
            var secrets = new PinnedSecrets(scope.ServiceProvider.GetRequiredService<ISecretResolver>(), pinned);
            var operation = ActivatorUtilities.CreateInstance<LocalAdministratorBootstrapOperation>(scope.ServiceProvider, secrets);
            await new LocalAdministratorBootstrapRunner(provider,
                scope.ServiceProvider.GetRequiredService<IInstanceBootstrapStateRepository>(), operation).RunAsync(token);
        }
        foreach (var persona in All)
        {
            Guid operationId = persona.OperationId ?? marker.Id;
            LocalCredentialOperationStatus? state = await ClassifyAsync(services, persona, operationId, token);
            if (Ready(state)) continue;
            await EnsureSecretsUnchangedAsync(services, pinned, token);
            string final = pinned[SecretDefinitionRegistry.Keys.Authentication.AgentBrowserPersonaPassword];
            string temporary = persona == Administrator
                ? pinned[SecretDefinitionRegistry.Keys.Authentication.LocalBootstrapPassword]
                : TemporaryPassword(final, operationId);
            if (persona != Administrator)
            {
                if (state is null)
                {
                    await using var creationScope = services.CreateAsyncScope();
                    var created = await creationScope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                        .CreateBootstrapPendingAsync(Creation(persona, operationId), persona.SubjectId, temporary, token);
                    if (created.Outcome is not (LocalCredentialCreateOutcome.Created or LocalCredentialCreateOutcome.Replayed))
                        throw Failure("creation_conflict");
                }
                LocalCredentialProvisioningSnapshot snapshot;
                await using (var scope = services.CreateAsyncScope())
                    snapshot = await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>().ReadProvisioningAsync(operationId, token)
                        ?? throw Failure("receipt_missing");
                await using (var scope = services.CreateAsyncScope())
                    await Binder(scope.ServiceProvider).BindAsync(persona, snapshot, token);
                if (snapshot.Receipt.Stage == LocalCredentialOperationStage.ProvisioningPending)
                {
                    await using var scope = services.CreateAsyncScope();
                    var outcome = await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>()
                        .ActivateChangeRequiredAsync(new LocalCredentialActivationRequest(operationId, snapshot.OperationConcurrencyStamp), token);
                    if (outcome is not (LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated))
                        throw Failure("activation_failed");
                }
            }
            await CompleteFirstUseAsync(services, persona, operationId, temporary, final, token);
        }
        await EnsureSecretsUnchangedAsync(services, pinned, token);
        foreach (var persona in All)
            if (!Ready(await ClassifyAsync(services, persona, persona.OperationId ?? marker.Id, token)))
                throw Failure("completion_incomplete");
    }

    private static async Task<LocalCredentialOperationStatus?> ClassifyAsync(IServiceProvider services, AgentBrowserPersona persona, Guid? operationId, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var state = operationId.HasValue ? await credentials.ReadOperationAsync(operationId.Value, token) : null;
        await Binder(scope.ServiceProvider).ValidateReceiptGraphAsync(persona, state?.Receipt, token);
        if (state is null) return null;
        if (!state.IsCurrent || state.Receipt.Kind != LocalCredentialOperationKind.Create
            || state.Receipt.LocalSubjectId != persona.SubjectId
            || state.Receipt.InitiatingApplicationUserId != Administrator.SubjectId)
            throw Failure("receipt_conflict");
        if (state.CredentialState is LocalCredentialState.Ready or LocalCredentialState.ChangeRequired)
        {
            var binding = await credentials.ReadLinkedIdentityAsync(persona.SubjectId, token);
            if (binding is null || binding.PersonalActorId != state.Receipt.PersonalActorId
                || binding.ExternalLoginId != state.Receipt.ExternalLoginId || binding.CredentialState != state.CredentialState)
                throw Failure("binding_incomplete");
        }
        return state;
    }

    private static async Task CompleteFirstUseAsync(IServiceProvider services, AgentBrowserPersona persona, Guid operationId,
        string temporary, string final, CancellationToken token)
    {
        var state = await ClassifyAsync(services, persona, operationId, token);
        if (Ready(state)) return;
        if (state?.CredentialState != LocalCredentialState.ChangeRequired) throw Failure("first_use_state");
        string username;
        string challenge;
        await using (var scope = services.CreateAsyncScope())
        {
            var snapshot = await scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>().ReadProvisioningAsync(operationId, token)
                ?? throw Failure("receipt_missing");
            username = snapshot.Username;
            var login = await LoginAsync(scope.ServiceProvider, username, temporary, token);
            if (login.Outcome != LocalAuthOutcome.ReplacementRequired || login.ReplacementChallenge is null || login.Token is not null)
                throw Failure("password_proof_failed");
            challenge = login.ReplacementChallenge.Token;
        }
        try
        {
            await using var scope = services.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = token };
            context.Request.Headers.Authorization = "Bearer " + challenge;
            var authentication = await context.AuthenticateAsync(ApiAuthenticationSchemeNames.LocalCredentialReplacement);
            var authority = authentication.Principal?.TryGetLocalCredentialReplacementAuthority();
            if (!authentication.Succeeded || authority is null || authority.Subject.LocalSubjectId != persona.SubjectId
                || authority.Subject.OperationId != operationId) throw Failure("challenge_invalid");
            var completed = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CompleteLocalCredentialReplacementCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new CompleteLocalCredentialReplacementCommand(new LocalCredentialReplacementRequest(authority, final)), token);
            if (!completed.IsSuccess) throw Failure("replacement_failed");
        }
        catch when (!token.IsCancellationRequested)
        {
            if (!Ready(await ClassifyAsync(services, persona, operationId, token))) throw;
        }
        await using (var scope = services.CreateAsyncScope())
        {
            var login = await LoginAsync(scope.ServiceProvider, username, final, token);
            if (login.Token is null || login.ReplacementChallenge is not null)
                throw Failure("ordinary_login_" + (persona == Administrator ? "administrator_" : "persona_") + login.FailureCode);
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = token };
            context.Request.Headers.Authorization = "Bearer " + login.Token;
            if (!(await context.AuthenticateAsync(ApiAuthenticationSchemeNames.LocalIdentity)).Succeeded)
                throw Failure("ordinary_authentication_failed");
        }
    }

    private static Task<LocalAuthResponseDto> LoginAsync(IServiceProvider services, string username, string password, CancellationToken token) =>
        services.GetRequiredService<ICommandHandler<LocalLoginCommand, LocalAuthResponseDto>>()
            .ExecuteAsync(new LocalLoginCommand(new LocalAuthRequestDto(username, password)), token);
    private static bool Ready(LocalCredentialOperationStatus? state) => state?.CredentialState == LocalCredentialState.Ready
        && state.Receipt.Stage == LocalCredentialOperationStage.Replaced && state.IsCurrent;
    private static AgentBrowserPersonaBindingSeeder Binder(IServiceProvider services) =>
        new(services.GetRequiredService<ExploreDbContext>(), services.GetRequiredService<TimeProvider>());
    private static LocalCredentialCreateRequest Creation(AgentBrowserPersona persona, Guid operationId) =>
        new(operationId, Administrator.SubjectId, persona.Email, persona.Name, string.Empty,
            persona == Administrator ? persona.SubjectId.ToString("D") : persona.Email);
    private static string TemporaryPassword(string final, Guid operationId) => "Aa1!" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(final), Encoding.UTF8.GetBytes("islamu-agent-browser-temporary-v1:" + operationId.ToString("D"))));

    private static async Task ValidatePasswordAsync(IServiceProvider services, AgentBrowserPersona persona, string password, CancellationToken token)
    {
        if (password.Length is < LocalIdentityOptions.MinimumPasswordLength or > LocalIdentityOptions.MaximumPasswordLength)
            throw Failure("password_invalid");
        var manager = services.GetRequiredService<UserManager<LocalIdentityUser>>();
        var user = new LocalIdentityUser { Id = persona.SubjectId, UserName = persona.Email, Email = persona.Email };
        foreach (var validator in manager.PasswordValidators)
        {
            token.ThrowIfCancellationRequested();
            if (!(await validator.ValidateAsync(manager, user, password)).Succeeded) throw Failure("password_invalid");
        }
    }

    private static void ValidateSigningAuthority(IServiceProvider services, IConfiguration configuration, string secret)
    {
        byte[] key;
        try { key = Convert.FromBase64String(secret); }
        catch (FormatException) { throw Failure("signing_key_invalid"); }
        if (key.Length < 32 || configuration["Authentication:Local:JwtKey"] != secret) throw Failure("signing_key_mismatch");
        var options = services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();
        foreach (string scheme in new[] { ApiAuthenticationSchemeNames.LocalIdentity, ApiAuthenticationSchemeNames.LocalCredentialReplacement })
            if (options.Get(scheme).TokenValidationParameters.IssuerSigningKey is not SymmetricSecurityKey configured
                || !CryptographicOperations.FixedTimeEquals(configured.Key, key)) throw Failure("signing_key_mismatch");
    }

    private static async Task<string> RequireSecretAsync(ISecretResolver resolver, string key, CancellationToken token)
    {
        var result = await resolver.ResolveAsync(key, null, token);
        return result.IsResolved && !string.IsNullOrWhiteSpace(result.Value) ? result.Value : throw Failure("secret_unavailable");
    }

    private static async Task EnsureSecretsUnchangedAsync(IServiceProvider services, IReadOnlyDictionary<string, string> pinned, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISecretResolver>();
        foreach (var pair in pinned)
            if (await RequireSecretAsync(resolver, pair.Key, token) != pair.Value) throw Failure("secret_authority_drift");
    }

    private sealed class PinnedSecrets(ISecretResolver inner, IReadOnlyDictionary<string, string> pinned) : ISecretResolver
    {
        public async Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default)
        {
            var result = await inner.ResolveAsync(settingKey, tenantId, cancellationToken);
            if (pinned.TryGetValue(settingKey, out string? value) && (!result.IsResolved || result.Value != value)) throw Failure("secret_authority_drift");
            return result;
        }
        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) => inner.ResolveQualifiedAsync(settingKey, scope, scopeId, qualifier, cancellationToken);
        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) => inner.ResolveTenantBindingAsync(tenantId, bindingId, cancellationToken);
        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) => inner.InvalidateAsync(settingKey, scope, scopeId, cancellationToken);
    }

    private static InvalidOperationException Failure(string reason) => new($"agent_browser_{reason}");
}
