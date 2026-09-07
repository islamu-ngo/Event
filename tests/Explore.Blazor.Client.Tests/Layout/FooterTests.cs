using System.Reflection;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Layout;

public sealed class FooterTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task StructuredPublicShellIdentity_RendersRoleSeparatedOperatorsAndLegalLinks_WithoutProseDisclaimer()
    {
        PropertyInfo? directoryProperty = typeof(PublicExperienceShellDto).GetProperty("DirectoryOperator");
        PropertyInfo? instanceProperty = typeof(PublicExperienceShellDto).GetProperty("InstanceOperator");
        await Assert.That(directoryProperty).IsNotNull();
        await Assert.That(instanceProperty).IsNotNull();
        if (directoryProperty is null || instanceProperty is null) return;

        var shell = new PublicExperienceShellDto();
        SetOperator(shell, directoryProperty, new Dictionary<string, object?>
        {
            ["DocumentRevision"] = Guid.Parse("77777777-7777-7777-7777-777777777777"),
            ["PublicName"] = "Community Directory",
            ["LegalName"] = "Community Directory Foundation",
            ["OperatorKindCode"] = "NONPROFIT",
            ["JurisdictionCountryCode"] = "DE",
            ["RegistrationIdentifier"] = "VR 12345",
            ["PublicContactEmail"] = "support@directory.example",
            ["LegalNoticeUrl"] = "https://directory.example/legal",
            ["TermsUrl"] = "https://directory.example/terms",
            ["PrivacyUrl"] = "https://directory.example/privacy"
        });
        SetOperator(shell, instanceProperty, new Dictionary<string, object?>
        {
            ["OperatorId"] = Guid.Parse("88888888-8888-8888-8888-888888888888"),
            ["PublicName"] = "Platform Operations",
            ["LegalName"] = "Platform Operations GmbH",
            ["IsOfficialInstance"] = true,
            ["OfficialOrigin"] = "https://events.example",
            ["OperatorKindCode"] = "COMPANY",
            ["JurisdictionCountryCode"] = "DE",
            ["RegistrationIdentifier"] = "HRB 98765",
            ["PublicContactEmail"] = "contact@events.example",
            ["WebsiteUrl"] = "https://events.example/about",
            ["LegalNoticeUrl"] = "https://events.example/legal",
            ["TermsUrl"] = "https://events.example/terms",
            ["PrivacyUrl"] = "https://events.example/privacy"
        });
        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            BrandDisplayName = "White-label Events"
        });
        publicExperience.GetCachedShellAsync().Returns(shell);
        _ctx.Services.AddSingleton(publicExperience);
        _ctx.Services.AddSingleton(new CookieConsentStateService());

        var cut = _ctx.RenderMudComponent<Footer>();

        IReadOnlyList<AngleSharp.Dom.IElement> directories = cut.FindAll("[data-testid='footer-directory-operator']");
        IReadOnlyList<AngleSharp.Dom.IElement> instances = cut.FindAll("[data-testid='footer-instance-operator']");
        await Assert.That(directories.Count).IsEqualTo(1);
        await Assert.That(instances.Count).IsEqualTo(1);
        if (directories.Count != 1 || instances.Count != 1) return;
        AngleSharp.Dom.IElement directory = directories[0];
        AngleSharp.Dom.IElement instance = instances[0];
        await Assert.That(directory.TextContent).Contains("Directory operator");
        await Assert.That(directory.TextContent).Contains("Community Directory");
        await Assert.That(directory.TextContent).Contains("Community Directory Foundation");
        await Assert.That(instance.TextContent).Contains("Platform operator");
        await Assert.That(instance.TextContent).Contains("Platform Operations GmbH");
        await Assert.That(cut.FindAll("bdi[dir='auto']").Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(cut.FindAll("[dir='ltr'][lang='en']").Count).IsGreaterThanOrEqualTo(4);
        await Assert.That(directory.QuerySelector("[data-testid='footer-directory-registration-identifier']")?.TextContent).Contains("VR 12345");
        await Assert.That(instance.QuerySelector("[data-testid='footer-instance-registration-identifier']")?.TextContent).Contains("HRB 98765");
        await Assert.That(directory.QuerySelector("a[href='https://directory.example/legal']")).IsNotNull();
        await Assert.That(directory.QuerySelector("a[href='https://directory.example/terms']")).IsNotNull();
        await Assert.That(directory.QuerySelector("a[href='https://directory.example/privacy']")).IsNotNull();
        await Assert.That(instance.QuerySelector("a[href='https://events.example/legal']")).IsNotNull();
        await Assert.That(instance.QuerySelector("a[href='https://events.example/privacy']")).IsNotNull();
        await Assert.That(cut.FindAll("[data-testid='footer-paid-event-directory-disclaimer']").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).DoesNotContain("provides an event discovery and management directory only");
        await Assert.That(typeof(PublicExperienceSettingsDto).GetProperty("PaidEventDirectoryDisclaimer"))
            .IsNull();
    }

    [Test]
    public async Task MissingShellOrEitherMandatoryIdentity_RendersOnlyAccessibleFailClosedFooter()
    {
        foreach (int missing in new[] { 0, 1, 2 })
        {
            using var context = new BlazorTestContext();
            var service = Substitute.For<IPublicExperienceService>();
            service.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto { BrandDisplayName = "Hidden brand" });
            PublicExperienceShellDto? shell = missing == 0 ? null : new PublicExperienceShellDto();
            if (shell is not null)
            {
                PropertyInfo directory = typeof(PublicExperienceShellDto).GetProperty("DirectoryOperator")!;
                PropertyInfo instance = typeof(PublicExperienceShellDto).GetProperty("InstanceOperator")!;
                if (missing != 1) SetOperator(shell, directory, new Dictionary<string, object?> { ["PublicName"] = "Directory", ["LegalName"] = "Directory legal" });
                if (missing != 2) SetOperator(shell, instance, new Dictionary<string, object?> { ["PublicName"] = "Instance", ["LegalName"] = "Instance legal" });
            }
            service.GetCachedShellAsync().Returns(shell);
            context.Services.AddSingleton(service);
            context.Services.AddSingleton(new CookieConsentStateService());

            var cut = context.RenderMudComponent<Footer>();

            await Assert.That(cut.FindAll("[data-testid='footer-identity-unavailable'][role]").Count).IsEqualTo(0);
            await Assert.That(cut.FindAll("[data-testid='footer-identity-unavailable'] [role='status'][aria-live='polite']")).HasSingleItem();
            await Assert.That(cut.Markup).DoesNotContain("Hidden brand");
        }
    }

    private static void SetOperator(object owner, PropertyInfo ownerProperty, IReadOnlyDictionary<string, object?> values)
    {
        Type operatorType = Nullable.GetUnderlyingType(ownerProperty.PropertyType) ?? ownerProperty.PropertyType;
        object operatorValue = Activator.CreateInstance(operatorType)
            ?? throw new InvalidOperationException($"Could not create {operatorType.Name}.");
        foreach ((string name, object? value) in values)
        {
            PropertyInfo property = operatorType.GetProperty(name)
                ?? throw new InvalidOperationException($"{operatorType.Name} does not expose {name}.");
            property.SetValue(operatorValue, value);
        }
        ownerProperty.SetValue(owner, operatorValue);
    }

    public void Dispose() => _ctx.Dispose();
}
