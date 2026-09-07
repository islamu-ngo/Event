using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.EventSessionLanguages.Commands;

public class EventSessionLanguageCommandSecurityTests
{
    [Test]
    public async Task CreateCommand_UsesSessionIdAndRequiresPersistedAttributeEnrichment()
    {
        var eventSessionId = Guid.NewGuid();
        ISecureRequest command = new CreateEventSessionLanguageCommand
        {
            EventSessionLanguageDto = new CreateEventSessionLanguageDto
            {
                EventSessionId = eventSessionId,
                LanguageId = 1
            }
        };

        await Assert.That(command.ResourceId).IsEqualTo(eventSessionId.ToString());
        await Assert.That(command.AuthorizationFacts).IsNull();
    }

    [Test]
    public async Task DeleteCommand_UsesParentSessionAndRequiresPersistedAttributeEnrichment()
    {
        var eventSessionId = Guid.NewGuid();
        ISecureRequest command = new DeleteEventSessionLanguageCommand
        {
            Id = 42,
            EventSessionId = eventSessionId
        };

        await Assert.That(command.ResourceId).IsEqualTo(eventSessionId.ToString());
        await Assert.That(command.AuthorizationFacts).IsNull();
    }

    [Test]
    public async Task UpdateCommand_UsesParentSessionAndRequiresPersistedAttributeEnrichment()
    {
        var eventSessionId = Guid.NewGuid();
        ISecureRequest command = new UpdateEventSessionLanguageCommand
        {
            EventSessionLanguageId = 42,
            EventSessionId = eventSessionId,
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            EventSessionLanguageDto = new UpdateEventSessionLanguageDto
            {
                Language = new UpdateEventSessionLanguageLanguageDto { LanguageId = 2 }
            }
        };

        await Assert.That(command.ResourceId).IsEqualTo(eventSessionId.ToString());
        await Assert.That(command.AuthorizationFacts).IsNull();
    }
}
