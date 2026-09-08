using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormStatusConfiguration : LookupConfiguration<RegistrationFormStatus>
{
    protected override string TableName => "registration_form_statuses";
}

public sealed class RegistrationFormVersionSourceKindConfiguration : LookupConfiguration<RegistrationFormVersionSourceKind>
{
    protected override string TableName => "registration_form_version_source_kinds";
}

public sealed class RegistrationFieldTypeConfiguration : LookupConfiguration<RegistrationFieldType>
{
    protected override string TableName => "registration_field_types";
}

public sealed class RegistrationOrganizerVisibilityConfiguration : LookupConfiguration<RegistrationOrganizerVisibility>
{
    protected override string TableName => "registration_organizer_visibilities";
}

public sealed class RegistrationRetentionPolicyConfiguration : LookupConfiguration<RegistrationRetentionPolicy>
{
    protected override string TableName => "registration_retention_policies";

    public override void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<RegistrationRetentionPolicy> builder)
    {
        base.Configure(builder);
        builder.Property(policy => policy.DurationDays);
        builder.Property(policy => policy.IsLegalHold).HasDefaultValue(false);
    }
}

public sealed class ContactShareConsentSubjectTypeConfiguration : LookupConfiguration<ContactShareConsentSubjectType>
{
    protected override string TableName => "contact_share_consent_subject_types";
}
