using System.IO;
using System.Text.Json;

namespace AppManager.Services;

public class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppManager", "config.json");

    public string OpenCodeApiKey { get; set; } = "";

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = new AppConfig();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("OpenCodeApiKey", out var prop))
                    config.OpenCodeApiKey = prop.GetString() ?? "";
                return config;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppConfig.Load failed: {ex.Message}");
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
