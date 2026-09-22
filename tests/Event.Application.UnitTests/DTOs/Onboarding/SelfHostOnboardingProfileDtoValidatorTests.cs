using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;

namespace Event.Application.UnitTests.DTOs.Onboarding;

public class SelfHostOnboardingProfileDtoValidatorTests
{
    private readonly SelfHostOnboardingProfileDtoValidator _validator = new();

    [Test]
    public async Task Validate_WithMinimalProfileDefaults_ReturnsValid()
    {
        var result = await _validator.ValidateAsync(new SelfHostOnboardingProfileDto
        {
            SiteName = "Community Events"
        });

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_WithoutSiteName_ReturnsInvalid()
    {
        var result = await _validator.ValidateAsync(new SelfHostOnboardingProfileDto());

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(SelfHostOnboardingProfileDto.SiteName))).IsTrue();
    }

    [Test]
    [Arguments("https://example.test:9443/community", true)]
    [Arguments("https://operator@example.test", false)]
    [Arguments("https://example.test/?token=value", false)]
    [Arguments("https://example.test/#fragment", false)]
    public async Task PublicUrl_RejectsNonOriginComponents(string url, bool valid)
    {
        var result = await _validator.ValidateAsync(new SelfHostOnboardingProfileDto
        {
            SiteName = "Community Events", CanonicalUrl = url
        });
        await Assert.That(result.IsValid).IsEqualTo(valid);
    }

    [Test]
    public async Task Validate_WithInvalidOptionalFields_ReturnsInvalid()
    {
        var result = await _validator.ValidateAsync(new SelfHostOnboardingProfileDto
        {
            SiteName = "Community Events",
            SupportEmail = "not-an-email",
            CanonicalUrl = "ftp://example.org",
            TimeZone = "Not/AZone"
        });

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(SelfHostOnboardingProfileDto.SupportEmail))).IsTrue();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(SelfHostOnboardingProfileDto.CanonicalUrl))).IsTrue();
        await Assert.That(result.Errors.Any(e => e.PropertyName == nameof(SelfHostOnboardingProfileDto.TimeZone))).IsTrue();
    }
}
