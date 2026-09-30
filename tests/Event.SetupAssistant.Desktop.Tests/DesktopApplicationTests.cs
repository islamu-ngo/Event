namespace Event.SetupAssistant.Desktop.Tests;

using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Desktop.ViewModels;
using ISLAMU.Event.SetupAssistant.Desktop.Views;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class DesktopApplicationTests
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ISLAMU.Event.SetupAssistant.Desktop.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });

    [Test]
    [NotInParallel]
    public async Task RealIdentityControlsPrepareThenInvalidatePrivateExport()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(DesktopApplicationTests));
        try
        {
            var result = await session.Dispatch(() =>
            {
                using var shell = Shell(static (_, _, _, _) =>
                    Task.FromResult(ProtectedArtifactStatus.Written));
                var window = new MainWindow { DataContext = shell };
                try
                {
                    window.Show();
                    TabControl tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
                    tabs.SelectedIndex = 2;
                    window.UpdateLayout();
                    if (tabs.SelectedItem is not TabItem { Content: IdentityDraftView view })
                        throw new InvalidOperationException("identity-tab-content-missing");
                    TextBox input = view.GetControl<TextBox>("IdentityDocument");
                    bool changed = false;
                    input.TextChanged += (_, _) => changed = true;
                    input.Text = IdentityDocument("Local UI operator");
                    Dispatcher.UIThread.RunJobs();
                    bool firstChangeObserved = changed;

                    view.GetControl<Button>("PrepareIdentity").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    bool prepared = shell.CanSaveIdentity;

                    changed = false;
                    input.Text = "{}";
                    Dispatcher.UIThread.RunJobs();
                    return (firstChangeObserved, prepared, secondChangeObserved: changed,
                        saveAfterEdit: shell.CanSaveIdentity);
                }
                finally
                {
                    window.Close();
                }
            }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            await Assert.That(result.firstChangeObserved).IsTrue();
            await Assert.That(result.prepared).IsTrue();
            await Assert.That(result.secondChangeObserved).IsTrue();
            await Assert.That(result.saveAfterEdit).IsFalse();
        }
        finally
        {
            await Task.Run(session.Dispose).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DefaultEnvironmentIncludesStandaloneDatabaseConfiguration()
    {
        byte[] output = [];
        using var shell = Shell((_, _, bytes, _) =>
        {
            output = bytes.ToArray();
            return Task.FromResult(ProtectedArtifactStatus.Written);
        });
        try
        {
            await Assert.That(shell.PrepareEnvironment()).IsTrue();
            await Assert.That(await shell.SaveEnvironmentAsync()).IsEqualTo(ProtectedArtifactStatus.Written);
            DotenvParseResult parsed = DotenvCodec.Parse(output);
            await Assert.That(parsed.Succeeded).IsTrue();
            await Assert.That(parsed.Document!.Entries.Any(entry => entry.Key == "DATABASE_PROVIDER")).IsTrue();
            await Assert.That(parsed.Document.Entries.Single(entry => entry.Key == "DATABASE_PROVIDER").Value)
                .IsEqualTo("Sqlite");
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(output);
        }
    }

    [Test]
    public async Task OversizedIdentityInputCannotLeaveAUsablePreparation()
    {
        using var shell = Shell(static (_, _, _, _) =>
            Task.FromResult(ProtectedArtifactStatus.Written));
        string identity = IdentityDocument("Bounded local operator");
        await Assert.That(shell.PrepareIdentityDraft(identity)).IsTrue();

        bool accepted = shell.PrepareIdentityDraft(
            new string(' ', OperatorIdentityManifestJson.MaximumBytes) + identity);

        await Assert.That(accepted).IsFalse();
        await Assert.That(shell.CanSaveIdentity).IsFalse();
    }

    [Test]
    public async Task EditingInputsInvalidatesEveryPreparedExport()
    {
        using var shell = Shell(static (_, _, _, _) =>
            Task.FromResult(ProtectedArtifactStatus.Written));

        await Assert.That(shell.PrepareEnvironment()).IsTrue();
        await Assert.That(shell.CanSaveEnvironment).IsTrue();
        shell.EnvironmentFileName = "changed.env";
        await Assert.That(shell.CanSaveEnvironment).IsFalse();

        await Assert.That(shell.PrepareManifest()).IsTrue();
        await Assert.That(shell.CanSaveManifest).IsTrue();
        shell.ManifestSourceName = "changed-source";
        await Assert.That(shell.CanSaveManifest).IsFalse();

        await Assert.That(shell.PrepareIdentityDraft(IdentityDocument("Prepared identity"))).IsTrue();
        await Assert.That(shell.CanSaveIdentity).IsTrue();
        shell.InvalidateIdentityDraft();
        await Assert.That(shell.CanSaveIdentity).IsFalse();
    }

    [Test]
    public async Task FailedProtectedSaveConsumesPreparationAndCannotReenableSave()
    {
        using var shell = Shell(static (_, _, _, _) =>
            Task.FromResult(ProtectedArtifactStatus.UnsafeTarget));
        await Assert.That(shell.PrepareIdentityDraft(IdentityDocument("Private operator"))).IsTrue();

        ProtectedArtifactStatus result = await shell.SaveIdentityAsync();

        await Assert.That(result).IsEqualTo(ProtectedArtifactStatus.UnsafeTarget);
        await Assert.That(shell.CanSaveIdentity).IsFalse();
        await Assert.That(shell.IdentityStatus).IsEqualTo("save-failed:UnsafeTarget");
        await Assert.That(await shell.SaveIdentityAsync()).IsEqualTo(ProtectedArtifactStatus.InvalidRequest);
    }

    [Test]
    public async Task CancellationAndStaleCompletionCannotRestoreSave()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shell = Shell(async (_, _, _, _) =>
        {
            started.SetResult();
            await finish.Task;
            return ProtectedArtifactStatus.Written;
        });
        await Assert.That(shell.PrepareIdentityDraft(IdentityDocument("Private pending operator"))).IsTrue();

        Task<ProtectedArtifactStatus> save = shell.SaveIdentityAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        shell.InvalidateIdentityDraft();
        finish.SetResult();
        _ = await save.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(shell.CanSaveIdentity).IsFalse();
        await Assert.That(shell.IdentityStatus).IsEqualTo("identity-input-required");
    }

    [Test]
    public async Task ObservablePublicStateNeverContainsIdentityInputOrPreparedBytes()
    {
        string marker = "Private-" + Guid.CreateVersion7().ToString("N");
        using var shell = Shell(static (_, _, _, _) =>
            Task.FromResult(ProtectedArtifactStatus.Written));

        await Assert.That(shell.PrepareIdentityDraft(IdentityDocument(marker))).IsTrue();

        foreach (PropertyInfo property in typeof(SetupDesktopShellViewModel)
                     .GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object? value = property.GetIndexParameters().Length == 0
                ? property.GetValue(shell)
                : null;
            await Assert.That(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
                .DoesNotContain(marker, StringComparison.Ordinal);
            await Assert.That(value is not ReadOnlyMemory<byte> and not byte[]).IsTrue();
        }
        await Assert.That(shell.ToString()).DoesNotContain(marker, StringComparison.Ordinal);
    }

    [Test]
    public async Task UnsupportedHostCannotOfferProtectedSave()
    {
        using var shell = new SetupDesktopShellViewModel();

        await Assert.That(shell.ProtectedSaveAvailable).IsEqualTo(OperatingSystem.IsLinux());
        if (!OperatingSystem.IsLinux())
        {
            await Assert.That(shell.PrepareManifest()).IsTrue();
            await Assert.That(shell.CanSaveManifest).IsFalse();
        }
    }

    private static SetupDesktopShellViewModel Shell(
        Func<SetupArtifactKind, string, ReadOnlyMemory<byte>, CancellationToken,
            Task<ProtectedArtifactStatus>> save) =>
        new(save, Path.GetTempPath(), protectedOutputAvailable: true);

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
        officialOrigin = (string?)null,
        isOfficialInstance = false
    });
}
