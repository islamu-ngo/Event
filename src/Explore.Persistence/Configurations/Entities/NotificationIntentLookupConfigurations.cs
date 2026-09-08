using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class NotificationCategoryConfiguration : IEntityTypeConfiguration<NotificationCategory>
{
    public void Configure(EntityTypeBuilder<NotificationCategory> builder)
    {
        ConfigureLookup(builder);
    }

    private static void ConfigureLookup<TLookup>(EntityTypeBuilder<TLookup> builder)
        where TLookup : class
    {
        builder.Property<int>("Id").ValueGeneratedNever();
        builder.Property<string>("MasterCode").IsRequired().HasMaxLength(100);
        builder.Property<string>("FullName").IsRequired().HasMaxLength(200);
        builder.Property<string?>("Description").HasMaxLength(500);
        builder.HasIndex("MasterCode").IsUnique();
    }
}

public sealed class NotificationOwnershipTypeConfiguration : IEntityTypeConfiguration<NotificationOwnershipType>
{
    public void Configure(EntityTypeBuilder<NotificationOwnershipType> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationIntentStatusConfiguration : IEntityTypeConfiguration<NotificationIntentStatus>
{
    public void Configure(EntityTypeBuilder<NotificationIntentStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationRecipientKindConfiguration : IEntityTypeConfiguration<NotificationRecipientKind>
{
    public void Configure(EntityTypeBuilder<NotificationRecipientKind> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationDeliveryStatusConfiguration : IEntityTypeConfiguration<NotificationDeliveryStatus>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationDeliveryPolicyConfiguration : IEntityTypeConfiguration<NotificationDeliveryPolicy>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryPolicy> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationExternalDelegationStatusConfiguration : IEntityTypeConfiguration<NotificationExternalDelegationStatus>
{
    public void Configure(EntityTypeBuilder<NotificationExternalDelegationStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class ExternalWorkflowProviderKindLookupConfiguration : IEntityTypeConfiguration<ExternalWorkflowProviderKindLookup>
{
    public void Configure(EntityTypeBuilder<ExternalWorkflowProviderKindLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class AccountAuthorityKindLookupConfiguration : IEntityTypeConfiguration<AccountAuthorityKindLookup>
{
    public void Configure(EntityTypeBuilder<AccountAuthorityKindLookup> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}
