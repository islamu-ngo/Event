namespace Explore.Application.DTOs.RegistrationForms;

public sealed record AttachRegistrationRequirementInputDto(
    Guid WorkflowId,
    bool StandaloneQuestionnaire,
    Guid? RegistrationFormId,
    Guid? RegistrationFormVersionId);

public sealed record OptionalQuestionnaireDto(
    Guid EventId,
    Guid RegistrationWorkflowId,
    Guid RegistrationRequirementId,
    Guid RegistrationFormId,
    Guid RegistrationFormVersionId,
    int Version,
    string LanguageTag,
    string SchemaHash,
    string DataSchemaArtifact,
    string UiSchemaArtifact,
    string LogicSchemaArtifact,
    string MappingArtifact,
    Guid ConcurrencyStamp);
