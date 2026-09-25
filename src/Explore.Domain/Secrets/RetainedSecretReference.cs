using Explore.Domain.Enums;

namespace Explore.Domain.Secrets;

/// <summary>Immutable external reference, not a secret value or an owned credential.</summary>
public sealed class RetainedSecretReference
{
    private RetainedSecretReference() { }

    public Guid? BindingId { get; private set; }
    public string SettingKey { get; private set; } = null!;
    public SecretScope Scope { get; private set; }
    public Guid? ScopeId { get; private set; }
    public string Qualifier { get; private set; } = string.Empty;
    public SecretSourceType SourceType { get; private set; }
    public string Authority { get; private set; } = null!;
    public string? AuthorityEndpoint { get; private set; }
    public string? AuthorityProject { get; private set; }
    public string? EnvironmentVariableName { get; private set; }
    public string? InfisicalEnvironment { get; private set; }
    public string? InfisicalPath { get; private set; }
    public string? InfisicalKey { get; private set; }

    public static RetainedSecretReference Capture(
        SecretBinding binding, string authority, string? authorityEndpoint = null, string? authorityProject = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        return new RetainedSecretReference
        {
            BindingId = binding.Id == Guid.Empty ? null : binding.Id,
            SettingKey = binding.SettingKey, Scope = binding.Scope, ScopeId = binding.ScopeId,
            Qualifier = binding.Qualifier, SourceType = binding.SourceType, Authority = authority,
            AuthorityEndpoint = authorityEndpoint, AuthorityProject = authorityProject,
            EnvironmentVariableName = binding.EnvironmentVariableName,
            InfisicalEnvironment = binding.InfisicalEnvironment, InfisicalPath = binding.InfisicalPath,
            InfisicalKey = binding.InfisicalKey
        };
    }

    public SecretBinding ToBinding()
    {
        var binding = SourceType switch
        {
            SecretSourceType.Infisical => SecretBinding.CreateInfisical(
                SettingKey, Scope, ScopeId, InfisicalEnvironment!, InfisicalPath!, InfisicalKey!, qualifier: Qualifier),
            SecretSourceType.EnvironmentVariable => SecretBinding.CreateEnvironmentVariable(
                SettingKey, Scope, ScopeId, EnvironmentVariableName!, qualifier: Qualifier),
            _ => throw new InvalidOperationException("secret_source_invalid")
        };
        binding.Id = BindingId ?? Guid.Empty;
        return binding;
    }
}
