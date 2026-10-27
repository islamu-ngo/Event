using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Database;

/// <summary>Owns selected database material for a migration process or tooling invocation.</summary>
public sealed class MigrationDatabaseAcquisition : IAsyncDisposable
{
    private readonly SecretAuthorityAcquisition _owner;
    private readonly SecretAuthorityLease _lease;
    private readonly IConfigurationRoot _configuration;

    private MigrationDatabaseAcquisition(
        SecretAuthorityAcquisition owner, SecretAuthorityLease lease, IConfigurationRoot configuration)
    {
        _owner = owner;
        _lease = lease;
        _configuration = configuration;
    }

    public IConfiguration Configuration => _configuration;

    /// <summary>Preserves explicit null masks when overlaying lower-priority host configuration.</summary>
    public IConfigurationBuilder ApplyTo(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddInMemoryCollection(_configuration.AsEnumerable());
    }

    public static async ValueTask<MigrationDatabaseAcquisition> OpenAsync(
        IConfiguration bootstrap,
        string environmentName,
        IEnumerable<string> folders,
        IConfiguration? testingAuthority = null,
        CancellationToken cancellationToken = default)
    {
        SecretAuthoritySelection selection = SecretAuthoritySelection.Select(
            bootstrap, environmentName, SecretConsumerRole.Migration);
        string[] scope = folders.ToArray();
        var mappings = CreateMappings(selection.Provider, scope);
        SecretAcquisitionEntry[] entries = mappings.Select(mapping => mapping.Entry).ToArray();
        SecretAuthorityAcquisition owner = await SecretAuthorityBootstrap.OpenAsync(
            bootstrap, environmentName, SecretConsumerRole.Migration, entries,
            new SecretAuthorityLoginAttempt(), testingAuthority, cancellationToken).ConfigureAwait(false);
        SecretAuthorityLease? lease = null;
        try
        {
            var prepared = await owner.PrepareAvailableGroupAsync(entries, cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess)
                throw new InvalidOperationException("secret_database_acquisition_unavailable");
            lease = prepared.Value;
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in selection.Provider == SecretAuthorityKind.Infisical
                ? CreateMappings(SecretAuthorityKind.Environment, scope) : mappings)
            {
                values.TryAdd(mapping.Entry.Key, null);
                values.TryAdd(mapping.Destination, null);
            }
            foreach (var mapping in mappings)
            {
                // Mask source aliases as well: overlaying this projection must not revive
                // a value from appsettings, command-line arguments, or another authority.
                values.TryAdd(mapping.Entry.Key, null);
                string? value = lease.Configuration[mapping.Entry.ConfigurationKey];
                if (!values.TryGetValue(mapping.Destination, out string? existing) || existing is null)
                    values[mapping.Destination] = value;
            }
            cancellationToken.ThrowIfCancellationRequested();
            IConfigurationRoot configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            return new MigrationDatabaseAcquisition(owner, lease, configuration);
        }
        catch
        {
            lease?.Dispose();
            await owner.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The synchronous EF contract is bridged once here, never by a configuration provider.</summary>
    internal static TContext CreateDesignTimeContext<TContext>(
        IConfiguration bootstrap, string[] folders, Func<IConfiguration, TContext> create)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        return CreateAsync().GetAwaiter().GetResult();

        async Task<TContext> CreateAsync()
        {
            await using var acquisition = await OpenAsync(
                bootstrap, GetEnvironmentName(bootstrap), folders,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return create(acquisition.Configuration);
        }
    }

    internal static string GetEnvironmentName(IConfiguration configuration) =>
        configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";

    public async ValueTask DisposeAsync()
    {
        try
        {
            ((IDisposable)_configuration).Dispose();
            _lease.Dispose();
        }
        finally
        {
            await _owner.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override string ToString() => "migration_database_acquisition";

    private static List<(SecretAcquisitionEntry Entry, string Destination)> CreateMappings(
        SecretAuthorityKind provider, IEnumerable<string> folders)
    {
        var mappings = new List<(SecretAcquisitionEntry Entry, string Destination)>();
        foreach (string folder in folders.Distinct(StringComparer.Ordinal))
        {
            if (!SecretAcquisitionPolicy.AllowsRoot(SecretConsumerRole.Migration, folder))
                throw new InvalidOperationException("secret_consumer_scope_invalid");
            switch (folder)
            {
                case "/database":
                    foreach (string field in new[]
                    {
                        "Provider", "Host", "Port", "Name", "Database", "Schema", "TlsMode",
                        "TrustServerCertificate", "ServerFlavor", "ServerVersion", "Prefix"
                    })
                    {
                        string alias = FieldAlias(field);
                        Add(folder, $"Database:{field}", $"DATABASE_{alias}", alias);
                        if (provider != SecretAuthorityKind.Infisical && alias != field.ToUpperInvariant())
                            Map(folder, $"DATABASE_{field.ToUpperInvariant()}", $"Database:{field}");
                    }
                    foreach (PrimaryDatabaseRole role in Enum.GetValues<PrimaryDatabaseRole>())
                    {
                        string alias = role switch
                        {
                            PrimaryDatabaseRole.InstanceKeyRead => "INSTANCE_KEY_READ",
                            PrimaryDatabaseRole.InstanceKeyWrite => "INSTANCE_KEY_WRITE",
                            PrimaryDatabaseRole.InstanceKeyIssuance => "INSTANCE_KEY_ISSUANCE",
                            PrimaryDatabaseRole.PlatformKeyReport => "PLATFORM_KEY_REPORT",
                            PrimaryDatabaseRole.EmailPolicyInstanceMutation => "EMAIL_POLICY_INSTANCE_MUTATION",
                            PrimaryDatabaseRole.EmailPolicyTenantMutation => "EMAIL_POLICY_TENANT_MUTATION",
                            PrimaryDatabaseRole.LocalIdentityWriter => "LOCAL_IDENTITY_WRITER",
                            _ => role.ToString().ToUpperInvariant()
                        };
                        foreach (string field in new[]
                        {
                            "Username", "Password", "Provider", "Host", "Port", "Name", "Database",
                            "TlsMode", "TrustServerCertificate", "ServerFlavor", "ServerVersion", "Prefix"
                        })
                        {
                            string suffix = FieldAlias(field);
                            Add(folder, $"Database:{role}:{field}", $"DATABASE_{alias}_{suffix}", $"{alias}_{suffix}");
                            if (provider != SecretAuthorityKind.Infisical
                                && field is not ("Username" or "Password")
                                && $"{alias}_{suffix}" != $"{role.ToString().ToUpperInvariant()}_{field.ToUpperInvariant()}")
                                Map(folder, $"DATABASE_{role.ToString().ToUpperInvariant()}_{field.ToUpperInvariant()}",
                                    $"Database:{role}:{field}");
                        }
                    }
                    Add(folder, "IdentityDatabase:Topology", "IDENTITY_DATABASE_TOPOLOGY");
                    Add(folder, "PrivacyErasure:Authority:Topology", "ERASURE_DATABASE_TOPOLOGY");
                    break;
                case "/database/identity":
                    foreach (string field in new[]
                    {
                        "Topology", "Provider", "Host", "Port", "Name", "Schema", "TlsMode",
                        "TrustServerCertificate", "ServerVersion"
                    })
                    {
                        string alias = FieldAlias(field);
                        Add(folder, $"IdentityDatabase:{field}", $"IDENTITY_DATABASE_{alias}", alias);
                    }
                    foreach (string role in new[] { "Runtime", "Migrator", "LocalIdentityWriter" })
                        foreach (string field in new[] { "Username", "Password" })
                        {
                            string alias = role == "LocalIdentityWriter" ? "LOCAL_IDENTITY_WRITER" : role.ToUpperInvariant();
                            string suffix = field.ToUpperInvariant();
                            Add(folder, $"IdentityDatabase:{role}:{field}",
                                $"IDENTITY_DATABASE_{alias}_{suffix}", $"{alias}_{suffix}");
                        }
                    break;
                case "/database/erasure":
                    Add(folder, "PrivacyErasure:Authority:Topology", "ERASURE_DATABASE_TOPOLOGY");
                    foreach (string field in new[]
                    {
                        "Provider", "Host", "Port", "Database", "TlsMode", "TrustServerCertificate"
                    })
                    {
                        string alias = field == "Database" ? "NAME" : FieldAlias(field);
                        AddErasure($"Database:Erasure:{field}", $"ERASURE_DATABASE_{alias}",
                            $"PrivacyErasureAuthorityDatabase:{field}", $"DatabaseErasure:{field}",
                            $"PRIVACY_ERASURE_AUTHORITY_{FieldAlias(field)}");
                    }
                    foreach (string role in new[] { "Runtime", "Migrator" })
                        foreach (string field in new[] { "Username", "Password" })
                        {
                            string alias = $"{role.ToUpperInvariant()}_{field.ToUpperInvariant()}";
                            AddErasure($"Database:Erasure:{role}:{field}", $"ERASURE_DATABASE_{alias}",
                                $"PrivacyErasureAuthorityDatabase:{role}:{field}", $"DatabaseErasure:{role}:{field}",
                                $"PRIVACY_ERASURE_AUTHORITY_{alias}");
                        }
                    break;
                default:
                    throw new InvalidOperationException("secret_database_scope_invalid");
            }
        }
        return mappings;

        void Add(string folder, string destination, string key, string? shortKey = null)
        {
            if (provider != SecretAuthorityKind.Infisical)
                Map(folder, destination, destination);
            Map(folder, key, destination);
            if (provider == SecretAuthorityKind.Infisical && shortKey is not null)
                Map(folder, shortKey, destination);
        }

        void AddErasure(string destination, string key, params string[] localAliases)
        {
            if (provider != SecretAuthorityKind.Infisical)
            {
                Map("/database/erasure", destination, destination);
                Map("/database/erasure", localAliases[1], destination);
                Map("/database/erasure", localAliases[0], destination);
                if (destination == "Database:Erasure:Database")
                    Map("/database/erasure", "ERASURE_DATABASE_DATABASE", destination);
            }
            Map("/database/erasure", key, destination);
            if (provider != SecretAuthorityKind.Infisical)
                Map("/database/erasure", localAliases[2], destination);
        }

        void Map(string folder, string key, string destination) =>
            mappings.Add((new SecretAcquisitionEntry(folder, key, $"Acquired:{mappings.Count}"), destination));
    }

    private static string FieldAlias(string field) => field switch
    {
        "TlsMode" => "TLS_MODE",
        "TrustServerCertificate" => "TRUST_SERVER_CERTIFICATE",
        "ServerFlavor" => "SERVER_FLAVOR",
        "ServerVersion" => "SERVER_VERSION",
        _ => field.ToUpperInvariant()
    };
}
