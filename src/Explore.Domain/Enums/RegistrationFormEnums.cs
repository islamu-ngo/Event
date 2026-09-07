namespace Explore.Domain.Enums;

public enum RegistrationFormStatusEnum
{
    Draft = 1,
    Published = 2,
    Retired = 3
}

public enum RegistrationFieldTypeEnum
{
    ShortText = 1,
    LongText = 2,
    Integer = 3,
    Decimal = 4,
    Boolean = 5,
    Date = 6,
    Time = 7,
    Instant = 8,
    Email = 9,
    Phone = 10,
    Url = 11,
    CountryCode = 12,
    LanguageTag = 13,
    SingleChoice = 14,
    MultipleChoice = 15,
    Rating = 16,
    Consent = 17,
    File = 18,
    OpaqueExternal = 19
}

public enum RegistrationOrganizerVisibilityEnum
{
    Hidden = 1,
    AuthorizedOrganizers = 2
}

public enum RegistrationFormVersionSourceKindEnum
{
    Authored = 1,
    TemplateClone = 2,
    ExternalImported = 3
}
