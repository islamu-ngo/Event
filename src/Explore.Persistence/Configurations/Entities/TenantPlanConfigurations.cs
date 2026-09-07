using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class TenantPlanStatusConfiguration : IEntityTypeConfiguration<TenantPlanStatus>
{
    public void Configure(EntityTypeBuilder<TenantPlanStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class TenantPlanAssignmentStatusConfiguration : IEntityTypeConfiguration<TenantPlanAssignmentStatus>
{
    public void Configure(EntityTypeBuilder<TenantPlanAssignmentStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class TenantPlanApplicationStatusConfiguration : IEntityTypeConfiguration<TenantPlanApplicationStatus>
{
    public void Configure(EntityTypeBuilder<TenantPlanApplicationStatus> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class TenantPlanConfiguration : IEntityTypeConfiguration<TenantPlan>
{
    public void Configure(EntityTypeBuilder<TenantPlan> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.Key).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);

        builder.HasIndex(e => e.Key).IsUnique();
    }
}

public sealed class TenantPlanVersionConfiguration : IEntityTypeConfiguration<TenantPlanVersion>
{
    public void Configure(EntityTypeBuilder<TenantPlanVersion> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.PriceAmount).HasPrecision(18, 2);
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.BillingPeriod).HasMaxLength(50).IsRequired();

        builder.HasOne(e => e.TenantPlan)
            .WithMany(e => e.Versions)
            .HasForeignKey(e => e.TenantPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TenantPlanStatus)
            .WithMany()
            .HasForeignKey(e => e.TenantPlanStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantPlanId, e.VersionNumber }).IsUnique();
        builder.HasIndex(e => new { e.TenantPlanStatusId, e.IsActiveForProvisioning });
    }
}

public sealed class TenantPlanVersionSettingConfiguration : IEntityTypeConfiguration<TenantPlanVersionSetting>
{
    public void Configure(EntityTypeBuilder<TenantPlanVersionSetting> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.SettingKey).HasMaxLength(200).IsRequired();
        builder.Property(e => e.JsonValue).HasColumnType("jsonb").IsRequired();

        builder.HasOne(e => e.TenantPlanVersion)
            .WithMany(e => e.Settings)
            .HasForeignKey(e => e.TenantPlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantPlanVersionId, e.SettingKey }).IsUnique();
    }
}

public sealed class TenantPlanVersionQuotaConfiguration : IEntityTypeConfiguration<TenantPlanVersionQuota>
{
    public void Configure(EntityTypeBuilder<TenantPlanVersionQuota> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.QuotaKey).HasMaxLength(128).IsRequired();

        builder.HasOne(e => e.TenantPlanVersion)
            .WithMany(e => e.Quotas)
            .HasForeignKey(e => e.TenantPlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantPlanVersionId, e.QuotaKey }).IsUnique();
    }
}

public sealed class TenantPlanAssignmentConfiguration : IEntityTypeConfiguration<TenantPlanAssignment>
{
    public void Configure(EntityTypeBuilder<TenantPlanAssignment> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.AssignedAt).IsRequired();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TenantPlan)
            .WithMany(e => e.Assignments)
            .HasForeignKey(e => e.TenantPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TenantPlanVersion)
            .WithMany(e => e.Assignments)
            .HasForeignKey(e => e.TenantPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TenantPlanAssignmentStatus)
            .WithMany()
            .HasForeignKey(e => e.TenantPlanAssignmentStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("ux_tenant_plan_assignments_active_tenant")
            .IsUnique()
            .HasFilter($"tenant_plan_assignment_status_id = {(int)TenantPlanAssignmentStatusEnum.Active}");

        builder.HasIndex(e => new { e.TenantPlanVersionId, e.TenantPlanAssignmentStatusId });
    }
}

public sealed class TenantPlanApplicationLogConfiguration : IEntityTypeConfiguration<TenantPlanApplicationLog>
{
    public void Configure(EntityTypeBuilder<TenantPlanApplicationLog> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.AppliedAt).IsRequired();
        builder.Property(e => e.ChangedSettingKeysJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.ChangedQuotaKeysJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.FailureReason).HasMaxLength(1000);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.TenantPlan)
            .WithMany()
            .HasForeignKey(e => e.TenantPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TenantPlanVersion)
            .WithMany()
            .HasForeignKey(e => e.TenantPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.PreviousTenantPlanVersion)
            .WithMany()
            .HasForeignKey(e => e.PreviousTenantPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.TenantPlanAssignment)
            .WithMany(e => e.ApplicationLogs)
            .HasForeignKey(e => e.TenantPlanAssignmentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(e => e.TenantPlanApplicationStatus)
            .WithMany()
            .HasForeignKey(e => e.TenantPlanApplicationStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.AppliedAt })
            .IsDescending(false, true);
        builder.HasIndex(e => e.AppliedByUserId);
    }
}
