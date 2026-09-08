namespace Explore.Diagnostic.Doctor.Infrastructure;

public sealed class PhysicalDoctorFileSystem : IDoctorFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);
}
