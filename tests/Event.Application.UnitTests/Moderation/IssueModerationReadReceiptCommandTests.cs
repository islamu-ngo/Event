using System.Text.Json;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class IssueModerationReadReceiptCommandTests
{
    [Test]
    [Arguments("empty", false)]
    [Arguments("random", false)]
    [Arguments("invalid-variant", false)]
    [Arguments("grant", true)]
    public async Task Read_issuance_accepts_only_a_native_read_grant_identity(string identity, bool valid)
    {
        var bytes = Guid.CreateVersion7().ToByteArray(bigEndian: true);
        bytes[8] = 1;
        var grantId = identity switch
        {
            "empty" => Guid.Empty,
            "random" => Guid.NewGuid(),
            "invalid-variant" => new Guid(bytes, bigEndian: true),
            "grant" => Guid.CreateVersion7(),
            _ => throw new ArgumentOutOfRangeException(nameof(identity))
        };
        var command = new IssueModerationReadReceiptCommand { ReadGrantId = grantId };
        await Assert.That((await new IssueModerationReadReceiptCommandValidator()
            .ValidateAsync(command)).IsValid).IsEqualTo(valid);
    }

    [Test]
    public async Task Read_grant_lookup_survives_JSON_without_inventing_account_or_case_authority()
    {
        var command = new IssueModerationReadReceiptCommand { ReadGrantId = Guid.CreateVersion7() };
        var json = JsonSerializer.Serialize(command);
        var restored = JsonSerializer.Deserialize<IssueModerationReadReceiptCommand>(json);
        await Assert.That(restored).IsEqualTo(command);
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { nameof(command.ReadGrantId) });
    }

    [Test]
    public async Task Missing_committed_read_authority_exposes_neither_bearer_nor_expiry()
    {
        var response = ModerationReadReceiptResponse.FromCommitted(null);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("ModerationCaseIntakeUnavailable");
        await Assert.That(response.PresentedToken.Length).IsEqualTo(0);
        await Assert.That(response.ExpiresAt).IsNull();
    }

    [Test]
    public async Task Production_discovery_registers_one_scoped_independent_read_receipt_handler()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        var contract = typeof(ICommandHandler<IssueModerationReadReceiptCommand, ModerationReadReceiptResponse>);
        await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
        await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime)
            .IsEqualTo(ServiceLifetime.Scoped);
    }
}
