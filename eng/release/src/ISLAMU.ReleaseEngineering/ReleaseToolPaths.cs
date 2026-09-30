using System.Runtime.InteropServices;

namespace ISLAMU.ReleaseEngineering;

internal static class ReleaseToolPaths
{
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
