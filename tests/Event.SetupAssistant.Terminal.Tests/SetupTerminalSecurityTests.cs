namespace ISLAMU.SetupAssistant.Terminal.Tests;

using System.Security.Cryptography;
using ISLAMU.Event.Setup.Artifacts;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.SetupAssistant.Terminal;
using global::Terminal.Gui.Input;

public sealed class SetupTerminalSecurityTests
{
    [Test]
    public async Task ProcessArgumentsCannotTransportSecretValues()
    {
        int exit = SetupTerminalProgram.Run(["--secret", Convert.ToHexString(RandomNumberGenerator.GetBytes(32))]);

        await Assert.That(exit).IsEqualTo(64);
    }

    [Test]
    public async Task ManualSecretIsClearedAndWrittenOnlyOnceWithOwnerMode()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-test-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "safe.env");
        string secretValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            using var secret = new SetupTerminalSecretBuffer();
            var writer = new ProtectedArtifactWriter();
            var operation = new SetupTerminalArtifactOperation(() => "safe.env", secret, writer, directory);

            await Assert.That(secret.TryReplace(secretValue)).IsTrue();
            await Assert.That(operation.PrepareManual()).IsTrue();
            var outcome = await operation.ExecuteAsync(CancellationToken.None);
            var result = (SetupTerminalArtifactResult)outcome.CoreResult;

            await Assert.That(result.Written).IsTrue();
            await Assert.That(secret.Count).IsEqualTo(0);
            await Assert.That(result.ToString()).DoesNotContain(secretValue, StringComparison.Ordinal);
            await Assert.That(File.ReadAllText(path)).Contains("SETUP_SECRET=" + secretValue, StringComparison.Ordinal);
            await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var repeat = await writer.PrepareAsync(
                SetupArtifactKind.Environment, path, RandomNumberGenerator.GetBytes(32));
            await Assert.That(await repeat.CommitAsync()).IsEqualTo(ProtectedArtifactStatus.TargetExists);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task SecretBufferRejectsControlCharactersAndRedactsItsProjection()
    {
        using var secret = new SetupTerminalSecretBuffer();

        await Assert.That(secret.TryReplace(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) + "\n")).IsFalse();
        await Assert.That(secret.Count).IsEqualTo(0);
        await Assert.That(secret.ToString()).Contains("Redacted", StringComparison.Ordinal);
    }

    [Test]
    public async Task PreCancelledOperationClearsPreparedSecret()
    {
        using var secret = new SetupTerminalSecretBuffer();
        var operation = new SetupTerminalArtifactOperation(
            () => "safe.env",
            secret,
            new ProtectedArtifactWriter(),
            Path.GetTempPath());
        using var cancellation = new CancellationTokenSource();
        await Assert.That(secret.TryReplace(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))).IsTrue();
        await Assert.That(operation.PrepareManual()).IsTrue();
        cancellation.Cancel();

        await Assert.That(async () => await operation.ExecuteAsync(cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(secret.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SecretFieldBlocksClipboardContextAndUndoHistory()
    {
        using var secret = new SetupTerminalSecretBuffer();
        using var field = new SetupSecretTextField(secret);
        int blocked = 0;
        field.SensitiveCommandBlocked += (_, _) => blocked++;
        await Assert.That(field.NewKeyDownEvent(new Key('c'))).IsTrue();
        await Assert.That(secret.Count).IsEqualTo(1);
        await Assert.That(field.Text).DoesNotContain("c", StringComparison.Ordinal);
        await Assert.That(field.Text).IsEqualTo("●");

        foreach (Command command in new[]
        {
            Command.Copy,
            Command.Cut,
            Command.Paste,
            Command.Undo,
            Command.Redo,
            Command.Context,
            Command.CutToEndOfLine,
            Command.CutToStartOfLine
        })
            await Assert.That(field.InvokeCommand(command)).IsTrue();

        field.ClearSensitiveState();
        await Assert.That(field.InvokeCommand(Command.Undo)).IsTrue();
        await Assert.That(blocked).IsEqualTo(9);
        await Assert.That(field.Text).IsEmpty();
    }

    [Test]
    public async Task CancelledProtectedWriteLeavesNoFinalOrTemporaryArtifact()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-cancel-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        try
        {
            var writer = new ProtectedArtifactWriter();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.That(async () => await writer.PrepareAsync(
                SetupArtifactKind.Environment,
                Path.Combine(directory, "cancelled.env"),
                RandomNumberGenerator.GetBytes(32),
                cancellation.Token)).Throws<OperationCanceledException>();
            await Assert.That(Directory.EnumerateFiles(directory)).IsEmpty();
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task SignalDuringPreparedWriteCancelsBeforeAtomicCommitAndClearsState()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-signal-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        var reachedCommitBoundary = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var cancellation = new CancellationTokenSource();
            using var secret = new SetupTerminalSecretBuffer();
            var writer = new ProtectedArtifactWriter();
            var resumeCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = new SetupTerminalArtifactOperation(() => "signal.env", secret, writer, directory,
                async token =>
                {
                    reachedCommitBoundary.TrySetResult();
                    await resumeCommit.Task.WaitAsync(token);
                });
            await Assert.That(secret.TryReplace(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))).IsTrue();
            await Assert.That(operation.PrepareManual()).IsTrue();
            Task execution = operation.ExecuteAsync(cancellation.Token);
            await reachedCommitBoundary.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var signals = new SetupTerminalSignalScope(secret, cancellation.Cancel);

            signals.RequestStop();

            await Assert.That(async () => await execution).Throws<OperationCanceledException>();
            await Assert.That(secret.Count).IsEqualTo(0);
            await Assert.That(Directory.EnumerateFiles(directory)).IsEmpty();
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task SharedWriterKeepsTerminalStagingAnonymous()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string directory = Path.Combine(Path.GetTempPath(), "islamu-terminal-swap-" + Guid.CreateVersion7());
        Directory.CreateDirectory(directory);
        try
        {
            using var secret = new SetupTerminalSecretBuffer();
            var writer = new ProtectedArtifactWriter();
            var operation = new SetupTerminalArtifactOperation(() => "safe.env", secret, writer, directory,
                _ =>
                {
                    if (Directory.EnumerateFiles(directory).Any())
                        throw new InvalidOperationException("replaceable-staging-entry");
                    return Task.CompletedTask;
                });
            await Assert.That(secret.TryReplace(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))).IsTrue();
            await Assert.That(operation.PrepareManual()).IsTrue();
            var outcome = await operation.ExecuteAsync(CancellationToken.None);
            await Assert.That(((SetupTerminalArtifactResult)outcome.CoreResult).Written).IsTrue();
            File.Delete(Path.Combine(directory, "safe.env"));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }
}
