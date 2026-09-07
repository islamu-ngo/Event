using Explore.Application.Contracts.Services;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class FormSchemaArtifactPublicationService(IFormSchemaArtifactGenerator generator)
{
    public void Publish(RegistrationFormVersion version, DateTime publishedAt)
    {
        ArgumentNullException.ThrowIfNull(version);
        FormSchemaArtifactBundle artifacts = generator.Generate(version);
        version.PinGeneratedSchemaBundle(artifacts.CanonicalBundleJson, publishedAt);
    }
}
