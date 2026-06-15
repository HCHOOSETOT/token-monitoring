using System.Diagnostics;
using System.IO;

namespace TokenMonitoring.Services;

public sealed record CodexLaunchCommand(string FileName, string Arguments);

public static class CodexExecutableLocator
{
    public static CodexLaunchCommand Find()
    {
        var configuredPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return CreateCommand(configuredPath);
        }

        var pathCommand = FindOnPath();
        if (pathCommand is not null)
        {
            return CreateCommand(pathCommand);
        }

        var runningPaths = new List<string>();
        foreach (var process in Process.GetProcessesByName("codex"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    runningPaths.Add(path);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        var runningPath = runningPaths
            .OrderBy(path => path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (runningPath is not null)
        {
            return CreateCommand(runningPath);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Programs", "Codex", "resources", "codex.exe"),
            Path.Combine(localAppData, "Programs", "codex", "resources", "codex.exe")
        };

        var installedPath = candidates.FirstOrDefault(File.Exists);
        return installedPath is null
            ? new CodexLaunchCommand("codex", "app-server --stdio")
            : CreateCommand(installedPath);
    }

    private static string? FindOnPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedDirectory = directory.Trim().Trim('"');
            foreach (var fileName in new[] { "codex.exe", "codex.cmd", "codex.bat" })
            {
                var candidate = Path.Combine(normalizedDirectory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static CodexLaunchCommand CreateCommand(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var commandInterpreter = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            return new CodexLaunchCommand(commandInterpreter, $"/d /s /c \"\"{path}\" app-server --stdio\"");
        }

        return new CodexLaunchCommand(path, "app-server --stdio");
    }
}
