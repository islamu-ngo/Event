using Explore.Domain;

namespace Explore.Application.Contracts.Services;

public interface IFormSchemaArtifactGenerator
{
    FormSchemaArtifactBundle Generate(RegistrationFormVersion version);
}

public sealed record FormSchemaArtifactBundle(
    string DataSchemaJson,
    string UiSchemaJson,
    string LogicSchemaJson,
    string MappingArtifactJson,
    string CanonicalBundleJson,
    string SchemaHash);
