namespace ISLAMU.SetupAssistant.Terminal.Tests;

using System.Globalization;
using System.Drawing;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Messaging;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Presentation;
using ISLAMU.Event.SetupAssistant.Terminal;
using global::Terminal.Gui.App;
using global::Terminal.Gui.Input;
using global::Terminal.Gui.ViewBase;
using global::Terminal.Gui.Views;

public sealed class SetupTerminalGuiAdapterTests
{
    [Test]
    public async Task WorkspaceCommandWritesCanonicalCoreBytes()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()))
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-adapter-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        string secretValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            using var secret = new SetupTerminalSecretBuffer();
            var operation = new SetupTerminalArtifactOperation(
                () => "adapter.env",
                secret,
                new SetupTerminalProtectedWriter(directory));
            using var session = new SetupPresentationSession(new StrongReferenceMessenger());
            await Assert.That(SetupWorkspaceId.TryCreate("environment", out SetupWorkspaceId id)).IsTrue();
            using SetupPresentationWorkspace workspace = session.CreateWorkspace(id, operation);
            workspace.Activate();
            await Assert.That(workspace.SetPublicInput("adapter.env")).IsTrue();
            await Assert.That(secret.TryReplace(secretValue)).IsTrue();
            await Assert.That(operation.PrepareManual()).IsTrue();

            await workspace.ExecuteAsync(Guid.CreateVersion7());

            DotenvCompositionResult expected = DotenvComposer.ComposeWithSecrets(
                CanonicalEnvironmentCatalogue.Catalogue,
                new EnvironmentActivationContext(
                    "standalone",
                    ["platform"],
                    ["environment", "local", "sqlite"]),
                [new DotenvEntry(
                    "SETUP_SECRET",
                    secretValue,
                    DotenvEntryKind.LocalHumanValue,
                    true,
                    DotenvProvenance.UserInput)]);
            DotenvRenderResult rendered = DotenvCodec.Render(expected.Document, true);

            await Assert.That(File.ReadAllBytes(Path.Combine(directory, "adapter.env")))
                .IsEquivalentTo(rendered.Bytes.ToArray());
            await Assert.That(secret.Count).IsEqualTo(0);
            await Assert.That(workspace.Result).IsTypeOf<SetupTerminalArtifactResult>();
        }
        finally
        {
            string path = Path.Combine(directory, "adapter.env");
            if (File.Exists(path))
                File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Test]
    [NotInParallel]
    public async Task ArabicUiCultureLoadsBundledLocalizedErrorText()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            string[] keys =
            [
                "WindowTitle", "Introduction", "OutputFile", "SetupSecret",
                "SaveManual", "Generate", "Close", "Cancel", "Ready",
                "ProtectedUnavailable", "Limitations", "TerminalTooSmall", "InvalidOutputPartial",
                "InvalidSecret", "SecretCommandsDisabled", "InvalidManual", "InvalidOutput",
                "Cancelled", "Unavailable", "Writing", "ReadinessReady",
                "ReadinessIncomplete", "ReadinessBlocked", "DigestSuffix",
                "OutcomeComplete", "OutcomeIncomplete", "OutcomeFailed"
            ];
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            string[] english = keys.Select(SetupTerminalText.Get).ToArray();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");

            string[] arabic = keys.Select(SetupTerminalText.Get).ToArray();
            await Assert.That(arabic.Zip(english).All(pair => pair.First != pair.Second)).IsTrue();
            await Assert.That(SetupTerminalText.Get("MissingKey"))
                .IsEqualTo("MissingKey");
            var success = new SetupTerminalArtifactResult(
                true,
                "terminal-complete",
                "abc123",
                DotenvReadinessState.Ready,
                0,
                0);
            await Assert.That(SetupTerminalText.FormatResult(success))
                .Contains("تم إنشاء الملف المحمي", StringComparison.Ordinal)
                .And.Contains("بصمة", StringComparison.Ordinal)
                .And.DoesNotContain("Readiness", StringComparison.Ordinal)
                .And.DoesNotContain("Digest", StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public async Task WindowUsesMaskedNativeFocusControlsAndSmallLayoutWithoutFocusableOverlap()
    {
        using IApplication application = Application.Create();
        using var secret = new SetupTerminalSecretBuffer();
        var operation = new SetupTerminalArtifactOperation(
            () => "layout.env",
            secret,
            new SetupTerminalProtectedWriter(Path.GetTempPath()));
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

        window.Frame = new Rectangle(0, 0, 40, 12);
        window.ApplyViewportPolicy(new Size(40, 12));
        foreach (View view in window.SubViews)
            await Assert.That(view.SetRelativeLayout(window.Viewport.Size)).IsTrue();
        await Assert.That(window.SubViews.Where(view => view is Label && view.Visible)
            .Any(view => view.Text == SetupTerminalText.Get("TerminalTooSmall"))).IsTrue();
        await Assert.That(window.SubViews.Where(view => view.CanFocus && view.Visible)).IsEmpty();

        window.ApplyViewportPolicy(new Size(30, 24));
        await Assert.That(window.SubViews.Where(view => view is Label && view.Visible)
            .Any(view => view.Text == SetupTerminalText.Get("TerminalTooSmall"))).IsTrue();
        await Assert.That(window.SubViews.Where(view => view.CanFocus && view.Visible)).IsEmpty();

        window.Frame = new Rectangle(0, 0, 80, 24);
        window.ApplyViewportPolicy(new Size(80, 24));
        foreach (View view in window.SubViews)
            await Assert.That(view.SetRelativeLayout(window.Viewport.Size)).IsTrue();
        View[] focusable = window.SubViews.Where(view => view.CanFocus && view.Visible).ToArray();
        await Assert.That(focusable.OfType<TextField>()).Count().IsEqualTo(2);
        await Assert.That(focusable.OfType<Button>()).Count().IsEqualTo(3);
        await Assert.That(focusable.OfType<SetupSecretTextField>().Single().Secret).IsTrue();
        await Assert.That(focusable.Select(view => view.Frame.Y).Distinct().Count())
            .IsEqualTo(focusable.Length);
    }

    [Test]
    [NotInParallel]
    public async Task NativeSaveAcceptRoutesTypedKeysThroughWorkspaceCommand()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()))
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-native-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        try
        {
            using IApplication application = Application.Create();
            application.Init("dotnet");
            using var secret = new SetupTerminalSecretBuffer();
            SetupPresentationWorkspace? workspace = null;
            var operation = new SetupTerminalArtifactOperation(
                () => workspace?.PublicInput ?? string.Empty,
                secret,
                new SetupTerminalProtectedWriter(directory));
            using var session = new SetupPresentationSession(new StrongReferenceMessenger());
            await Assert.That(SetupWorkspaceId.TryCreate("environment", out SetupWorkspaceId id)).IsTrue();
            workspace = session.CreateWorkspace(id, operation);
            workspace.Activate();
            using var window = new SetupTerminalWindow(
                application,
                workspace,
                operation,
                secret,
                protectedOutputAvailable: true);
            window.Frame = new Rectangle(0, 0, 80, 24);
            window.ApplyViewportPolicy(new Size(80, 24));
            foreach (View view in window.SubViews)
                view.SetRelativeLayout(window.Viewport.Size);
            SetupSecretTextField field = window.SubViews.OfType<SetupSecretTextField>().Single();
            TextField output = window.SubViews.OfType<TextField>().First(v => v != field);
            await Assert.That(output.SetFocus()).IsTrue();
            await Assert.That(window.AdvanceFocus(NavigationDirection.Forward, null)).IsTrue();
            await Assert.That(window.Focused).IsEqualTo(field);
            const string value = "Abcdefghijklmnopqrstuvwxyz012345";
            foreach (char character in value)
                await Assert.That(field.NewKeyDownEvent(new Key(character))).IsTrue();
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            workspace.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SetupPresentationWorkspace.Result)
                    && workspace.Result is not null)
                    settled.TrySetResult();
            };

            bool? accepted = window.SubViews.OfType<Button>().First().InvokeCommand(Command.Accept);
            await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(accepted).IsTrue();
            await Assert.That(workspace.Result).IsTypeOf<SetupTerminalArtifactResult>();
            await Assert.That(secret.Count).IsEqualTo(0);
            await Assert.That(field.Text).IsEmpty();
            await Assert.That(File.ReadAllText(Path.Combine(directory, ".env.setup")))
                .Contains("SETUP_SECRET=" + value, StringComparison.Ordinal);
        }
        finally
        {
            string path = Path.Combine(directory, ".env.setup");
            if (File.Exists(path))
                File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Test]
    [NotInParallel]
    public async Task NativeCloseAcceptCancelsInFlightWriteBeforeCommit()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()))
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-native-cancel-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        var reachedCommitBoundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using IApplication application = Application.Create();
            application.Init("dotnet");
            using var secret = new SetupTerminalSecretBuffer();
            SetupPresentationWorkspace? workspace = null;
            var operation = new SetupTerminalArtifactOperation(
                () => workspace?.PublicInput ?? string.Empty,
                secret,
                new SetupTerminalProtectedWriter(
                    directory,
                    async token =>
                    {
                        reachedCommitBoundary.TrySetResult();
                        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token);
                    }));
            using var session = new SetupPresentationSession(new StrongReferenceMessenger());
            await Assert.That(SetupWorkspaceId.TryCreate("environment", out SetupWorkspaceId id)).IsTrue();
            workspace = session.CreateWorkspace(id, operation);
            workspace.Activate();
            using var window = new SetupTerminalWindow(
                application,
                workspace,
                operation,
                secret,
                protectedOutputAvailable: true);
            window.ApplyViewportPolicy(new Size(80, 24));
            SetupSecretTextField field = window.SubViews.OfType<SetupSecretTextField>().Single();
            foreach (char character in "CancelSafeValue0123456789")
                field.NewKeyDownEvent(new Key(character));
            window.SubViews.OfType<Button>().First().InvokeCommand(Command.Accept);
            await reachedCommitBoundary.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            workspace.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SetupPresentationWorkspace.IsBusy)
                    && !workspace.IsBusy)
                    cancelled.TrySetResult();
            };

            await Assert.That(window.SubViews.OfType<Button>().Last().InvokeCommand(Command.Accept)).IsTrue();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(secret.Count).IsEqualTo(0);
            await Assert.That(field.Text).IsEmpty();
            await Assert.That(Directory.EnumerateFiles(directory)).IsEmpty();
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
