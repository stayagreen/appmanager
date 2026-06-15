using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AppManager.Services;

public class AIScriptGenerator
{
    private readonly HttpClient _http;
    private const string ApiUrl = "https://opencode.ai/zen/go/v1/chat/completions";
    private readonly string _apiKey;

    public AIScriptGenerator()
    {
        var configKey = AppConfig.Load().OpenCodeApiKey;
        _apiKey = !string.IsNullOrWhiteSpace(configKey)
            ? configKey
            : "sk-inEoQQxSJfivJEftKNiIqaKK3By7uMHL9yF7qMkpSNve2mpOYgZpClnScS1XCT4b";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public readonly record struct AIScanResult(
        string Command,
        string StopMethod,
        int? ApiPort,
        int? WebPort,
        int? WsPort,
        string LoginUrl
    );

    public async Task<AIScanResult?> AnalyzeProject(string projectDir, string existingStartBat)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return null;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"项目目录: {projectDir}");

            var packageJson = Path.Combine(projectDir, "package.json");
            if (File.Exists(packageJson))
                sb.AppendLine($"package.json:\n{File.ReadAllText(packageJson)}");

            if (File.Exists(existingStartBat))
                sb.AppendLine($"start.bat:\n{File.ReadAllText(existingStartBat)}");

            var stopBat = Path.Combine(projectDir, "stop.bat");
            if (File.Exists(stopBat))
                sb.AppendLine($"stop.bat:\n{File.ReadAllText(stopBat)}");

            // Read server source files for port detection
            foreach (var pattern in new[] { "server.*", "index.*", "app.*", "main.*" })
            {
                foreach (var f in Directory.GetFiles(projectDir, pattern, SearchOption.TopDirectoryOnly))
                {
                    if (f.EndsWith(".ts") || f.EndsWith(".js") || f.EndsWith(".py") || f.EndsWith(".go"))
                    {
                        var content = File.ReadAllText(f);
                        if (content.Length < 5000)
                            sb.AppendLine($"{Path.GetFileName(f)}:\n{content}");
                    }
                }
            }

            // Also check subdirectories for server files
            foreach (var sub in new[] { "server", "src", "app" })
            {
                var subDir = Path.Combine(projectDir, sub);
                if (!Directory.Exists(subDir)) continue;
                foreach (var f in Directory.GetFiles(subDir, "*", SearchOption.AllDirectories))
                {
                    if ((f.EndsWith(".ts") || f.EndsWith(".js")) && !f.Contains("node_modules"))
                    {
                        var content = File.ReadAllText(f);
                        if (content.Length < 5000)
                            sb.AppendLine($"{Path.GetFileName(sub)}/{Path.GetFileName(f)}:\n{content}");
                    }
                }
            }
            foreach (var cfg in new[] { ".env", ".env.example", "vite.config.ts", "vite.config.js" })
            {
                var p = Path.Combine(projectDir, cfg);
                if (File.Exists(p))
                {
                    var content = File.ReadAllText(p);
                    if (content.Length < 3000)
                        sb.AppendLine($"{cfg}:\n{content}");
                }
            }

            if (sb.Length < 100) return null;

            var request = new
            {
                model = "mimo-v2.5",
                messages = new[]
                {
                    new {
                        role = "system",
                        content = """分析项目，返回严格JSON：{"command":"启动命令","stopMethod":"停止方法","apiPort":端口,"webPort":端口,"wsPort":端口,"loginUrl":"地址"}。command必须从start.bat和package.json中提取真实命令（如npm run dev、node server.js、python app.py），不要用start.bat本身。stopMethod: port-端口号 或 taskkill-进程名。端口从源码中查找listen/port定义。loginUrl用webPort或apiPort拼。端口不存在填0。"""
                    },
                    new { role = "user", content = sb.ToString() }
                },
                max_tokens = 2000,
                temperature = 0
            };

            var json = JsonSerializer.Serialize(request);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(ApiUrl, httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseBody);
            var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

            var reply = "";
            if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                reply = c.GetString() ?? "";

            if (string.IsNullOrWhiteSpace(reply)) return null;

            var cleanJson = reply.Trim();
            if (cleanJson.StartsWith("```"))
            {
                var idx = cleanJson.IndexOf('{');
                var endIdx = cleanJson.LastIndexOf('}');
                if (idx >= 0 && endIdx > idx)
                    cleanJson = cleanJson.Substring(idx, endIdx - idx + 1);
            }

            using var resultDoc = JsonDocument.Parse(cleanJson);
            var root = resultDoc.RootElement;

            return new AIScanResult(
                root.TryGetProperty("command", out var cmd) ? cmd.GetString() ?? "" : "",
                root.TryGetProperty("stopMethod", out var sm) ? sm.GetString() ?? "" : "",
                root.TryGetProperty("apiPort", out var ap) && ap.ValueKind == JsonValueKind.Number ? ap.GetInt32() : null,
                root.TryGetProperty("webPort", out var wp) && wp.ValueKind == JsonValueKind.Number ? wp.GetInt32() : null,
                root.TryGetProperty("wsPort", out var wsp) && wsp.ValueKind == JsonValueKind.Number ? wsp.GetInt32() : null,
                root.TryGetProperty("loginUrl", out var lu) ? lu.GetString() ?? "" : ""
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AI analysis failed: {ex.Message}");
            return new AIScanResult("", "", null, null, null, "");
        }
    }
}
