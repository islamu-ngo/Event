#:property RestorePackagesWithLockFile=false
#:project ../../eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj

using ISLAMU.ReleaseEngineering;

string? eventPath = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
string? fileMetadataPath = Environment.GetEnvironmentVariable("RELEASE_IMPACT_FILE_METADATA_PATH");
string? githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

return await ReleaseImpactPolicy.EvaluateEventFileAsync(eventPath, fileMetadataPath, githubToken, Console.Out);
