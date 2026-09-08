using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailDispatchPointerTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task FromOutboxCopiesOnlyPointerFields()
    {
        var dispatch = new EmailDispatchOutbox
        {
            TenantId = Guid.CreateVersion7(),
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.RegistrationConfirmation,
            SourceType = "event-registration",
            SourceId = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            RegistrationOrderId = Guid.CreateVersion7(),
            RecipientUserId = Guid.CreateVersion7(),
            RecipientEmail = "person@example.test",
            Subject = "Registration confirmed",
            PlainTextBody = "body text",
            HtmlBody = "<p>body</p>"
        };

        EmailDispatchPointer pointer = EmailDispatchPointer.FromOutbox(dispatch);
        string json = JsonSerializer.Serialize(pointer, SerializerOptions);

        await Assert.That(json).Contains(dispatch.PublishEventId.ToString());
        await Assert.That(json).DoesNotContain(dispatch.RecipientEmail);
        await Assert.That(json).DoesNotContain(dispatch.Subject);
        await Assert.That(json).DoesNotContain(dispatch.PlainTextBody);
        await Assert.That(json).DoesNotContain(dispatch.HtmlBody);
        await Assert.That(json).DoesNotContain("userId");
        await Assert.That(json).DoesNotContain("recipientUserId");
        await Assert.That(json).DoesNotContain("recipient");
        await Assert.That(json).DoesNotContain("body");
        await Assert.That(json).DoesNotContain("subject");
    }
}
