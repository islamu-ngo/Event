using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

/// <summary>Captures only the public coordinates used to compose this actual primary context.</summary>
internal sealed class PrimaryDatabasePublicIdentityOptionsExtension(
    PrimaryDatabaseProvider provider, string publicDigest) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? info;
    public PrimaryDatabaseProvider Provider { get; } = provider;
    public string PublicDigest { get; } = publicDigest;
    public DbContextOptionsExtensionInfo Info => info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
        if (!Enum.IsDefined(Provider) || PublicDigest.Length != 64
            || !PublicDigest.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidOperationException("Primary public database identity must originate from validated structured composition.");
    }

    private sealed class ExtensionInfo(PrimaryDatabasePublicIdentityOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        private new PrimaryDatabasePublicIdentityOptionsExtension Extension =>
            (PrimaryDatabasePublicIdentityOptionsExtension)base.Extension;
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "PrimaryPublicDatabaseIdentity=" + Extension.PublicDigest + " ";
        // Per-context facts install no services and must not create a provider per database identity.
        public override int GetServiceProviderHashCode() => 0;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Explore:PrimaryPublicDatabaseIdentity"] = Extension.PublicDigest;
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;
    }
}
