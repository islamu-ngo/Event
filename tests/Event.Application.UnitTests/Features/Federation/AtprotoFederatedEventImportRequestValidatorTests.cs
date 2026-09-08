using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Validators;

namespace Event.Application.UnitTests.Features.Federation;

public sealed class AtprotoFederatedEventImportRequestValidatorTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Validate_MinimumLexiconRecord_Succeeds()
    {
        var validator = new AtprotoFederatedEventImportInputValidator();

        var result = await validator.ValidateAsync(ValidRequest());

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_MissingNameAndCreatedAt_FailsBothRequiredFields()
    {
        var validator = new AtprotoFederatedEventImportInputValidator();
        var request = ValidRequest() with
        {
            Name = " ",
            CreatedAt = null
        };

        var result = await validator.ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.Name))).IsTrue();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.CreatedAt))).IsTrue();
    }

    [Test]
    public async Task Validate_SafeHttpsSourceAndSupportedOptionalValues_Succeeds()
    {
        var validator = new AtprotoFederatedEventImportInputValidator();
        var request = ValidRequest() with
        {
            Description = "A public community event.",
            SourceUrl = "https://events.example.org/program/iftar",
            StartsAt = CreatedAt.AddDays(2),
            EndsAt = CreatedAt.AddDays(2).AddHours(2),
            Mode = "#hybrid",
            Status = "#rescheduled",
            RsvpExpected = true
        };

        var result = await validator.ValidateAsync(request);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_UnsafeSource_EndWithoutStart_AndUnsupportedTokens_FailsEverySuppliedValue()
    {
        var validator = new AtprotoFederatedEventImportInputValidator();
        var request = ValidRequest() with
        {
            SourceUrl = "http://127.0.0.1/private",
            StartsAt = null,
            EndsAt = CreatedAt.AddHours(1),
            Mode = "#eventVirtual",
            Status = "#unknown"
        };

        var result = await validator.ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.SourceUrl))).IsTrue();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.EndsAt))).IsTrue();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.Mode))).IsTrue();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.Status))).IsTrue();
    }

    [Test]
    public async Task Validate_EndNotAfterStart_FailsScheduleOrdering()
    {
        var validator = new AtprotoFederatedEventImportInputValidator();
        DateTimeOffset startsAt = CreatedAt.AddDays(1);
        var request = ValidRequest() with
        {
            StartsAt = startsAt,
            EndsAt = startsAt
        };

        var result = await validator.ValidateAsync(request);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(AtprotoFederatedEventImportInput.EndsAt))).IsTrue();
    }

    private static AtprotoFederatedEventImportInput ValidRequest() => new(
        Name: "Community iftar",
        CreatedAt: CreatedAt);
}
