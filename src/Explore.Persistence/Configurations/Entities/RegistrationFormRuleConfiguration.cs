using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormRuleConfiguration : IEntityTypeConfiguration<RegistrationFormRule>
{
    public void Configure(EntityTypeBuilder<RegistrationFormRule> builder)
    {
        ValueConverter<FormCondition, string> converter = new(
            condition => FormConditionJson.Serialize(condition),
            json => FormConditionJson.Deserialize(json));
        ValueComparer<FormCondition> comparer = new(
            (left, right) => left == null ? right == null :
                right != null && FormConditionJson.Serialize(left) == FormConditionJson.Serialize(right),
            condition => FormConditionJson.Serialize(condition).GetHashCode(StringComparison.Ordinal),
            condition => FormConditionJson.Deserialize(FormConditionJson.Serialize(condition)));

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_form_rules_ordinal_positive", "ordinal > 0");
            table.HasCheckConstraint("ck_registration_form_rules_effect", "effect BETWEEN 1 AND 4");
        });
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.TargetNamespace).IsRequired().HasMaxLength(100);
        builder.Property(rule => rule.TargetKey).IsRequired().HasMaxLength(100);
        builder.Ignore(rule => rule.Target);
        builder.Property(rule => rule.Condition).HasConversion(converter, comparer).IsRequired();
        builder.Property(rule => rule.CreatedAt).IsRequired();
        builder.Property(rule => rule.IsDeleted).HasDefaultValue(false);
        builder.Property(rule => rule.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(rule => new
        {
            rule.TenantId,
            rule.EventId,
            rule.RegistrationFormId,
            rule.RegistrationFormVersionId,
            rule.Id
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(rule => rule.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormVersion>().WithMany(version => version.Rules)
            .HasForeignKey(rule => new
            {
                rule.TenantId,
                rule.EventId,
                rule.RegistrationFormId,
                rule.RegistrationFormVersionId
            })
            .HasPrincipalKey(version => new
            {
                version.TenantId,
                version.EventId,
                version.RegistrationFormId,
                version.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(rule => new
        {
            rule.TenantId,
            rule.EventId,
            rule.RegistrationFormVersionId,
            rule.Ordinal
        }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(rule => new
        {
            rule.TenantId,
            rule.EventId,
            rule.RegistrationFormVersionId,
            rule.TargetNamespace,
            rule.TargetKey
        });
    }

    private static class FormConditionJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            AllowOutOfOrderMetadataProperties = true
        };

        public static string Serialize(FormCondition condition) => JsonSerializer.Serialize(condition, Options);

        public static FormCondition Deserialize(string json) =>
            JsonSerializer.Deserialize<FormCondition>(json, Options) ??
            throw new InvalidOperationException("A persisted form condition cannot be null.");
    }
}
