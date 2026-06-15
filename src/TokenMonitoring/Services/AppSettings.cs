using System.IO;
using System.Text.Json;

namespace TokenMonitoring.Services;

public sealed record AppSettings(
    bool ShowRemaining = true,
    bool StartWithWindows = false,
    bool ShowOnlyWhenCodexActive = true,
    double Opacity = 0.96,
    double? WindowLeft = null,
    double? WindowTop = null);

public static class AppSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TokenMonitoring");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(nameof(AppSettings.ShowOnlyWhenCodexActive), out _)
                ? settings
                : settings with { ShowOnlyWhenCodexActive = true };
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
