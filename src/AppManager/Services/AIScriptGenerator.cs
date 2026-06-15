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
        _apiKey = AppConfig.Load().OpenCodeApiKey;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public readonly record struct AIScanResult(
        string Command,
        string StopMethod,
        string? WorkingDir
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

            // Also check common config files
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
                model = "deepseek-v4-pro",
                messages = new[]
                {
                    new {
                        role = "system",
                        content = """你是一个项目分析专家。根据提供的项目文件，分析这个项目的启动方式。返回严格的 JSON 格式（不要markdown，不要解释）：{"command":"启动命令","stopMethod":"停止方法"}。command是完整的启动命令。stopMethod格式只能是以下之一：port-端口号（如port-7000）表示通过端口杀进程；taskkill-进程名（如taskkill-node.exe）表示通过进程名杀进程。只返回JSON。"""
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
            if (string.IsNullOrWhiteSpace(reply) &&
                msg.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                reply = rc.GetString() ?? "";

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
                root.GetProperty("command").GetString() ?? "",
                root.TryGetProperty("stopMethod", out var sm) ? sm.GetString() ?? "" : "",
                root.TryGetProperty("workingDir", out var wd) ? wd.GetString() : null
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AI analysis failed: {ex.Message}");
            return null;
        }
    }
}
