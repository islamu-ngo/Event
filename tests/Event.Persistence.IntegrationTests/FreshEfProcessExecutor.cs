// ABOUTME: Runs opaque production EF construction contracts in a fresh copy of this test executable.
// ABOUTME: Exact TUnit node IDs retain parameterized coverage; child failures and cancellation reach dotnet test.

using System.Diagnostics;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests;

public sealed class FreshEfProcessExecutor : ITestExecutor
{
    internal const string ChildTestIdVariable = "EVENT_PERSISTENCE_EF_CHILD_TEST_ID";
    internal const string ParentProcessIdVariable = "EVENT_PERSISTENCE_EF_PARENT_PROCESS_ID";

    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        string testId = context.Metadata.TestDetails.Identity.TestId;
        string? childTestId = Environment.GetEnvironmentVariable(ChildTestIdVariable);
        if (childTestId is not null)
        {
            if (!StringComparer.Ordinal.Equals(childTestId, testId))
            {
                throw new InvalidOperationException($"Isolated EF process selected unexpected test {testId}.");
            }

            await action();
            return;
        }

        await RunAsync(testId, context.Execution.CancellationToken);
    }

    internal static async Task RunAsync(string testId, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        start.ArgumentList.Add(typeof(FreshEfProcessExecutor).Assembly.Location);
        string resultsDirectory = Path.Combine(Path.GetTempPath(), "event-persistence-ef", Guid.NewGuid().ToString("N"));
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(resultsDirectory);
        start.ArgumentList.Add("--filter-uid");
        start.ArgumentList.Add(testId);
        start.ArgumentList.Add("--minimum-expected-tests");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("--zero-tests-policy");
        start.ArgumentList.Add("strict");
        start.ArgumentList.Add("--no-ansi");
        start.ArgumentList.Add("--progress");
        start.ArgumentList.Add("off");
        start.ArgumentList.Add("--exit-on-process-exit");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment[ChildTestIdVariable] = testId;
        start.Environment[ParentProcessIdVariable] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var process = new Process { StartInfo = start };
        process.Start();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        string output = await stdout + await stderr;
        if (process.ExitCode != 0)
        {
            throw new IsolatedEfTestException(process.ExitCode, testId, output);
        }
        if (Directory.Exists(resultsDirectory))
        {
            Directory.Delete(resultsDirectory, recursive: true);
        }
    }
}

internal sealed class IsolatedEfTestException(int exitCode, string testId, string output)
    : Exception($"Isolated EF test {testId} exited with code {exitCode}.\n{output}")
{
    public int ExitCode { get; } = exitCode;
}
