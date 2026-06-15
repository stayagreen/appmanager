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

                // Use folder name as default project name
                entry.Name = Path.GetFileName(dir);

            var (command, stopMethod) = ("", "");

            // AI analysis
            if (ai.HasApiKey)
            {
                onProgress?.Invoke(current, total, $"{Path.GetFileName(dir)} (AI分析中...)");
                try
                {
                    var aiResult = await ai.AnalyzeProject(dir, startBat);
                    if (aiResult.HasValue)
                    {
                        command = aiResult.Value.Command;
                        stopMethod = aiResult.Value.StopMethod;
                        if (aiResult.Value.ApiPort > 0) entry.ApiPort = aiResult.Value.ApiPort;
                        if (aiResult.Value.WebPort > 0) entry.WebPort = aiResult.Value.WebPort;
                        if (aiResult.Value.WsPort > 0) entry.WsPort = aiResult.Value.WsPort;
                        if (!string.IsNullOrWhiteSpace(aiResult.Value.LoginUrl)) entry.LoginUrl = aiResult.Value.LoginUrl;
                    }
                }
                catch { }
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
