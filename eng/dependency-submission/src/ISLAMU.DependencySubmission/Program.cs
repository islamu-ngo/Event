using System.Collections.Frozen;
using System.Globalization;
using System.Net.Http.Headers;

namespace ISLAMU.DependencySubmission;

public enum ToolCommand { Validate, Snapshot, Submit }

public static class Program
{
    private static FrozenDictionary<string, ToolCommand> Commands { get; } =
        new Dictionary<string, ToolCommand>(StringComparer.Ordinal)
        {
            ["validate"] = ToolCommand.Validate,
            ["snapshot"] = ToolCommand.Snapshot,
            ["submit"] = ToolCommand.Submit
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || !Commands.TryGetValue(args[0], out var command) ||
            (command == ToolCommand.Validate ? args.Length is not (2 or 3) :
                args.Length != (command == ToolCommand.Snapshot ? 3 : 2)))
        {
            Console.Error.WriteLine("Usage: dependency-submission validate <root> [report] | snapshot <root> <output> | submit <root>");
            return 2;
        }
        using var preparation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            var root = Path.GetFullPath(args[1]);
            var manifests = SnapshotBuilder.BuildManifests(
                await RepositoryLocks.ReadAsync(root, preparation.Token).ConfigureAwait(false));
            var context = command == ToolCommand.Validate
                ? new SnapshotContext(
                    (await RepositoryLocks.GitAsync(root, ["rev-parse", "HEAD"], preparation.Token).ConfigureAwait(false)).Trim(),
                    (await RepositoryLocks.GitAsync(root, ["symbolic-ref", "HEAD"], preparation.Token).ConfigureAwait(false)).Trim(),
                    new SnapshotJob(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "dependency-submission",
                        new Uri("https://github.com/islamu-ngo/Event")), DateTimeOffset.UtcNow)
                : ActionsContext();
            var snapshot = SnapshotBuilder.Build(context, manifests);
            if (command != ToolCommand.Validate &&
                !(await RepositoryLocks.GitAsync(root, ["rev-parse", "HEAD"], preparation.Token).ConfigureAwait(false))
                .Trim().Equals(context.Sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checkout does not match snapshot commit.");
            if (command == ToolCommand.Validate)
            {
                _ = SnapshotJson.Serialize(snapshot);
                if (args.Length == 3)
                    await File.WriteAllBytesAsync(args[2], SnapshotJson.Serialize(
                        new ValidationReport(true, null)), preparation.Token).ConfigureAwait(false);
                Console.WriteLine("result=validated");
                return 0;
            }
            if (command == ToolCommand.Snapshot)
            {
                await File.WriteAllBytesAsync(args[2], SnapshotJson.Serialize(snapshot), preparation.Token).ConfigureAwait(false);
                Console.WriteLine("result=snapshot_written");
                return 0;
            }
            var repository = RequiredEnvironment("GITHUB_REPOSITORY");
            var parts = repository.Split('/');
            if (parts.Length != 2 || parts.Any(part => part.Length == 0 || !part.All(
                character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
                throw new InvalidDataException("Invalid repository identifier.");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RequiredEnvironment("GITHUB_TOKEN"));
            var result = await new SubmissionClient(http, TimeProvider.System, entry => SubmissionLogging.Write(Console.Out, entry))
                .SubmitAsync(new Uri($"https://api.github.com/repos/{parts[0]}/{parts[1]}/dependency-graph/snapshots"), snapshot)
                .ConfigureAwait(false);
            return result.Outcome == SubmissionOutcome.Success ? 0 : 1;
        }
        catch (SnapshotValidationException exception)
        {
            if (command == ToolCommand.Validate && args.Length == 3)
            {
                try
                {
                    await File.WriteAllBytesAsync(args[2], SnapshotJson.Serialize(
                        new ValidationReport(false, new ValidationFinding(exception.Failure)))).ConfigureAwait(false);
                }
                catch (Exception reportError) when (reportError is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    Console.Error.WriteLine("result=validation_report_failed");
                }
            }
            Console.Error.WriteLine($"classification={SnapshotValidationException.Codes[exception.Failure]} result=invalid_input");
            return 2;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("result=invalid_input_or_preparation_failed");
            return 2;
        }
    }

    private static SnapshotContext ActionsContext()
    {
        var runId = RequiredEnvironment("GITHUB_RUN_ID");
        if (!long.TryParse(runId, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count <= 0)
            throw new InvalidDataException("Invalid run identifier.");
        var repository = RequiredEnvironment("GITHUB_REPOSITORY");
        return new SnapshotContext(RequiredEnvironment("GITHUB_SHA"), RequiredEnvironment("GITHUB_REF"),
            new SnapshotJob(count, "dependency-submission",
                new Uri($"https://github.com/{repository}/actions/runs/{runId}")), DateTimeOffset.UtcNow);
    }

    private static string RequiredEnvironment(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value : throw new InvalidDataException("Missing workflow environment.");
}

public sealed record ValidationReport(bool Valid, ValidationFinding? Finding);
public sealed record ValidationFinding(SnapshotValidationFailure Classification);
