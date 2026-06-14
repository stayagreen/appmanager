using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AppManager.Models;

namespace AppManager.Services;

public class ProcessService
{
    private readonly Dictionary<int, Process> _runningProcesses = new();

    public bool IsProgramRunning(ProgramEntry entry)
    {
        if (_runningProcesses.TryGetValue(entry.Id, out var proc))
        {
            try { return !proc.HasExited; }
            catch { return false; }
        }
        return false;
    }

    public void Start(ProgramEntry entry)
    {
        if (!File.Exists(entry.StartBat))
            throw new FileNotFoundException($"Start bat not found: {entry.StartBat}");

        var (command, workDir) = ParseBatFile(entry.StartBat);
        if (string.IsNullOrWhiteSpace(command))
            throw new InvalidOperationException("无法从 bat 文件中提取启动命令");

        if (string.IsNullOrWhiteSpace(workDir))
            workDir = entry.Directory;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            WorkingDirectory = workDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var sb = new StringBuilder();
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
            _runningProcesses.Remove(entry.Id);
            try { proc.Dispose(); } catch { }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        _runningProcesses[entry.Id] = proc;
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
        entry.Status = IsProgramRunning(entry) ? "Running" : "Stopped";
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

                // Extract cd /d path
                var cdMatch = Regex.Match(trimmed, @"^cd\s+/d\s+""?(.+?)""?\s*$", RegexOptions.IgnoreCase);
                if (cdMatch.Success)
                {
                    var cdPath = cdMatch.Groups[1].Value.Trim();
                    // Handle batch variables like %~dp0, %CD%
                    if (cdPath.Contains("%"))
                    {
                        workDir = batDir;
                    }
                    else
                    {
                        workDir = cdPath;
                    }
                    continue;
                }

                // Extract start "{title}" ... cmd /c "{command}"
                var startMatch = Regex.Match(trimmed, @"^start\s+""[^""]*""\s+(?:/[a-z]+\s+)*cmd\s+/c\s+""(.+)""\s*$", RegexOptions.IgnoreCase);
                if (startMatch.Success)
                {
                    return (startMatch.Groups[1].Value, workDir ?? batDir);
                }

                // start "title" ... cmd /c command (no quotes around command)
                var startMatch2 = Regex.Match(trimmed, @"^start\s+""[^""]*""\s+(?:/[a-z]+\s+)*cmd\s+/c\s+(.+)$", RegexOptions.IgnoreCase);
                if (startMatch2.Success)
                {
                    var cmd = startMatch2.Groups[1].Value.Trim('"');
                    return (cmd, workDir ?? batDir);
                }
            }

            // Fallback: look for last non-@echo, non-cd, non-pause, non-timeout line
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("@")) continue;
                if (line.StartsWith("echo", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("cd", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("pause", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("timeout", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("start", StringComparison.OrdinalIgnoreCase)) continue;
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
