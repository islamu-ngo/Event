using System.Runtime.InteropServices;

namespace ISLAMU.ReleaseEngineering;

internal static class ReleaseToolPaths
{
    private static readonly Lazy<string> StatExecutable = new(() => SelectStatExecutable("/usr/bin/stat"));

    public static string Stat => StatExecutable.Value;

    internal static string SelectStatExecutable(string systemEntry)
    {
        if (!Path.IsPathFullyQualified(systemEntry) || Path.GetFileName(systemEntry) != "stat")
            throw new InvalidDataException("stat_tool_entry_invalid");
        string entry = Path.GetFullPath(systemEntry);
        for (string? current = Path.GetDirectoryName(entry); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("stat_tool_selection_linked");
        string selected = new FileInfo(entry).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry;
        if (Path.GetFileName(selected) != "stat")
            throw new InvalidDataException("stat_tool_dispatch_unsupported");
        for (string? current = selected; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("stat_tool_selection_linked");
        if ((File.GetAttributes(selected) & FileAttributes.Directory) != 0)
            throw new InvalidDataException("stat_tool_selection_invalid");
        return selected;
    }

    public static string Git => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe")
        : "/usr/bin/git";

    public static string SshKeygen => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh-keygen.exe")
        : "/usr/bin/ssh-keygen";

    public static string Dotnet => Path.GetFullPath(Path.Combine(
        RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
}
