using System.Diagnostics;
using System.IO;
using System.Text;
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

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{entry.StartBat}\"",
            WorkingDirectory = Path.GetDirectoryName(entry.StartBat) ?? entry.Directory,
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
            proc.Dispose();
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
