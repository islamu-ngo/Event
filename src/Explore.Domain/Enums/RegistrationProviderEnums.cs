namespace Explore.Domain.Enums;

public enum RegistrationProviderKindEnum { Native = 1, ExternalForm = 2, ExternalApi = 3 }
public enum RegistrationProviderDeploymentKindEnum { HostedSaas = 1, SelfHosted = 2, Native = 3 }
public enum RegistrationProviderSchemaAuthorityEnum { PlatformGenerated = 1, ProviderDiscovered = 2, OperatorEntered = 3 }
public enum RegistrationProviderPresentationModeEnum { Redirect = 1, Embed = 2, Manual = 3 }
public enum RegistrationProviderCollectionModeEnum { Native = 1, ProviderHosted = 2, ProviderApi = 3, MirrorOnly = 4 }
public enum RegistrationProviderCompletionModeEnum { Callback = 1, Polling = 2, Manual = 3 }
public enum RegistrationProviderTrustLevelEnum { Untrusted = 1, CompletionOnly = 2, SelectedFields = 3, FullCanonical = 4 }
public enum RegistrationProviderDriftClassEnum { NoDrift = 1, AdditiveOptionalChange = 2, LabelOnlyChange = 3, MappingRequired = 4, RequiredFieldRemoved = 5, TypeChanged = 6, OptionSetChanged = 7, UnsupportedChange = 8 }
public enum RegistrationProviderBindingStateEnum { Draft = 1, Published = 2, Disabled = 3, DriftBlocked = 4 }
