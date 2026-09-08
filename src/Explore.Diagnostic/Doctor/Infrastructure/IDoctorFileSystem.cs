namespace Explore.Diagnostic.Doctor.Infrastructure;

public interface IDoctorFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
}
