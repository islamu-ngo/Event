using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationCaseNarrativeContractTests
{
    [Test]
    public async Task SubmissionOwnsItsMaterialAndNeverFormatsConfidentialText()
    {
        var bytes = Encoding.UTF8.GetBytes("Confidential material.");
        var command = new SubmitModerationAllegationCommand
        {
            OperationId = Guid.CreateVersion7(),
            ReceiptId = Guid.CreateVersion7(),
            PresentedToken = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            Narrative = bytes
        };
        bytes.AsSpan().Clear();
        await Assert.That(Encoding.UTF8.GetString(command.Narrative.Span)).IsEqualTo("Confidential material.");
        await Assert.That(command.ToString()).DoesNotContain("Confidential material.");
        await Assert.That((await new SubmitModerationAllegationCommandValidator().ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That((await new SubmitModerationAllegationCommandValidator().ValidateAsync(
            command with { Narrative = default })).IsValid).IsFalse();
    }

    [Test]
    public async Task ReadHandoffOwnsAndClearsPlaintextWhenDisposed()
    {
        var input = Encoding.UTF8.GetBytes("Protected read.");
        using var disclosure = new ModerationCaseNarrativeDisclosure(input);
        var borrowed = disclosure.Material;
        input.AsSpan().Clear();
        await Assert.That(Encoding.UTF8.GetString(borrowed.Span)).IsEqualTo("Protected read.");
        await Assert.That(disclosure.ToString()).DoesNotContain("Protected read.");
        disclosure.Dispose();
        await Assert.That(borrowed.Span.ToArray().All(value => value == 0)).IsTrue();
        await Assert.That(disclosure.Material.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task InvestigationCannotReplaceAnyRequiredSubstantiveGroundWithAnEmptyAssertion(int absent)
    {
        var grounds = new ModerationParticipantInvestigationGrounds(
            absent == 0 ? " " : "Identification source.",
            absent == 1 ? " " : "Specific alleged conduct.",
            absent == 2 ? " " : "Authenticated source provenance.",
            absent == 3 ? " " : "Contradiction and credential-use assessment.");
        await Assert.That(() => grounds.Encode(4096)).Throws<ArgumentException>();
    }

    [Test]
    public async Task InvestigationEncodingPreservesAllMachineConsumedGroundsAndObeysTheAdoptedByteLimit()
    {
        var grounds = new ModerationParticipantInvestigationGrounds(
            "Identification source.", "Specific alleged conduct.",
            "Authenticated source provenance.", "Contradiction and credential-use assessment.");
        var material = grounds.Encode(4096);
        try
        {
            using var parsed = System.Text.Json.JsonDocument.Parse(material);
            await Assert.That(parsed.RootElement.GetProperty(nameof(grounds.IdentificationGrounds)).GetString())
                .IsEqualTo(grounds.IdentificationGrounds);
            await Assert.That(parsed.RootElement.GetProperty(nameof(grounds.AllegedConduct)).GetString())
                .IsEqualTo(grounds.AllegedConduct);
            await Assert.That(parsed.RootElement.GetProperty(nameof(grounds.EvidenceProvenance)).GetString())
                .IsEqualTo(grounds.EvidenceProvenance);
            await Assert.That(parsed.RootElement.GetProperty(nameof(grounds.ContradictionsAndCredentialMisuse)).GetString())
                .IsEqualTo(grounds.ContradictionsAndCredentialMisuse);
            await Assert.That(() => grounds.Encode(material.Length - 1)).Throws<ArgumentException>();
            var exact = grounds.Encode(material.Length);
            try { await Assert.That(exact.SequenceEqual(material)).IsTrue(); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(exact); }
            await Assert.That(grounds.ToString()).DoesNotContain(grounds.IdentificationGrounds);
            await Assert.That(grounds.ToString()).DoesNotContain(grounds.ContradictionsAndCredentialMisuse);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(material); }
    }
}
