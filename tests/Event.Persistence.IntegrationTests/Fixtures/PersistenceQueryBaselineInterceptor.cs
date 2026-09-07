using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class PersistenceQueryBaselineInterceptor : DbCommandInterceptor
{
    private readonly object _gate = new();
    private readonly List<CommandShape> _commands = [];

    public void Reset()
    {
        lock (_gate)
        {
            _commands.Clear();
        }
    }

    public PersistenceQueryBaselineSnapshot Snapshot(
        string operation,
        int cardinality,
        TimeSpan elapsed)
    {
        lock (_gate)
        {
            return new PersistenceQueryBaselineSnapshot(
                operation,
                _commands.Count,
                _commands.Count == 0 ? 0 : _commands.Max(command => command.SelectedColumnCount),
                _commands.Count == 0 ? 0 : _commands.Max(command => command.ParameterCount),
                _commands.Sum(command => command.Duration.TotalMilliseconds),
                elapsed.TotalMilliseconds,
                cardinality);
        }
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Capture(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Capture(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbCommand command, TimeSpan duration)
    {
        var shape = new CommandShape(
            CountSelectedColumns(command.CommandText),
            command.Parameters.Count,
            duration);
        lock (_gate)
        {
            _commands.Add(shape);
        }
    }

    private static int CountSelectedColumns(string commandText)
    {
        string normalized = string.Join(
            ' ',
            commandText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int selectIndex = normalized.IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);
        if (selectIndex < 0)
        {
            int returningIndex = normalized.IndexOf(" RETURNING ", StringComparison.OrdinalIgnoreCase);
            return returningIndex < 0
                ? 0
                : CountTopLevelExpressions(normalized[(returningIndex + " RETURNING ".Length)..]);
        }

        int projectionStart = selectIndex + "SELECT ".Length;
        int fromIndex = normalized.IndexOf(" FROM ", projectionStart, StringComparison.OrdinalIgnoreCase);
        return fromIndex < 0
            ? 0
            : CountTopLevelExpressions(normalized[projectionStart..fromIndex]);
    }

    private static int CountTopLevelExpressions(string projection)
    {
        if (string.IsNullOrWhiteSpace(projection))
        {
            return 0;
        }

        int count = 1;
        int depth = 0;
        foreach (char character in projection)
        {
            if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && depth > 0)
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                count++;
            }
        }

        return count;
    }

    private sealed record CommandShape(
        int SelectedColumnCount,
        int ParameterCount,
        TimeSpan Duration);
}

public sealed record PersistenceQueryBaselineSnapshot(
    string Operation,
    int CommandCount,
    int MaximumSelectedColumnCount,
    int MaximumParameterCount,
    double CommandDurationMilliseconds,
    double ElapsedMilliseconds,
    int Cardinality)
{
    public string ToEvidenceLine() =>
        FormattableString.Invariant(
            $"PERSISTENCE_QUERY_BASELINE operation={Operation} commands={CommandCount} selected_columns_max={MaximumSelectedColumnCount} parameters_max={MaximumParameterCount} command_duration_ms={CommandDurationMilliseconds:F3} elapsed_ms={ElapsedMilliseconds:F3} cardinality={Cardinality}");
}

public static class PersistenceQueryBaselineEvidence
{
    private const string OutputPathVariable = "PERSISTENCE_QUERY_BASELINE_OUTPUT";
    private static readonly object Gate = new();

    public static void Record(PersistenceQueryBaselineSnapshot snapshot)
    {
        string evidence = snapshot.ToEvidenceLine();
        Console.WriteLine(evidence);
        string? outputPath = Environment.GetEnvironmentVariable(OutputPathVariable);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        lock (Gate)
        {
            File.AppendAllText(outputPath, evidence + Environment.NewLine);
        }
    }
}
