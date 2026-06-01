using System.Runtime.InteropServices;

namespace MosaicLiveGenerator.Process;

internal static class FfmpegPathResolver
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string ExecutableFileName =>
        IsWindows ? "ffmpeg.exe" : "ffmpeg";

    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            if (!File.Exists(explicitPath))
                throw new MosaicConfigurationException(
                    $"FfmpegOptions.BinaryPath '{explicitPath}' does not exist.");
            return explicitPath!;
        }

        var fromPath = TryFindOnPath();
        if (fromPath is null)
            throw new MosaicConfigurationException(
                $"ffmpeg not found on PATH and no FfmpegOptions.BinaryPath was provided. " +
                $"Looked for '{ExecutableFileName}'.");
        return fromPath;
    }

    public static string? TryFindOnPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        var separator = IsWindows ? ';' : ':';
        foreach (var dir in pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir.Trim(), ExecutableFileName); }
            catch { continue; }

            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
