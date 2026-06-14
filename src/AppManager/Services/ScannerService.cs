using System.IO;
using System.Text.Json;
using AppManager.Models;

namespace AppManager.Services;

public class ScannerService
{
    public readonly record struct ScanResult(
        string Directory,
        string AppJsonPath,
        ProgramEntry Entry,
        bool AlreadyExists
    );

    public List<ScanResult> ScanDirectory(string rootPath)
    {
        var results = new List<ScanResult>();
        if (!Directory.Exists(rootPath)) return results;

        var appJsonFiles = Directory.GetFiles(rootPath, "app.json", SearchOption.AllDirectories);

        foreach (var jsonPath in appJsonFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(jsonPath)!;
                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var entry = new ProgramEntry
                {
                    Directory = dir,
                    Name = GetStringProperty(root, "name") ?? Path.GetFileName(dir),
                    ApiPort = GetIntProperty(root, "apiPort"),
                    WebPort = GetIntProperty(root, "webPort"),
                    WsPort = GetIntProperty(root, "wsPort"),
                    LoginUrl = GetStringProperty(root, "loginUrl") ?? "",
                };

                var startBat = GetStringProperty(root, "startBat");
                if (startBat != null)
                    entry.StartBat = Path.GetFullPath(Path.Combine(dir, startBat));

                var stopBat = GetStringProperty(root, "stopBat");
                if (stopBat != null)
                    entry.StopBat = Path.GetFullPath(Path.Combine(dir, stopBat));

                var restartBat = GetStringProperty(root, "restartBat");
                if (restartBat != null)
                    entry.RestartBat = Path.GetFullPath(Path.Combine(dir, restartBat));

                results.Add(new ScanResult(dir, jsonPath, entry, false));
            }
            catch { }
        }

        return results;
    }

    private static string? GetStringProperty(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int? GetIntProperty(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return null;
    }
}
