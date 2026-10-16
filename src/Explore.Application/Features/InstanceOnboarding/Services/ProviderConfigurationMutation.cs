using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Settings;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public interface IProviderConfigurationSettingsWriter
{
    Task<ProviderConfigurationState> ObserveAsync(IProviderConfigurationMutationSource source, IUnitOfWork owner, CancellationToken token);
    void RequireIdle();
    DateTime RepresentTimestamp(DateTime timestamp);
    Task RequireUnchangedAsync(ProviderConfigurationState original, CancellationToken token);
    void PrepareSetupAttempt(ProviderConfigurationMutation mutation);
    Task<ImmutableArray<SettingChangedNotification>> ExecuteAsync(ProviderConfigurationMutation mutation, CancellationToken token);
    Task RequireAppliedAsync(ProviderConfigurationMutation mutation, CancellationToken token);
}

public interface IProviderConfigurationSetupAuthority
{
    Task<ITrustedSetupSettingsAuthorityLease> CaptureAsync(TrustedSetupSettingsSource source, CancellationToken token);
}

/// <summary>The first privately issued provider observation and its one non-replaying physical owner.</summary>
public sealed class ProviderConfigurationMutation : IAsyncDisposable
{
    private readonly object issuer;
    private readonly string commandSnapshot;
    private IPolicyMutationExecutionScope? scope;
    private string current = "";
    private string proposed = "";
    private ValidationResult? preflight;
    private IAuthorizationProviderConfigurationService? authorizationVerifier;
    private bool compiled;
    private bool disposed;
    private int consumed;
    private int claimed;

    private ProviderConfigurationMutation(object command, object issuer)
    {
        OriginalCommand = command;
        this.issuer = issuer;
        commandSnapshot = JsonSerializer.Serialize(command, command.GetType());
        Actor = command switch
        {
            UpdateAuthProviderConfigurationCommand value => value.UserId,
            UpdateAuthorizationProviderConfigurationCommand value => value.UserId,
            UpdateAuthProviderConfigurationDuringSetupCommand => null,
            UpdateAuthorizationProviderConfigurationDuringSetupCommand => null,
            _ => throw new UnauthorizedAccessException("An actual closed provider command is required.")
        };
    }

    public object OriginalCommand { get; }
    public Guid? Actor { get; }
    public bool IsSetup => OriginalCommand is UpdateAuthProviderConfigurationDuringSetupCommand or UpdateAuthorizationProviderConfigurationDuringSetupCommand;
    public bool IsAuthentication => OriginalCommand is UpdateAuthProviderConfigurationCommand or UpdateAuthProviderConfigurationDuringSetupCommand;
    public IProviderConfigurationMutationSource Source { get; private set; } = null!;
    public IUnitOfWork OriginalOwner { get; private set; } = null!;
    public ProviderConfigurationState Before { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }
    public ImmutableArray<ProviderConfigurationRow> Effects { get; private set; }
    internal IServiceProvider Services => scope!.Services;
    public AuthProviderConfigurationDto AuthenticationCurrent => JsonSerializer.Deserialize<AuthProviderConfigurationDto>(current)!;
    public AuthProviderConfigurationDto AuthenticationProposed => JsonSerializer.Deserialize<AuthProviderConfigurationDto>(proposed)!;
    public AuthorizationProviderConfigurationDto AuthorizationCurrent => JsonSerializer.Deserialize<AuthorizationProviderConfigurationDto>(current)!;
    public AuthorizationProviderConfigurationDto AuthorizationProposed => JsonSerializer.Deserialize<AuthorizationProviderConfigurationDto>(proposed)!;
    internal AuthProviderConfigurationWriteDto AuthenticationPatch
    {
        get
        {
            var group = OriginalCommand switch
            {
                UpdateAuthProviderConfigurationCommand => JsonSerializer.Deserialize<UpdateAuthProviderConfigurationCommand>(commandSnapshot)!.Patch.Configuration,
                UpdateAuthProviderConfigurationDuringSetupCommand => JsonSerializer.Deserialize<UpdateAuthProviderConfigurationDuringSetupCommand>(commandSnapshot)!.Patch.Configuration,
                _ => throw new UnauthorizedAccessException()
            };
            return group is { HasValue: true, Value: { } value } ? value
                : throw new ArgumentException("A complete authentication provider group is required.");
        }
    }
    internal AuthorizationProviderConfigurationWriteDto AuthorizationPatch
    {
        get
        {
            var group = OriginalCommand switch
            {
                UpdateAuthorizationProviderConfigurationCommand => JsonSerializer.Deserialize<UpdateAuthorizationProviderConfigurationCommand>(commandSnapshot)!.Patch.Configuration,
                UpdateAuthorizationProviderConfigurationDuringSetupCommand => JsonSerializer.Deserialize<UpdateAuthorizationProviderConfigurationDuringSetupCommand>(commandSnapshot)!.Patch.Configuration,
                _ => throw new UnauthorizedAccessException()
            };
            return group is { HasValue: true, Value: { } value } ? value
                : throw new ArgumentException("A complete authorization provider group is required.");
        }
    }

