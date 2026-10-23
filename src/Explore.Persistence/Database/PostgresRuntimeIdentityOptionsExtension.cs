using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

/// <summary>
/// Retains only the declared Runtime login for exact policy admission. Connection
/// identity or the presence of a shared interceptor does not establish Runtime purpose.
/// </summary>
internal sealed class PostgresRuntimeIdentityOptionsExtension(string? declaredLogin) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    internal string? DeclaredLogin { get; } = declaredLogin;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) { }

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo(PostgresRuntimeIdentityOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => string.Empty;

        // The immutable identity belongs to each context's options, not to the
        // shared EF service provider or model. No services vary with this value.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) { }
    }
}
