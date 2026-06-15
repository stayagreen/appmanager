using System.Diagnostics;
using System.IO;
using System.Text;
using AppManager.Models;

namespace AppManager.Services;

public class ProcessService
{
    private readonly Dictionary<int, Process> _runningProcesses = new();
    private readonly PortChecker _portChecker;
    private HashSet<int>? _beforePorts;

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
        if (entry.ApiPort.HasValue && _portChecker.IsPortOccupied(entry.ApiPort.Value)) return true;
        if (entry.WebPort.HasValue && _portChecker.IsPortOccupied(entry.WebPort.Value)) return true;
        if (entry.WsPort.HasValue && _portChecker.IsPortOccupied(entry.WsPort.Value)) return true;
        return false;
    }

    public void Start(ProgramEntry entry)
    {
        if (!File.Exists(entry.StartBat))
            throw new FileNotFoundException($"Start bat not found: {entry.StartBat}");

        if (IsListeningOnPorts(entry))
        {
            entry.LogOutput = "[程序已在运行中（端口被占用）]\r\n";
            return;
        }

        var workDir = Path.GetDirectoryName(entry.StartBat) ?? entry.Directory;
        _beforePorts = _portChecker.GetActivePorts();

        // Use AI-detected command if available, otherwise run the bat directly
        var executable = string.IsNullOrWhiteSpace(entry.StartCommand)
            ? $"\"{entry.StartBat}\""
            : $"/c {entry.StartCommand}";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = executable,
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
        if (!string.IsNullOrWhiteSpace(entry.StartCommand))
        {
            sb.AppendLine($"[AI命令] {entry.StartCommand}");
            sb.AppendLine($"[停止方式] {entry.StopMethod}");
        }
        sb.AppendLine(string.IsNullOrWhiteSpace(entry.StartCommand)
            ? $"[执行] {entry.StartBat}"
            : $"[执行] {entry.StartCommand}");
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

        var beforeSnapshot = _beforePorts;
        entry.LogOutput += $"\r\n[端口检测] 启动前: {string.Join(",", beforeSnapshot)}\r\n";
        entry.LogOutput += $"[端口检测] 启动后: {string.Join(",", afterPorts)}\r\n";
        entry.LogOutput += $"[端口检测] 新增端口: {string.Join(",", newPorts)}\r\n";

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

        // Handle AI-detected stop method
        if (!string.IsNullOrWhiteSpace(entry.StopMethod))
        {
            if (entry.StopMethod.StartsWith("port-"))
            {
                var port = entry.StopMethod.Substring(5);
                KillByPort(port);
            }
            else if (entry.StopMethod.StartsWith("taskkill-"))
            {
                var procName = entry.StopMethod.Substring(9);
                KillByProcessName(procName);
            }
            else
            {
                // Use as direct kill command
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {entry.StopMethod}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
            }
        }

        KillProcessTree(entry);
    }

    private static void KillByPort(string port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c for /f \"tokens=5\" %a in ('netstat -ano ^| findstr :{port} ^| findstr LISTENING') do taskkill /f /pid %a",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch { }
    }

    private static void KillByProcessName(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/f /im {name}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch { }
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

        // Auto-detect ports if program is running via ports but ports aren't configured
        if (running && !entry.ApiPort.HasValue && !entry.WebPort.HasValue)
        {
            var activePorts = _portChecker.GetActivePorts();
            if (activePorts.Count > 0 && _beforePorts != null)
            {
                var newPorts = activePorts.Except(_beforePorts).OrderBy(p => p).ToList();
                if (newPorts.Count > 0)
                {
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
                        int? ws = null;
                        for (int i = newPorts.Count - 1; i >= 0; i--)
                        {
                            if (i == 0 || newPorts[i] - newPorts[i - 1] > 1000)
                            {
                                ws = newPorts[i];
                                newPorts.RemoveAt(i);
                                break;
                            }
                        }
                        if (ws.HasValue) entry.WsPort = ws.Value;
                        if (newPorts.Count >= 2) { entry.ApiPort = newPorts[0]; entry.WebPort = newPorts[1]; }
                        else if (newPorts.Count == 1) { entry.ApiPort = newPorts[0]; entry.WebPort = newPorts[0]; }
                    }
                    if (entry.WebPort.HasValue)
                        entry.LoginUrl = $"http://localhost:{entry.WebPort.Value}";
                    _beforePorts = null;
                }
            }
        }
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
