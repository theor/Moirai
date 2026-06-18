namespace Moirai.Benchmarks;

internal static class BenchmarkPaths
{
    /// <summary>
    /// Locates a sample story file under MoiraiCli/ by walking up from the executing assembly's
    /// directory until the repo root is found. BenchmarkDotNet spawns the benchmark in a generated
    /// project whose working directory is not the repo, so a relative path off cwd is unreliable.
    /// </summary>
    public static string FindStory(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "MoiraiCli", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate MoiraiCli/{fileName} above {AppContext.BaseDirectory}");
    }
}
