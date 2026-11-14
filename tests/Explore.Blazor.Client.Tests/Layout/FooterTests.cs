using System.Reflection;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Tests.Layout;

public sealed class FooterTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RefreshRetainsOnlyTheSameTenantFooter(bool changeTenant)
    {
        var current = new FooterConfigDto
        {
            BrandDisplayName = "Current community",
            Settings = new FooterSettingsDto { Enabled = true, Template = "minimal", ShowCookieSettingsLink = true }
        };
        AddFooter(_ctx, Task.FromResult(current));
        var consent = new CookieConsentStateService();
        bool reopened = false;
        consent.OnReopenRequested += () =>
        {
            reopened = true;
            return Task.CompletedTask;
        };
        _ctx.Services.AddSingleton(consent);
        var cut = _ctx.RenderMudComponent<CascadingValue<TenantContext>>(parameters => parameters
            .Add(component => component.Name, "TenantContext")
            .Add(component => component.Value, new TenantContext { TenantId = Guid.CreateVersion7() })
            .AddChildContent<Footer>());
        await Assert.That(cut.Find("footer").TextContent).Contains("Current community");
        var pending = new TaskCompletionSource<FooterConfigDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ctx.Services.GetRequiredService<IFooterClient>()
            .GetFooterConfigAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return pending.Task;
            });
        var updated = current with { BrandDisplayName = "Updated community" };
        try
        {
            if (changeTenant)
                cut.Render(parameters => parameters
                    .Add(component => component.Name, "TenantContext")
                    .Add(component => component.Value, new TenantContext { TenantId = Guid.CreateVersion7() })
                    .AddChildContent<Footer>());
            else
                await cut.InvokeAsync(() => _ctx.Services.GetRequiredService<IFooterPresentationService>().ResetCache());
            await started.Task.WaitAsync(TUnit.Core.TestContext.Current!.Execution.CancellationToken);

            if (changeTenant)
                await Assert.That(cut.Find("footer").TextContent).DoesNotContain("Current community");
            else
                await Assert.That(cut.Find("footer").TextContent).Contains("Current community");
            await cut.Find("footer button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            await Assert.That(reopened).IsTrue();
            pending.SetResult(updated);
            cut.WaitForState(() => cut.Find("footer").TextContent.Contains("Updated community", StringComparison.Ordinal));
        }
        finally { pending.TrySetResult(updated); }
    }

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
        AddFooter(_ctx, Task.FromResult(new FooterConfigDto
        {
            BrandDisplayName = "White-label Events",
            DirectoryOperator = shell.DirectoryOperator,
            InstanceOperator = shell.InstanceOperator,
            Settings = new FooterSettingsDto { Enabled = true }
        }));
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
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task MissingIdentityOrDisabledOptionalTemplate_PreservesFooterAndCookieInteraction(int missing)
    {
        using var context = new BlazorTestContext();
        var footer = new FooterConfigDto
        {
            BrandDisplayName = "Safe display brand",
            Settings = new FooterSettingsDto { Enabled = missing != 4, ShowCookieSettingsLink = false }
        };
        PublicExperienceShellDto? shell = missing == 0 ? null : new PublicExperienceShellDto();
        if (shell is not null)
        {
            PropertyInfo directory = typeof(PublicExperienceShellDto).GetProperty("DirectoryOperator")!;
            PropertyInfo instance = typeof(PublicExperienceShellDto).GetProperty("InstanceOperator")!;
            if (missing is not (1 or 3)) SetOperator(shell, directory, new Dictionary<string, object?> { ["PublicName"] = "Directory", ["LegalName"] = "Directory legal" });
            if (missing is not (2 or 3)) SetOperator(shell, instance, new Dictionary<string, object?> { ["PublicName"] = "Instance", ["LegalName"] = "Instance legal" });
        }
        AddFooter(context, Task.FromResult(footer with
        {
            DirectoryOperator = shell?.DirectoryOperator,
            InstanceOperator = shell?.InstanceOperator
        }));
        var consent = new CookieConsentStateService();
        bool reopened = false;
        consent.OnReopenRequested += () =>
        {
            reopened = true;
            return Task.CompletedTask;
        };
        context.Services.AddSingleton(consent);

        var cut = context.RenderMudComponent<Footer>();

        await Assert.That(cut.FindAll("footer")).HasSingleItem();
        await cut.Find("footer button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Assert.That(reopened).IsTrue();
        if (missing < 4)
        {
            await Assert.That(cut.FindAll("[data-testid='footer-directory-operator']").Count)
                .IsEqualTo(missing == 2 ? 1 : 0);
            await Assert.That(cut.FindAll("[data-testid='footer-instance-operator']").Count)
                .IsEqualTo(missing == 1 ? 1 : 0);
        }
    }

    [Test]
    public async Task FailedFooterTransportPreservesCookieInteraction()
    {
        AddFooter(_ctx, Task.FromException<FooterConfigDto>(new HttpRequestException()));
        var consent = new CookieConsentStateService();
        bool reopened = false;
        consent.OnReopenRequested += () =>
        {
            reopened = true;
            return Task.CompletedTask;
        };
        _ctx.Services.AddSingleton(consent);
        var cut = _ctx.RenderMudComponent<Footer>();
        await Assert.That(cut.FindAll("footer")).HasSingleItem();
        await cut.Find("footer button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await Assert.That(reopened).IsTrue();
        await Assert.That(cut.FindAll("[data-testid='footer-directory-operator']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='footer-instance-operator']")).IsEmpty();
    }

    [Test]
    public async Task PendingPublicResource_KeepsMinimumFooterInteractive()
    {
        var pending = new TaskCompletionSource<FooterConfigDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        AddFooter(_ctx, pending.Task);
        var consent = new CookieConsentStateService();
        bool reopened = false;
        consent.OnReopenRequested += () =>
        {
            reopened = true;
            return Task.CompletedTask;
        };
        _ctx.Services.AddSingleton(consent);
        try
        {
            var cut = _ctx.RenderMudComponent<Footer>();
            await Assert.That(cut.FindAll("footer")).HasSingleItem();
            await cut.Find("footer button").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            await Assert.That(reopened).IsTrue();
        }
        finally { pending.TrySetResult(new FooterConfigDto()); }
    }

    private static void AddFooter(BlazorTestContext context, Task<FooterConfigDto> response)
    {
        var client = Substitute.For<IFooterClient>();
        client.GetFooterConfigAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(response);
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(TimeProvider.System);
        context.Services.AddScoped<IFooterPresentationService, FooterPresentationService>();
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
