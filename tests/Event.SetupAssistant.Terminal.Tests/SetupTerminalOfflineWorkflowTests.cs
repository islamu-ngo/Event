namespace ISLAMU.SetupAssistant.Terminal.Tests;

using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.SetupAssistant.Presentation;
using ISLAMU.Event.SetupAssistant.Terminal;
using ISLAMU.Event.SetupAssistant.Terminal.Views;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using global::Terminal.Gui.App;
using global::Terminal.Gui.Input;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

public sealed class SetupTerminalOfflineWorkflowTests
{
    [Test]
    public async Task IdentityInputIsMaskedAndCannotRestorePrivateHistory()
    {
        string marker = Guid.CreateVersion7().ToString("N");
        using var view = new IdentityDraftView(new ProtectedArtifactWriter(), Path.GetTempPath())
        {
            Document = IdentityDocument(marker)
        };
        TextField input = view.SubViews.OfType<TextField>().First();
        await Assert.That(input.Secret).IsTrue();
        await Assert.That(input.Text).DoesNotContain(marker);
        await Assert.That(view.ValidateDraft()).IsTrue();

        foreach (Command command in new[] { Command.Copy, Command.Cut, Command.Paste, Command.Undo, Command.Redo, Command.Context })
            await Assert.That(input.InvokeCommand(command)).IsTrue();
        view.ClearPrivateState();
        await Assert.That(input.InvokeCommand(Command.Undo)).IsTrue();
        await Assert.That(input.Text).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalFilesCanBeValidatedComparedAndPreparedForExport(bool tenant)
    {
        string directory = Path.Combine(Path.GetTempPath(), "setup-local-compare-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        try
        {
            ManifestWorkspaceKind kind = tenant ? ManifestWorkspaceKind.TenantPackage : ManifestWorkspaceKind.Manifest;
            using var source = new ManifestView(kind, new ProtectedArtifactWriter(), directory);
            await Assert.That(source.PreparePreview()).IsTrue();
            byte[] expected = source.GetPreviewBytes().ToArray();
            await File.WriteAllBytesAsync(Path.Combine(directory, "input.json"), expected);
            using var candidate = new ManifestView(kind, new ProtectedArtifactWriter(), directory)
            {
                InputFileName = "input.json",
                BaselineFileName = "input.json"
            };
            await Assert.That(candidate.OpenLocalFile()).IsTrue();
            await Assert.That(candidate.GetPreviewBytes().ToArray()).IsEquivalentTo(expected);
            await Assert.That(candidate.CompareLocalFiles()).IsTrue();
            await Assert.That(candidate.DifferenceCount).IsEqualTo(0);

            await File.WriteAllTextAsync(Path.Combine(directory, "invalid.json"), "invalid");
            candidate.InputFileName = "invalid.json";
            await Assert.That(candidate.OpenLocalFile()).IsFalse();
            await Assert.That(candidate.GetPreviewBytes()).IsEmpty();
            await Assert.That(candidate.DifferenceCount).IsNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [NotInParallel]
    public async Task WindowAdvertisesEveryOfflineWorkspaceWithoutAConnectedSurface()
    {
        using IApplication application = Application.Create();
        using var secret = new SetupTerminalSecretBuffer();
        var operation = new SetupTerminalArtifactOperation(
            () => "offline.env",
            secret,
            new ProtectedArtifactWriter(),
            Path.GetTempPath());
        using var session = new SetupPresentationSession(new StrongReferenceMessenger());
        await Assert.That(SetupWorkspaceId.TryCreate("environment", out SetupWorkspaceId id)).IsTrue();
        using SetupPresentationWorkspace workspace = session.CreateWorkspace(id, operation);
        workspace.Activate();
        using var window = new SetupTerminalWindow(
            application,
            workspace,
            operation,
            secret,
            protectedOutputAvailable: true);

        window.Frame = new Rectangle(0, 0, 100, 30);
        window.ApplyViewportPolicy(window.Frame.Size);
        string[] navigation = Descendants(window)
            .OfType<Button>()
            .Select(button => button.Text.ToString() ?? string.Empty)
            .ToArray();

        await Assert.That(navigation).Contains("_Environment");
        await Assert.That(navigation).Contains("_Catalogue");
        await Assert.That(navigation).Contains("_Manifest");
        await Assert.That(navigation).Contains("_Tenant package");
        await Assert.That(navigation).Contains("_Legal draft");
        await Assert.That(navigation).Contains("_Identity draft");
        await Assert.That(navigation.Any(text =>
            text.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || text.Contains("sign in", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(typeof(SetupTerminalWindow).Assembly.GetReferencedAssemblies()
            .Any(reference => reference.Name?.Contains("SetupLive", StringComparison.Ordinal) == true)).IsFalse();
        window.SelectWorkspace(SetupTerminalWorkspaceKind.IdentityDraft);
        IdentityDraftView identity = Descendants(window).OfType<IdentityDraftView>().Single();
        TextField input = identity.SubViews.OfType<TextField>().First();
        await Assert.That(input.HasFocus).IsTrue();
    }

    [Test]
    public async Task CatalogueWorkspaceProjectsDefinitionsWithoutEnvironmentValues()
    {
        using var view = new CatalogueView();

        view.ShowList();

        await Assert.That(view.Output).Contains("PUBLIC_BASE_URL", StringComparison.Ordinal);
        await Assert.That(view.Output).Contains("SETUP_SECRET", StringComparison.Ordinal);
        await Assert.That(view.Output).Contains("Secret", StringComparison.Ordinal);
        view.Key = "DATABASE_PROVIDER";
        view.ShowLookup();
        await Assert.That(view.Output).StartsWith("DATABASE_PROVIDER |", StringComparison.Ordinal);
        view.Key = "NOT_A_REAL_KEY";
        view.ShowLookup();
        await Assert.That(view.Output).IsEqualTo(SetupTerminalText.Get("CatalogueNotFound"));
    }

    [Test]
    public async Task ManifestAndTenantPreviewsMatchAuthoritativeCoreBytes()
    {
        var writer = new ProtectedArtifactWriter();
        using var manifestView = new ManifestView(
            ManifestWorkspaceKind.Manifest,
            writer,
            Path.GetTempPath());
        manifestView.SourceName = "terminal-manifest";
        using var tenantView = new ManifestView(
            ManifestWorkspaceKind.TenantPackage,
            writer,
            Path.GetTempPath());
        tenantView.SourceName = "terminal-package";
        tenantView.TenantName = "terminal-tenant";
        tenantView.DisplayName = "Terminal tenant";

        await Assert.That(manifestView.PreparePreview()).IsTrue();
        await Assert.That(tenantView.PreparePreview()).IsTrue();

        SetupProfile profile = Profile();
        SetupSelection manifestSelection = Selection(
            SetupScope.Instance,
            "instance.settings",
            "instance.documents",
            "instance.legal_documents");
        SetupSelection tenantSelection = Selection(
            SetupScope.Tenant,
            "tenant.settings",
            "tenant.documents",
            "tenant.legal_documents");
        OfflinePortabilityDocument manifest = OfflinePortabilityWorkflow.Validate(
            OfflinePortabilityWorkflow.CreateManifest(
                profile,
                manifestSelection,
                "terminal-manifest",
                null,
                null).Document!).Document!;
        OfflinePortabilityDocument tenantPackage = OfflinePortabilityWorkflow.Validate(
            OfflinePortabilityWorkflow.CreateTenantPackage(
                profile,
                tenantSelection,
                "terminal-package",
                "terminal-tenant",
                "Terminal tenant",
                null).Document!).Document!;

        await Assert.That(manifestView.GetPreviewBytes().ToArray())
            .IsEquivalentTo(OfflinePortabilityWorkflow.Format(manifest).Output!.Bytes.ToArray());
        await Assert.That(tenantView.GetPreviewBytes().ToArray())
            .IsEquivalentTo(OfflinePortabilityWorkflow.Format(tenantPackage).Output!.Bytes.ToArray());
    }

    [Test]
    public async Task EditingInputInvalidatesPreviouslyPreparedArtifacts()
    {
        using var manifest = new ManifestView(
            ManifestWorkspaceKind.Manifest, new ProtectedArtifactWriter(), Path.GetTempPath());
        await Assert.That(manifest.PreparePreview()).IsTrue();
        manifest.SourceName = "changed-source";
        await Assert.That(manifest.GetPreviewBytes()).IsEmpty();

        using var identity = new IdentityDraftView(new ProtectedArtifactWriter(), Path.GetTempPath())
        {
            Document = IdentityDocument("Original operator")
        };
        await Assert.That(identity.ValidateDraft()).IsTrue();
        identity.Document = "{}";
        await Assert.That(identity.GetManifestBytes()).IsEmpty();

        using var legal = new LegalDraftView
        {
            AccountableIdentity = "Original operator",
            Markdown = "# Terms\\n\\nOperator: {{accountable_identity}}."
        };
        await Assert.That(legal.PreparePreview()).IsTrue();
        legal.AccountableIdentity = string.Empty;
        await Assert.That(legal.Preview).IsEmpty();
    }

    [Test]
    public async Task LegalWorkspaceRequiresIdentityThenRendersCorePreview()
    {
        using var view = new LegalDraftView
        {
            AccountableIdentity = string.Empty,
            Markdown = "# Terms\n\nOperator: {{accountable_identity}}."
        };

        await Assert.That(view.PreparePreview()).IsFalse();
        await Assert.That(view.Preview).IsEmpty();

        view.AccountableIdentity = "Local operator";
        await Assert.That(view.PreparePreview()).IsTrue();
        await Assert.That(view.Preview).Contains("Local operator", StringComparison.Ordinal);
        await Assert.That(view.Preview).DoesNotContain(
            "{{accountable_identity}}",
            StringComparison.Ordinal);
    }

    [Test]
    public async Task IdentityWorkspaceKeepsPrivateValuesOutOfStatusAndUsesProtectedWriter()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "islamu-terminal-offline-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "identity.json");
        const string privateMarker = "Private operator";
        try
        {
            using var view = new IdentityDraftView(new ProtectedArtifactWriter(), directory)
            {
                Document = IdentityDocument(privateMarker),
                FileName = "identity.json"
            };

            await Assert.That(view.ValidateDraft()).IsTrue();
            await Assert.That(view.Status).DoesNotContain(privateMarker, StringComparison.Ordinal);
            OperatorIdentityManifest manifest =
                OperatorIdentityManifestCodec.Read(view.GetManifestBytes());
            await Assert.That(manifest.Document.GetProperty("legalName").GetString())
                .IsEqualTo(privateMarker);

            if (OperatingSystem.IsLinux())
            {
                await Assert.That(await view.SaveProtectedAsync())
                    .IsEqualTo(ProtectedArtifactStatus.Written);
                await Assert.That(await File.ReadAllBytesAsync(path))
                    .IsEquivalentTo(view.GetManifestBytes().ToArray());
                await Assert.That(File.GetUnixFileMode(path))
                    .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task NavigationClearsRestrictedInputAndRestoresMaskedEnvironmentWorkspace()
    {
        using IApplication application = Application.Create();
        using var secret = new SetupTerminalSecretBuffer();
        var operation = new SetupTerminalArtifactOperation(
            () => "offline.env",
            secret,
            new ProtectedArtifactWriter(),
            Path.GetTempPath());
        using var session = new SetupPresentationSession(new StrongReferenceMessenger());
        await Assert.That(SetupWorkspaceId.TryCreate("environment", out SetupWorkspaceId id)).IsTrue();
        using SetupPresentationWorkspace workspace = session.CreateWorkspace(id, operation);
        workspace.Activate();
        using var window = new SetupTerminalWindow(
            application,
            workspace,
            operation,
            secret,
            protectedOutputAvailable: true);
        window.Frame = new Rectangle(0, 0, 100, 30);
        window.ApplyViewportPolicy(window.Frame.Size);
        SetupSecretTextField secretField = window.SubViews.OfType<SetupSecretTextField>().Single();
        foreach (char character in Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))
            secretField.NewKeyDownEvent(new Key(character));
        await Assert.That(secret.Count).IsGreaterThan(0);

        window.SelectWorkspace(SetupTerminalWorkspaceKind.Catalogue);
        await Assert.That(secret.Count).IsEqualTo(0);
        await Assert.That(secretField.Text).IsEmpty();
        await Assert.That(secretField.Visible).IsFalse();

        window.SelectWorkspace(SetupTerminalWorkspaceKind.IdentityDraft);
        IdentityDraftView identity = Descendants(window).OfType<IdentityDraftView>().Single();
        identity.Document = IdentityDocument("Private switch value");
        window.SelectWorkspace(SetupTerminalWorkspaceKind.Manifest);
        await Assert.That(identity.Document).IsEmpty();
        ManifestView manifest = Descendants(window).OfType<ManifestView>().First();
        await Assert.That(manifest.PreparePreview()).IsTrue();
        window.SelectWorkspace(SetupTerminalWorkspaceKind.LegalDraft);
        await Assert.That(manifest.GetPreviewBytes()).IsEmpty();
        LegalDraftView legal = Descendants(window).OfType<LegalDraftView>().Single();
        legal.AccountableIdentity = "Private legal identity";
        await Assert.That(legal.PreparePreview()).IsTrue();
        window.SelectWorkspace(SetupTerminalWorkspaceKind.Catalogue);
        await Assert.That(legal.AccountableIdentity).IsEmpty();
        await Assert.That(legal.Preview).IsEmpty();

        window.SelectWorkspace(SetupTerminalWorkspaceKind.Environment);
        await Assert.That(secretField.Visible).IsTrue();
        await Assert.That(secretField.Secret).IsTrue();
    }

    private static IEnumerable<View> Descendants(View root)
    {
        foreach (View child in root.SubViews)
        {
            yield return child;
            foreach (View descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static SetupProfile Profile() => new(
        new SetupProfileIdentity("event-setup"),
        [],
        [new SetupTopologyKey("standalone")]);

    private static SetupSelection Selection(SetupScope scope, params string[] sections) => new(
        scope,
        ConfigurationImportApplyMode.PreviewOnly,
        sections.Select(section => new PortableSectionKey(section)));

    private static string IdentityDocument(string legalName) => JsonSerializer.Serialize(new
    {
        operatorId = Guid.CreateVersion7(),
        revision = Guid.CreateVersion7(),
        publicName = "Local operator",
        legalName,
        operatorKindCode = "registered_organization",
        jurisdictionCountryCode = "BE",
        registrationIdentifier = (string?)null,
        publicContactEmail = (string?)null,
        websiteUrl = (string?)null,
        legalNoticeUrl = (string?)null,
        termsUrl = (string?)null,
        privacyUrl = (string?)null,
        isOfficialInstance = false,
        officialOrigin = (string?)null
    });
}
