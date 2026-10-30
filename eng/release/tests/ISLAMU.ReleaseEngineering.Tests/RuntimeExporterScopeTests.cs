using System.Text;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class RuntimeExporterScopeTests
{
    private const string Scope = "otel_scope_name=\"System.Runtime\",otel_scope_version=\"10.0.0\"";

    private static string Exporter(string user, string system, string memory) =>
        "# TYPE dotnet_process_cpu_time_seconds counter\n# UNIT dotnet_process_cpu_time_seconds seconds\n"
        + "dotnet_process_cpu_time_seconds_total{" + user + "} 1.25\n"
        + "dotnet_process_cpu_time_seconds_total{" + system + "} 0.5\n"
        + "# TYPE dotnet_process_memory_working_set_bytes gauge\n# UNIT dotnet_process_memory_working_set_bytes bytes\n"
        + "dotnet_process_memory_working_set_bytes{" + memory + "} 4096\n# EOF\n";

    [Test]
    [Arguments("otel_scope_name=\"System.Runtime\"")]
    [Arguments(Scope)]
    [Arguments("otel_scope_version=\"\",otel_scope_name=\"System.Runtime\"")]
    [Arguments("otel_scope_name=\"Runtime,\\\"scope\\\"\",otel_scope_version=\"10.0.0\"")]
    [Arguments("otel_scope_name=\"Runtime scope\"")]
    [Arguments("otel_scope_name=\"System.Runtime\",otel_scope_version=\"runtime version\"")]
    public async Task MatchingScopeLabelsAreAdmittedRegardlessOfLabelOrder(string scope)
    {
        string exporter = Exporter(scope + ",cpu_mode=\"user\"", "cpu_mode=\"system\"," + scope, scope);
        await Assert.That(SecurityExecutionEvidence.TryReadRuntimeExporter(Encoding.UTF8.GetBytes(exporter), out var values)).IsTrue();
        await Assert.That(values).IsEqualTo(new SecurityExecutionEvidence.RuntimeExporterValues(1.25, 0.5, 4096));
    }

    [Test]
    [Arguments("foreign-label")]
    [Arguments("duplicate-scope")]
    [Arguments("duplicate-mode")]
    [Arguments("different-name")]
    [Arguments("different-version")]
    [Arguments("missing-scope")]
    [Arguments("missing-version")]
    [Arguments("memory-mode")]
    [Arguments("malformed-quote")]
    [Arguments("invalid-escape")]
    [Arguments("trailing-comma")]
    [Arguments("duplicate-memory")]
    [Arguments("extra-sample-field")]
    public async Task AmbiguousOrMalformedScopeLabelsCannotQualifyPressure(string fault)
    {
        string exporter = Exporter(Scope + ",cpu_mode=\"user\"", Scope + ",cpu_mode=\"system\"", Scope);
        exporter = fault switch
        {
            "foreign-label" => exporter.Replace("cpu_mode=\"user\"", "cpu_mode=\"user\",other=\"value\"", StringComparison.Ordinal),
            "duplicate-scope" => exporter.Replace("cpu_mode=\"user\"", "cpu_mode=\"user\",otel_scope_name=\"System.Runtime\"", StringComparison.Ordinal),
            "duplicate-mode" => exporter.Replace("cpu_mode=\"user\"", "cpu_mode=\"user\",cpu_mode=\"user\"", StringComparison.Ordinal),
            "different-name" => exporter.Replace("bytes{" + Scope, "bytes{" + Scope.Replace("System.Runtime", "Other.Runtime", StringComparison.Ordinal), StringComparison.Ordinal),
            "different-version" => exporter.Replace("bytes{" + Scope, "bytes{" + Scope.Replace("10.0.0", "9.0.0", StringComparison.Ordinal), StringComparison.Ordinal),
            "missing-scope" => exporter.Replace("bytes{" + Scope + "}", "bytes", StringComparison.Ordinal),
            "missing-version" => exporter.Replace("bytes{" + Scope + "}", "bytes{otel_scope_name=\"System.Runtime\"}", StringComparison.Ordinal),
            "memory-mode" => exporter.Replace("bytes{" + Scope + "}", "bytes{" + Scope + ",cpu_mode=\"user\"}", StringComparison.Ordinal),
            "malformed-quote" => exporter.Replace("cpu_mode=\"user\"", "cpu_mode=\"user", StringComparison.Ordinal),
            "invalid-escape" => exporter.Replace("System.Runtime", "System\\qRuntime", StringComparison.Ordinal),
            "trailing-comma" => exporter.Replace("cpu_mode=\"user\"}", "cpu_mode=\"user\",}", StringComparison.Ordinal),
            "duplicate-memory" => exporter.Replace("# EOF", "dotnet_process_memory_working_set_bytes{" + Scope + "} 4096\n# EOF", StringComparison.Ordinal),
            "extra-sample-field" => exporter.Replace(" 4096\n", " 4096 123\n", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        await Assert.That(SecurityExecutionEvidence.TryReadRuntimeExporter(Encoding.UTF8.GetBytes(exporter), out var values)).IsFalse();
        await Assert.That(values).IsNull();
    }
}
