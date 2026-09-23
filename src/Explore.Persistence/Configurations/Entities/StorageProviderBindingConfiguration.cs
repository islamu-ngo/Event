using Explore.Domain;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class StorageProviderBindingConfiguration : IEntityTypeConfiguration<StorageProviderBinding>
{
    public void Configure(EntityTypeBuilder<StorageProviderBinding> builder)
    {
        builder.ToTable("storage_provider_bindings");
        builder.HasKey(binding => binding.Id);
        builder.Property(binding => binding.Id).ValueGeneratedNever();
        builder.Property(binding => binding.Provider).HasMaxLength(50).IsRequired();
        builder.Property(binding => binding.LocalRootPath).HasMaxLength(4096);
        builder.Property(binding => binding.Endpoint).HasMaxLength(2048);
        builder.Property(binding => binding.BucketName).HasMaxLength(255);
        builder.Property(binding => binding.Region).HasMaxLength(128);
        builder.OwnsOne(binding => binding.AccessKeyReference, ConfigureReference);
        builder.OwnsOne(binding => binding.SecretKeyReference, ConfigureReference);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    private static void ConfigureReference(OwnedNavigationBuilder<StorageProviderBinding, RetainedSecretReference> builder)
    {
        builder.Property(reference => reference.SettingKey).HasMaxLength(256).IsRequired();
        builder.Property(reference => reference.Qualifier).HasMaxLength(128).IsRequired();
        builder.Property(reference => reference.Authority).HasMaxLength(32).IsRequired();
        builder.Property(reference => reference.AuthorityEndpoint).HasMaxLength(2048);
        builder.Property(reference => reference.AuthorityProject).HasMaxLength(256);
        builder.Property(reference => reference.EnvironmentVariableName).HasMaxLength(256);
        builder.Property(reference => reference.InfisicalEnvironment).HasMaxLength(64);
        builder.Property(reference => reference.InfisicalPath).HasMaxLength(512);
        builder.Property(reference => reference.InfisicalKey).HasMaxLength(256);
        foreach (var property in builder.OwnedEntityType.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
