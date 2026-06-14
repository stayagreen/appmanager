using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        // Find all start.bat files — that's our main trigger
        var startBatFiles = Directory.GetFiles(rootPath, "start.bat", SearchOption.AllDirectories);

        foreach (var startBat in startBatFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(startBat)!;
                var entry = AutoDetect(dir, startBat);

                if (string.IsNullOrWhiteSpace(entry.Name))
                    entry.Name = Path.GetFileName(dir);

                results.Add(new ScanResult(dir, "", entry, false));
            }
            catch { }
        }

        // Also pick up app.json files if present (supplemental)
        var appJsonFiles = Directory.GetFiles(rootPath, "app.json", SearchOption.AllDirectories);
        foreach (var jsonPath in appJsonFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(jsonPath)!;
                if (results.Any(r => r.Directory.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var json = File.ReadAllText(jsonPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var entry = new ProgramEntry
                {
                    Directory = dir,
                    Name = GetStringProp(root, "name") ?? Path.GetFileName(dir),
                    ApiPort = GetIntProp(root, "apiPort"),
                    WebPort = GetIntProp(root, "webPort"),
                    WsPort = GetIntProp(root, "wsPort"),
                    LoginUrl = GetStringProp(root, "loginUrl") ?? "",
                };

                var sb = GetStringProp(root, "startBat");
                if (sb != null) entry.StartBat = Path.GetFullPath(Path.Combine(dir, sb));

                var stb = GetStringProp(root, "stopBat");
                if (stb != null) entry.StopBat = Path.GetFullPath(Path.Combine(dir, stb));

                var rb = GetStringProp(root, "restartBat");
                if (rb != null) entry.RestartBat = Path.GetFullPath(Path.Combine(dir, rb));

                results.Add(new ScanResult(dir, jsonPath, entry, false));
            }
            catch { }
        }

        return results;
    }

    private static ProgramEntry AutoDetect(string dir, string startBat)
    {
        var entry = new ProgramEntry
        {
            Directory = dir,
            StartBat = startBat,
        };

        // Detect bat files
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
                    entry.Name = nameProp.GetString() ?? "";
            }
            catch { }
        }

        // Detect ports from .env
        var envFile = Path.Combine(dir, ".env");
        if (File.Exists(envFile))
        {
            try
            {
                var envLines = File.ReadAllLines(envFile);
                foreach (var line in envLines)
                {
                    var match = Regex.Match(line, @"^\s*(?:export\s+)?(PORT|BROWSER_PORT|API_PORT|WS_PORT|VITE_PORT|HMR_PORT)\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                    if (!match.Success) continue;

                    var key = match.Groups[1].Value.ToUpper();
                    var val = int.Parse(match.Groups[2].Value);

                    switch (key)
                    {
                        case "PORT": entry.ApiPort ??= val; break;
                        case "BROWSER_PORT": entry.WebPort ??= val; break;
                        case "API_PORT": entry.ApiPort ??= val; break;
                        case "WS_PORT": entry.WsPort ??= val; break;
                        case "VITE_PORT": entry.WebPort ??= val; break;
                        case "HMR_PORT": entry.WsPort ??= val; break;
                    }
                }
            }
            catch { }
        }

        // Also check .env.example
        var envExample = Path.Combine(dir, ".env.example");
        if (File.Exists(envExample))
        {
            try
            {
                var lines = File.ReadAllLines(envExample);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"^\s*#?\s*(PORT|BROWSER_PORT|API_PORT|WS_PORT|VITE_PORT|HMR_PORT)\s*[=:]\s*(\d+)", RegexOptions.IgnoreCase);
                    if (!match.Success) continue;

                    var key = match.Groups[1].Value.ToUpper();
                    var val = int.Parse(match.Groups[2].Value);

                    switch (key)
                    {
                        case "PORT": entry.ApiPort ??= val; break;
                        case "BROWSER_PORT": entry.WebPort ??= val; break;
                        case "API_PORT": entry.ApiPort ??= val; break;
                        case "WS_PORT": entry.WsPort ??= val; break;
                        case "VITE_PORT": entry.WebPort ??= val; break;
                        case "HMR_PORT": entry.WsPort ??= val; break;
                    }
                }
            }
            catch { }
        }

        // Scan source files for port definitions (server.ts, server.js, etc.)
        var sourceFiles = Directory.GetFiles(dir, "*.ts", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(dir, "*.js", SearchOption.TopDirectoryOnly));
        foreach (var sf in sourceFiles)
        {
            try
            {
                var content = File.ReadAllText(sf);
                ScanSourceForPorts(content, entry);
            }
            catch { }
        }

        // Try vite configs
        var viteConfigs = new[] { "vite.config.ts", "vite.config.js" };
        foreach (var vc in viteConfigs)
        {
            var vcPath = Path.Combine(dir, vc);
            if (!File.Exists(vcPath)) continue;

            try
            {
                var content = File.ReadAllText(vcPath);

                // Find port that is NOT inside an hmr block
                var portMatches = Regex.Matches(content, @"port\s*:\s*(\d+)");
                foreach (Match m in portMatches.Cast<Match>())
                {
                    var before = content.Substring(0, m.Index);
                    var lastHmr = before.LastIndexOf("hmr", StringComparison.Ordinal);
                    var lastServer = before.LastIndexOf("server", StringComparison.Ordinal);
                    if (lastHmr > lastServer) continue;

                    entry.WebPort ??= int.Parse(m.Groups[1].Value);
                    break;
                }

                var hmrPortMatch = Regex.Match(content, @"hmr\s*:.*?port\s*:\s*(\d+)", RegexOptions.Singleline);
                if (hmrPortMatch.Success) entry.WsPort ??= int.Parse(hmrPortMatch.Groups[1].Value);
            }
            catch { }
        }

        // Auto-construct login URL
        if (string.IsNullOrWhiteSpace(entry.LoginUrl) && entry.WebPort.HasValue)
            entry.LoginUrl = $"http://localhost:{entry.WebPort.Value}";
        else if (string.IsNullOrWhiteSpace(entry.LoginUrl) && entry.ApiPort.HasValue)
            entry.LoginUrl = $"http://localhost:{entry.ApiPort.Value}";

        return entry;
    }

    private static void ScanSourceForPorts(string content, ProgramEntry entry)
    {
        // Pattern: const PORT = Number(process.env.PORT) || 6000
        var portValue = Regex.Match(content, @"PORT\s*[=:]\s*(?:Number\([^)]+\)\s*\|\|\s*)?(\d{4,5})", RegexOptions.IgnoreCase);
        if (portValue.Success && !entry.ApiPort.HasValue)
            entry.ApiPort = int.Parse(portValue.Groups[1].Value);

        // Pattern: const BROWSER_PORT = Number(process.env.BROWSER_PORT) || 6001
        var browserValue = Regex.Match(content, @"BROWSER_PORT\s*[=:]\s*(?:Number\([^)]+\)\s*\|\|\s*)?(\d{4,5})", RegexOptions.IgnoreCase);
        if (browserValue.Success && !entry.WebPort.HasValue)
            entry.WebPort = int.Parse(browserValue.Groups[1].Value);

        var wsValue = Regex.Match(content, @"WS_PORT\s*[=:]\s*(?:Number\([^)]+\)\s*\|\|\s*)?(\d{4,5})", RegexOptions.IgnoreCase);
        if (wsValue.Success && !entry.WsPort.HasValue)
            entry.WsPort = int.Parse(wsValue.Groups[1].Value);
    }

    private static string? GetStringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetIntProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
}
