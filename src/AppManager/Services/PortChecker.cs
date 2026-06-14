using System.Diagnostics;
using System.Net.NetworkInformation;

namespace AppManager.Services;

public class PortChecker
{
    public readonly record struct PortInfo(int Port, bool IsOccupied, string ProcessName, int Pid);

    public bool IsPortOccupied(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        return listeners.Any(l => l.Port == port);
    }

    public PortInfo CheckPort(int port)
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        var listener = listeners.FirstOrDefault(l => l.Port == port);
        if (listener == default)
        {
            return new PortInfo(port, false, "", 0);
        }

        var (processName, pid) = GetProcessForPort(port);
        return new PortInfo(port, true, processName, pid);
    }

    public List<PortInfo> CheckPorts(params int?[] ports)
    {
        return ports.Where(p => p.HasValue).Select(p => CheckPort(p!.Value)).ToList();
    }

    public List<int> SuggestAvailablePorts(int targetPort, int count = 3, int range = 10)
    {
        var occupied = new HashSet<int>(
            IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Select(l => l.Port));

        var suggestions = new List<int>();
        for (int offset = 1; offset <= range; offset++)
        {
            if (suggestions.Count >= count) break;
            var above = targetPort + offset;
            if (above > 0 && above <= 65535 && !occupied.Contains(above) && !suggestions.Contains(above))
                suggestions.Add(above);

            if (suggestions.Count >= count) break;
            var below = targetPort - offset;
            if (below > 0 && below <= 65535 && !occupied.Contains(below) && !suggestions.Contains(below))
                suggestions.Add(below);
        }
        return suggestions;
    }

    private static (string ProcessName, int Pid) GetProcessForPort(int port)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            foreach (var line in output.Split('\n'))
            {
                if (line.Contains($":{port} ") && line.Contains("LISTENING"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid))
                    {
                        try
                        {
                            var p = Process.GetProcessById(pid);
                            return (p.ProcessName, pid);
                        }
                        catch
                        {
                            return ("unknown", pid);
                        }
                    }
                }
            }
        }
        catch { }
        return ("unknown", 0);
    }
}
