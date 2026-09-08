namespace Explore.Diagnostic.Doctor;

public static class DoctorRepositoryLocator
{
    public static string LocateRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if ((File.Exists(Path.Combine(directory.FullName, "Explore.slnx")) || File.Exists(Path.Combine(directory.FullName, "Explore.sln")))
                && (Directory.Exists(Path.Combine(directory.FullName, "Explore.Diagnostic")) || Directory.Exists(Path.Combine(directory.FullName, "src", "Explore.Diagnostic"))))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return startDirectory;
    }
}
