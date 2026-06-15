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

    public async Task<List<ScanResult>> ScanDirectoryAsync(string rootPath, Action<int, int, string>? onProgress = null)
    {
        var results = new List<ScanResult>();
        if (!Directory.Exists(rootPath)) return results;

        var ai = new AIScriptGenerator();
        var startBatFiles = Directory.GetFiles(rootPath, "start.bat", SearchOption.AllDirectories);
        var total = startBatFiles.Length;
        var current = 0;

        foreach (var startBat in startBatFiles)
        {
            current++;
            try
            {
                var dir = Path.GetDirectoryName(startBat)!;
                onProgress?.Invoke(current, total, Path.GetFileName(dir));
                var entry = new ProgramEntry
                {
                    Directory = dir,
                    StartBat = startBat,
                };

                var stopBat = Path.Combine(dir, "stop.bat");
                if (File.Exists(stopBat)) entry.StopBat = stopBat;

                var restartBat = Path.Combine(dir, "restart.bat");
                if (File.Exists(restartBat)) entry.RestartBat = restartBat;

                // Read package.json for name
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

            var (command, stopMethod) = ("", "");

            // AI analysis
            if (!string.IsNullOrWhiteSpace(new AIScriptGenerator().ApiKey))
            {
                onProgress?.Invoke(current, total, $"{Path.GetFileName(dir)} (AI分析中...)");
                var aiResult = await ai.AnalyzeProject(dir, startBat);
                if (aiResult.HasValue)
                {
                    command = aiResult.Value.Command;
                    stopMethod = aiResult.Value.StopMethod;
                }
            }

            if (!string.IsNullOrWhiteSpace(command))
                entry.StartCommand = command;
            if (!string.IsNullOrWhiteSpace(stopMethod))
                entry.StopMethod = stopMethod;

            results.Add(new ScanResult(dir, "", entry, false));
            }
            catch { }
        }

        return results;
    }
}