    internal static async Task<ProviderConfigurationMutation> ObserveAsync(object command, object issuer,
        IPolicyMutationExecutionScopeFactory factory, CancellationToken token)
    {
        var mutation = new ProviderConfigurationMutation(command, issuer);
        try
        {
            mutation.scope = await factory.CreateAsync(PolicyMutationScope.Instance, token);
            var services = mutation.Services;
            mutation.Source = await services.GetRequiredService<IProviderConfigurationMutationAuthority>().CaptureAsync(command, token);
            mutation.OriginalOwner = services.GetRequiredService<IUnitOfWork>();
            var writer = services.GetRequiredService<IProviderConfigurationSettingsWriter>();
            mutation.Before = await writer.ObserveAsync(mutation.Source, mutation.OriginalOwner, token);
            mutation.OccurredAt = writer.RepresentTimestamp(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
            mutation.RequireActive();
            return mutation;
        }
        catch { await mutation.DisposeAsync(); throw; }
    }

    internal void CompileAuthentication(AuthProviderConfigurationDto original, IEnumerable<SystemSetting> rows)
    {
        if (!IsAuthentication || compiled) throw new UnauthorizedAccessException();
        var patch = AuthenticationPatch ?? throw new ArgumentException("A complete provider group is required.");
        current = JsonSerializer.Serialize(original);
        proposed = JsonSerializer.Serialize(new AuthProviderConfigurationDto
        {
            PrimaryProviderId = patch.PrimaryProviderId,
            PrimaryProviderCode = "",
            PrimaryProviderName = "",
            LockPrimaryProvider = patch.LockPrimaryProvider,
            KeycloakAuthority = patch.KeycloakAuthority,
            KeycloakClientId = patch.KeycloakClientId,
            KeycloakDetectedFromEnvironment = original.KeycloakDetectedFromEnvironment,
            KeycloakClientSecretOwnership = original.KeycloakClientSecretOwnership,
            AtprotoLoginEnabled = patch.AtprotoLoginEnabled,
            AtprotoPublicUrl = patch.AtprotoPublicUrl,
            GoogleSsoEnabled = patch.GoogleSsoEnabled,
            GoogleClientId = patch.GoogleClientId,
            GoogleClientSecretOwnership = original.GoogleClientSecretOwnership,
            LockAtprotoLoginEnabled = patch.LockAtprotoLoginEnabled,
            LockGoogleSsoEnabled = patch.LockGoogleSsoEnabled,
            KeycloakPublicOnboardingPolicy = patch.KeycloakPublicOnboardingPolicy,
            KeycloakPublicSignupUrl = patch.KeycloakPublicSignupUrl,
            GooglePublicOnboardingPolicy = patch.GooglePublicOnboardingPolicy,
            GooglePublicSignupUrl = patch.GooglePublicSignupUrl
        });
        Freeze(rows);
    }

    internal async Task CompileAuthorizationAsync(AuthorizationProviderConfigurationDto original,
        IEnumerable<SystemSetting> rows, IAuthorizationProviderConfigurationService service, CancellationToken token)
    {
        if (IsAuthentication || compiled) throw new UnauthorizedAccessException();
        authorizationVerifier = service;
        var patch = AuthorizationPatch ?? throw new ArgumentException("A complete provider group is required.");
        current = JsonSerializer.Serialize(original);
        var projection = new AuthorizationProviderConfigurationDto
        {
            Provider = patch.Provider,
            CerbosGrpcEndpoint = patch.CerbosGrpcEndpoint,
            CerbosAdminEndpoint = patch.CerbosAdminEndpoint,
            CerbosDetectedFromEnvironment = original.CerbosDetectedFromEnvironment,
            AuthorizationProviderConfigured = original.AuthorizationProviderConfigured,
            AuthorizationProviderManagedByDeployment = original.AuthorizationProviderManagedByDeployment,
            AuthorizationProviderBootstrapStatus = original.AuthorizationProviderBootstrapStatus,
            CerbosPoliciesSynchronized = original.CerbosPoliciesSynchronized,
            AuthorizationProviderBootstrapMessage = original.AuthorizationProviderBootstrapMessage,
            CerbosEndpointOwnership = original.CerbosEndpointOwnership
        };
        proposed = JsonSerializer.Serialize(projection);
        preflight = await new AuthorizationProviderConfigurationDtoValidator().ValidateAsync(projection, token);
        if (original.AuthorizationProviderManagedByDeployment
            && !projection.Provider.Equals(original.Provider, StringComparison.OrdinalIgnoreCase))
            preflight = new([new ValidationFailure("Provider", "The authorization provider is managed by deployment configuration and cannot be changed in the application.")]);
        if (preflight.IsValid && !original.AuthorizationProviderManagedByDeployment
            && projection.Provider.Equals("cerbos", StringComparison.OrdinalIgnoreCase))
        {
            Services.GetRequiredService<IProviderConfigurationSettingsWriter>().RequireIdle();
            if (!await service.VerifyCerbosEndpointAsync(projection.CerbosGrpcEndpoint, token))
                preflight = new([new ValidationFailure("CerbosGrpcEndpoint", "Cerbos gRPC endpoint could not be verified.")]);
            else if (!string.IsNullOrWhiteSpace(projection.CerbosAdminEndpoint)
                && !await service.VerifyCerbosAdminEndpointAsync(projection.CerbosAdminEndpoint, token))
                preflight = new([new ValidationFailure("CerbosAdminEndpoint", "Cerbos Admin API endpoint is not allowed.")]);
        }
        Freeze(rows);
    }

    private void Freeze(IEnumerable<SystemSetting> rows)
    {
        RequireActive();
        Effects = rows.Select(row =>
        {
            var original = Before.System[row.SettingKey];
            if (IsAuthentication)
            {
                var definition = SettingRegistry.Get(row.SettingKey)!;
                row.ValueType = original?.ValueType ?? definition.ValueType;
                row.AllowedValues = original?.AllowedValues;
                row.Category = original is null ? definition.Category : original.Category;
                row.Description = original is null ? definition.Description : original.Description;
                row.DisplayOrder = original?.DisplayOrder ?? 0;
            }
            row.Id = original?.Id ?? Guid.CreateVersion7();
            row.CreatedAt = original?.CreatedAt ?? OccurredAt;
            row.CreatedBy = original is null ? Actor : original.CreatedBy;
            row.UpdatedAt = OccurredAt;
            row.UpdatedBy = Actor;
            return ProviderConfigurationRow.From(row);
        }).ToImmutableArray();
        if (Effects.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != Effects.Length)
            throw new UnauthorizedAccessException("Provider effects cannot duplicate keys.");
        compiled = true;
    }

    public async Task<ValidationResult> ValidateAsync(CancellationToken token = default)
    {
        RequireActive();
        if (!compiled) throw new UnauthorizedAccessException("The original provider projection has not been compiled.");
        if (!IsAuthentication) return preflight ?? throw new UnauthorizedAccessException();
        var projection = AuthenticationProposed;
        var validation = await new AuthProviderConfigurationDtoValidator(AuthenticationCurrent).ValidateAsync(projection, token);
        if (!validation.IsValid || IsSetup) return validation;
        if (!Before.HasOriginalUser(Actor!.Value))
            return new([new ValidationFailure("User", "Current user could not be resolved.")]);
        return AuthenticationProviderLockoutPolicy.PreservesCurrentAdministratorAccess(Before.OriginalLogins(Actor!.Value), projection)
            ? validation : new([new ValidationFailure("Provider", "Cannot disable all authentication providers linked to your current admin account.")]);
    }

    internal async Task ExecuteAsync(object originalIssuer, CancellationToken token)
    {
        RequireActive();
        if (!ReferenceEquals(originalIssuer, issuer) || Interlocked.Exchange(ref consumed, 1) != 0)
            throw new UnauthorizedAccessException("Provider observations cannot change issuer or replay.");
        var validation = await ValidateAsync(token);
        if (!validation.IsValid) throw new ValidationException(validation.Errors);
        var writer = Services.GetRequiredService<IProviderConfigurationSettingsWriter>();
        await Source.RequireExternalSourcesAsync(token);
        if (!IsAuthentication && !AuthorizationCurrent.AuthorizationProviderManagedByDeployment
            && AuthorizationProposed.Provider.Equals("cerbos", StringComparison.OrdinalIgnoreCase))
        {
            writer.RequireIdle();
            var original = AuthorizationProposed;
            if (!await authorizationVerifier!.VerifyCerbosEndpointAsync(original.CerbosGrpcEndpoint, token)
                || !string.IsNullOrWhiteSpace(original.CerbosAdminEndpoint)
                    && !await authorizationVerifier.VerifyCerbosAdminEndpointAsync(original.CerbosAdminEndpoint, token))
                throw new UnauthorizedAccessException("The original provider endpoint preflight changed.");
        }
        await writer.RequireUnchangedAsync(Before, token);
        var preparation = Services.GetRequiredService<IHumanSettingsMutationPreparation>();
        IPreparedHumanSettingsMutation? prepared = OriginalCommand switch
        {
            UpdateAuthProviderConfigurationCommand command => await preparation.PrepareAsync(command, this, token),
            UpdateAuthorizationProviderConfigurationCommand command => await preparation.PrepareAsync(command, this, token),
            _ => null
        };
        var notifications = await Services.GetRequiredService<ISettingMutationLock>().ExecuteOrderedGroupsAsync(
            [ProviderConfigurationState.Keys], ct => IsSetup
                ? ((ISerializableAttemptOwner)OriginalOwner).ExecuteAsync(inner =>
                {
                    writer.PrepareSetupAttempt(this);
                    return writer.ExecuteAsync(this, inner);
                }, ct)
                : preparation.ExecutePreparedAsync(prepared!, (owner, inner) =>
                    preparation.ExecuteProviderJoinedAsync(owner, this, (_, joined) => writer.ExecuteAsync(this, joined), inner), ct), token);
        foreach (var notification in notifications)
            await Services.GetServices<INotificationHandler<SettingChangedNotification>>().HandleAsync(notification, CancellationToken.None);
    }

    public void ClaimOnce()
    {
        RequireActive();
        if (Interlocked.Exchange(ref claimed, 1) != 0) throw new UnauthorizedAccessException("Provider effects have one physical attempt.");
    }

    public void RequireActive()
    {
        if (disposed || scope is null || Source is null
            || !ReferenceEquals(Source.OriginalCommand, OriginalCommand)
            || JsonSerializer.Serialize(OriginalCommand, OriginalCommand.GetType()) != commandSnapshot
            || IsSetup != (Source is IProviderConfigurationSetupSource)
            || !IsSetup && (Source is not IProviderConfigurationHumanSource person || person.Subject.PrincipalId != Actor))
            throw new UnauthorizedAccessException("The original provider source handle changed.");
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (scope is not null) await scope.DisposeAsync();
        scope = null;
    }

    public override string ToString() => nameof(ProviderConfigurationMutation);
}
