using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AppManager.Models;

namespace AppManager.Services;

public class ProcessService
{
    private readonly Dictionary<int, Process> _runningProcesses = new();
    private readonly PortChecker _portChecker;

    public ProcessService(PortChecker portChecker)
    {
        _portChecker = portChecker;
    }

    public bool IsProgramRunning(ProgramEntry entry)
    {
        if (_runningProcesses.TryGetValue(entry.Id, out var proc))
        {
            try { return !proc.HasExited; }
            catch { }
        }

        return IsListeningOnPorts(entry);
    }

    private bool IsListeningOnPorts(ProgramEntry entry)
    {
        if (entry.ApiPort.HasValue && _portChecker.IsPortOccupied(entry.ApiPort.Value))
            return true;
        if (entry.WebPort.HasValue && _portChecker.IsPortOccupied(entry.WebPort.Value))
            return true;
        if (entry.WsPort.HasValue && _portChecker.IsPortOccupied(entry.WsPort.Value))
            return true;
        return false;
    }

    public bool IsManagedByUs(ProgramEntry entry)
    {
        return _runningProcesses.ContainsKey(entry.Id);
    }

    private HashSet<int>? _beforePorts;

    public void Start(ProgramEntry entry)
    {
        if (!File.Exists(entry.StartBat))
            throw new FileNotFoundException($"Start bat not found: {entry.StartBat}");

        if (IsListeningOnPorts(entry))
        {
            entry.LogOutput = "[程序已在运行中（端口被占用）]\r\n";
            return;
        }

        var (command, workDir) = ParseBatFile(entry.StartBat);
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("无法从 bat 文件中提取启动命令");

        if (string.IsNullOrWhiteSpace(workDir))
            workDir = entry.Directory;

        _beforePorts = _portChecker.GetActivePorts();

        // If parsed command looks like a short setup line, run the entire bat instead
        string executableCmd;
        if (command.Length < 15 ||
            command.StartsWith("set ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("call ", StringComparison.OrdinalIgnoreCase))
        {
            executableCmd = $"\"{entry.StartBat}\"";
        }
        else
        {
            executableCmd = command;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {executableCmd}",
            WorkingDirectory = workDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var sb = new StringBuilder();
        sb.AppendLine($"[工作目录] {workDir}");
        if (executableCmd == command)
            sb.AppendLine($"[执行命令] {command}");
        else
            sb.AppendLine($"[执行] 直接运行 bat: {entry.StartBat}");
        sb.AppendLine("");
        entry.LogOutput = sb.ToString();

        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                sb.AppendLine(e.Data);
                entry.LogOutput = sb.ToString();
            }
        };
        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                sb.AppendLine(e.Data);
                entry.LogOutput = sb.ToString();
            }
        };

        proc.Exited += (s, e) =>
        {
            sb.AppendLine($"[进程已退出，退出码: {proc.ExitCode}]");
            entry.LogOutput = sb.ToString();
            _runningProcesses.Remove(entry.Id);
            try { proc.Dispose(); } catch { }
        };

        proc.Start();
        proc.StandardInput.Close();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        _runningProcesses[entry.Id] = proc;
    }

    public void DetectPortsAfterStart(ProgramEntry entry)
    {
        if (_beforePorts == null) return;

        var afterPorts = _portChecker.GetActivePorts();
        var newPorts = afterPorts.Except(_beforePorts).OrderBy(p => p).ToList();
        _beforePorts = null;

        if (newPorts.Count == 0) return;

        if (newPorts.Count == 1)
        {
            entry.ApiPort = newPorts[0];
            entry.WebPort = newPorts[0];
        }
        else if (newPorts.Count == 2)
        {
            entry.ApiPort = newPorts[0];
            entry.WebPort = newPorts[1];
        }
        else
        {
            // 3+ ports: find WS port (significantly larger outlier)
            int? wsCandidate = null;
            for (int i = newPorts.Count - 1; i >= 0; i--)
            {
                if (i == 0 || newPorts[i] - newPorts[i - 1] > 1000)
                {
                    wsCandidate = newPorts[i];
                    newPorts.RemoveAt(i);
                    break;
                }
            }
            if (wsCandidate.HasValue)
                entry.WsPort = wsCandidate.Value;

            if (newPorts.Count >= 2)
            {
                entry.ApiPort = newPorts[0];
                entry.WebPort = newPorts[1];
            }
            else if (newPorts.Count == 1)
            {
                entry.ApiPort = newPorts[0];
                entry.WebPort = newPorts[0];
            }
        }

        // Auto-generate login URL
        if (entry.WebPort.HasValue)
            entry.LoginUrl = $"http://localhost:{entry.WebPort.Value}";
    }

    public void Stop(ProgramEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.StopBat) && File.Exists(entry.StopBat))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{entry.StopBat}\"",
                WorkingDirectory = Path.GetDirectoryName(entry.StopBat) ?? entry.Directory,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
        }

        KillProcessTree(entry);
    }

    public void Restart(ProgramEntry entry)
    {
        Stop(entry);
        Thread.Sleep(2000);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (IsProgramRunning(entry))
        {
            if (DateTime.UtcNow > deadline) break;
            Thread.Sleep(500);
        }

        Start(entry);
    }

    public void RefreshStatus(ProgramEntry entry)
    {
        var running = IsProgramRunning(entry);
        entry.Status = running ? "Running" : "Stopped";

        if (running && !_runningProcesses.ContainsKey(entry.Id))
        {
            if (string.IsNullOrWhiteSpace(entry.LogOutput))
                entry.LogOutput = "[程序在外部启动，无法捕获日志]\r\n";
        }
    }

    private static (string? command, string? workDir) ParseBatFile(string batPath)
    {
        try
        {
            var lines = File.ReadAllLines(batPath);
            string? workDir = null;
            var batDir = Path.GetDirectoryName(batPath) ?? "";

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                var cdMatch = Regex.Match(trimmed, @"^cd\s+/d\s+""?(.+?)""?\s*$", RegexOptions.IgnoreCase);
                if (cdMatch.Success)
                {
                    var cdPath = cdMatch.Groups[1].Value.Trim();
                    workDir = cdPath.Contains("%") ? batDir : cdPath;
                    continue;
                }

                var startMatch = Regex.Match(trimmed, @"^start\s+""[^""]*""\s+(?:/[a-z]+\s+)*cmd\s+/c\s+""(.+)""\s*$", RegexOptions.IgnoreCase);
                if (startMatch.Success)
                {
                    return (startMatch.Groups[1].Value, workDir ?? batDir);
                }

                var startMatch2 = Regex.Match(trimmed, @"^start\s+""[^""]*""\s+(?:/[a-z]+\s+)*cmd\s+/c\s+(.+)$", RegexOptions.IgnoreCase);
                if (startMatch2.Success)
                {
                    var cmd = startMatch2.Groups[1].Value.Trim('"');
                    return (cmd, workDir ?? batDir);
                }
            }

            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("@")) continue;
                if (line.StartsWith("::")) continue;
                if (line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("echo", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("cd", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("pause", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("timeout", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("start", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("set ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("if ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("goto ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("call ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("title ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("exit", StringComparison.OrdinalIgnoreCase)) continue;
                return (line, workDir ?? batDir);
            }
        }
        catch { }

        return (null, null);
    }

    private void KillProcessTree(ProgramEntry entry)
    {
        if (_runningProcesses.TryGetValue(entry.Id, out var proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(5000);
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
            _runningProcesses.Remove(entry.Id);
        }
    }
}
