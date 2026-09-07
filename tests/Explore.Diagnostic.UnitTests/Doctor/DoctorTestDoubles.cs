using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.UnitTests.Doctor;

internal sealed class FakeDoctorFileSystem : IDoctorFileSystem
{
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);

    public void AddFile(string path, string content) => files[path] = content;

    public bool FileExists(string path) => files.ContainsKey(path);

    public string ReadAllText(string path) => files[path];
}

internal sealed class FakeDoctorProcessRunner : IDoctorProcessRunner
{
    private readonly Dictionary<(string FileName, string Arguments), DoctorProcessResult> results = [];

    public List<(string FileName, string Arguments)> Calls { get; } = [];

    public void AddResult(string fileName, string arguments, DoctorProcessResult result) => results[(fileName, arguments)] = result;

    public Task<DoctorProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(results.TryGetValue((fileName, arguments), out var result)
            ? result
            : new DoctorProcessResult(-1, string.Empty, "not configured"));
    }
}
