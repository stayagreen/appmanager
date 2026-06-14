using System.Diagnostics;
using System.IO;
using AppManager.Models;

namespace AppManager.Services;

public class ProcessService
{
    public bool IsProgramRunning(ProgramEntry entry)
    {
        return Process.GetProcesses()
            .Any(p =>
            {
                try { return p.MainWindowTitle.Contains(entry.WindowTitle) && !p.HasExited; }
                catch { return false; }
            });
    }

    public void Start(ProgramEntry entry)
    {
        if (!File.Exists(entry.StartBat))
        {
            throw new FileNotFoundException($"Start bat not found: {entry.StartBat}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"{entry.WindowTitle}\" /min cmd /c \"{entry.StartBat}\"",
            WorkingDirectory = Path.GetDirectoryName(entry.StartBat) ?? entry.Directory,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        Process.Start(startInfo);
    }

    public void Stop(ProgramEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.StopBat) && File.Exists(entry.StopBat))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{entry.StopBat}\"",
                WorkingDirectory = Path.GetDirectoryName(entry.StopBat) ?? entry.Directory,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(startInfo);
            proc?.WaitForExit(10000);
        }

        KillByWindowTitle(entry.WindowTitle);
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

    private static void KillByWindowTitle(string windowTitle)
    {
        var processes = Process.GetProcesses()
            .Where(p =>
            {
                try { return p.MainWindowTitle.Contains(windowTitle) && !p.HasExited; }
                catch { return false; }
            })
            .ToList();

        foreach (var p in processes)
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch { }
        }
    }
}
