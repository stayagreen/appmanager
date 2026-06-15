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

        var startBatFiles = Directory.GetFiles(rootPath, "start.bat", SearchOption.AllDirectories);

        foreach (var startBat in startBatFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(startBat)!;
                var entry = new ProgramEntry
                {
                    Directory = dir,
                    StartBat = startBat,
                };

                var stopBat = Path.Combine(dir, "stop.bat");
                if (File.Exists(stopBat)) entry.StopBat = stopBat;

                var restartBat = Path.Combine(dir, "restart.bat");
                if (File.Exists(restartBat)) entry.RestartBat = restartBat;

                var packageJson = Path.Combine(dir, "package.json");
                if (File.Exists(packageJson))
                {
                    try
                    {
                        var json = File.ReadAllText(packageJson);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("name", out var nameProp))
                            entry.Name = nameProp.GetString() ?? Path.GetFileName(dir);
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(entry.Name))
                    entry.Name = Path.GetFileName(dir);

                results.Add(new ScanResult(dir, "", entry, false));
            }
            catch { }
        }

        return results;
    }
}
