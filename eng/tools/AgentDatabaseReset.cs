#:property RestorePackagesWithLockFile=false
#:property PublishAot=false

using System.Diagnostics;
using System.IO.Pipes;

if (args is ["--help"])
{
    Usage();
    return 0;
}
if (args is not ["--owner-pid", var rawPid, "--apply"] || !int.TryParse(rawPid, out int pid) || pid <= 0)
{
    Usage();
    return 64;
}

using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(125));
ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancel;
long started = Stopwatch.GetTimestamp();
try
{
    // The API process owns admission, draining, purge and native recovery. This tool never opens a DB
    // connection and cannot accidentally reset a database behind a still-serving API.
    await using var pipe = new NamedPipeClientStream(".", $"islamu-agent-database-reset-{pid}",
        PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
    connectDeadline.CancelAfter(TimeSpan.FromSeconds(5));
    await pipe.ConnectAsync(connectDeadline.Token);
    await pipe.WriteAsync("reset\n"u8.ToArray(), cancellation.Token);
    await pipe.FlushAsync(cancellation.Token);
    using var reader = new StreamReader(pipe);
    string? response = await reader.ReadLineAsync(cancellation.Token);
    if (response is null || !response.StartsWith("ready ", StringComparison.Ordinal))
    {
        Console.WriteLine("agent-database-reset: failed; maintenance remains closed; retry the same owner after resolving the failure");
        return 70;
    }
    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    Console.WriteLine(FormattableString.Invariant(
        $"agent-database-reset: ready; request-to-ready={elapsed:F0}ms; target=2000ms; target-met={elapsed < 2000}"));
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("agent-database-reset: cancelled-or-timed-out; owner outcome must be checked before browser work");
    return 130;
}
catch (Exception)
{
    Console.WriteLine("agent-database-reset: owner-control-unavailable");
    return 70;
}
finally { Console.CancelKeyPress -= cancel; }

static void Usage() => Console.WriteLine(
    "Usage: dotnet run eng/tools/AgentDatabaseReset.cs -- --owner-pid <agent-api-process-id> --apply\n" +
    "Development-only database reset. Run as the API's OS user. Redis, Mailpit, files and erasure authority are not erased.");
