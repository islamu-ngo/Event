using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationProviderKindConfiguration : LookupConfiguration<RegistrationProviderKind> { protected override string TableName => "registration_provider_kinds"; }
public sealed class RegistrationProviderDeploymentKindConfiguration : LookupConfiguration<RegistrationProviderDeploymentKind> { protected override string TableName => "registration_provider_deployment_kinds"; }
public sealed class RegistrationProviderSchemaAuthorityConfiguration : LookupConfiguration<RegistrationProviderSchemaAuthority> { protected override string TableName => "registration_provider_schema_authorities"; }
public sealed class RegistrationProviderPresentationModeConfiguration : LookupConfiguration<RegistrationProviderPresentationMode> { protected override string TableName => "registration_provider_presentation_modes"; }
public sealed class RegistrationProviderCollectionModeConfiguration : LookupConfiguration<RegistrationProviderCollectionMode> { protected override string TableName => "registration_provider_collection_modes"; }
public sealed class RegistrationProviderCompletionModeConfiguration : LookupConfiguration<RegistrationProviderCompletionMode> { protected override string TableName => "registration_provider_completion_modes"; }
public sealed class RegistrationProviderTrustLevelConfiguration : LookupConfiguration<RegistrationProviderTrustLevel> { protected override string TableName => "registration_provider_trust_levels"; }
public sealed class RegistrationProviderDriftClassConfiguration : LookupConfiguration<RegistrationProviderDriftClass> { protected override string TableName => "registration_provider_drift_classes"; }
public sealed class RegistrationProviderBindingStateConfiguration : LookupConfiguration<RegistrationProviderBindingState> { protected override string TableName => "registration_provider_binding_states"; }
