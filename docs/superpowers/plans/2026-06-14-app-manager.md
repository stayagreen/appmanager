# App Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a WPF desktop application to centrally manage multiple local dev services with start/stop/restart control, port conflict detection, real-time status monitoring, and system tray support.

**Architecture:** MVVM pattern with CommunityToolkit.Mvvm, SQLite for persistence. Services layer handles process management, port checking, and directory scanning. MainViewModel orchestrates the UI bound to MainWindow via data binding.

**Tech Stack:** WPF .NET 8, C# 12, CommunityToolkit.Mvvm 8.x, Microsoft.Data.Sqlite 8.x

---

### Task 1: Create WPF Project and Install Dependencies

**Files:**
- Create: `src/AppManager/AppManager.csproj`
- Create: `AppManager.sln`

- [ ] **Step 1: Create solution and project**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet new sln -n AppManager
New-Item -ItemType Directory -Path "src\AppManager" -Force
Set-Location -LiteralPath "src\AppManager"
dotnet new wpf -n AppManager --framework net8.0
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet sln add src\AppManager\AppManager.csproj
```

- [ ] **Step 2: Add NuGet packages**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager\src\AppManager"
dotnet add package CommunityToolkit.Mvvm --version 8.2.2
dotnet add package Microsoft.Data.Sqlite --version 8.0.0
```

- [ ] **Step 3: Enable Windows Forms compatibility (for NotifyIcon, FolderBrowserDialog)**

Edit `src/AppManager/AppManager.csproj` and ensure these properties exist:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  ...
</Project>
```

- [ ] **Step 4: Create directory structure**

```bash
New-Item -ItemType Directory -Path "src\AppManager\Models" -Force
New-Item -ItemType Directory -Path "src\AppManager\Services" -Force
New-Item -ItemType Directory -Path "src\AppManager\ViewModels" -Force
```

- [ ] **Step 4: Verify project builds**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git init
git add -A
git commit -m "chore: scaffold WPF .NET 8 project with dependencies"
```

---

### Task 2: Create ProgramEntry Model

**Files:**
- Create: `src/AppManager/Models/ProgramEntry.cs`

- [ ] **Step 1: Write ProgramEntry model**

**`src/AppManager/Models/ProgramEntry.cs`:**
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppManager.Models;

public partial class ProgramEntry : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _startBat = string.Empty;

    [ObservableProperty]
    private string _stopBat = string.Empty;

    [ObservableProperty]
    private string _restartBat = string.Empty;

    [ObservableProperty]
    private int? _apiPort;

    [ObservableProperty]
    private int? _webPort;

    [ObservableProperty]
    private int? _wsPort;

    [ObservableProperty]
    private string _loginUrl = string.Empty;

    [ObservableProperty]
    private string _directory = string.Empty;

    [ObservableProperty]
    private string _status = "Stopped";

    [ObservableProperty]
    private int _sortOrder;

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");

    public string WindowTitle => $"{Name}-Server";

    public string StatusIcon => Status switch
    {
        "Running" => "\U0001F7E2",
        "Stopped" => "\U0001F534",
        _ => "\u26AA"
    };

    public bool IsRunning => Status == "Running";
    public bool IsStopped => Status == "Stopped";
}
```

- [ ] **Step 2: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/AppManager/Models/ProgramEntry.cs
git commit -m "feat: add ProgramEntry model with MVVM observable properties"
```

---

### Task 3: Implement DatabaseService

**Files:**
- Create: `src/AppManager/Services/DatabaseService.cs`

- [ ] **Step 1: Write DatabaseService**

**`src/AppManager/Services/DatabaseService.cs`:**
```csharp
using Microsoft.Data.Sqlite;
using AppManager.Models;
using System.Collections.ObjectModel;

namespace AppManager.Services;

public class DatabaseService : IDisposable
{
    private readonly SqliteConnection _connection;

    public DatabaseService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AppManager");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "data.db");

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Programs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                StartBat TEXT,
                StopBat TEXT,
                RestartBat TEXT,
                ApiPort INTEGER,
                WebPort INTEGER,
                WsPort INTEGER,
                LoginUrl TEXT,
                Directory TEXT,
                Status TEXT DEFAULT 'Stopped',
                SortOrder INTEGER DEFAULT 0,
                CreatedAt TEXT,
                UpdatedAt TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public List<ProgramEntry> GetAll()
    {
        var entries = new List<ProgramEntry>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Programs ORDER BY SortOrder, Id";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(MapEntry(reader));
        }
        return entries;
    }

    public ProgramEntry? GetById(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Programs WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return MapEntry(reader);
        }
        return null;
    }

    public void Insert(ProgramEntry entry)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Programs (Name, StartBat, StopBat, RestartBat, ApiPort, WebPort, WsPort,
                LoginUrl, Directory, Status, SortOrder, CreatedAt, UpdatedAt)
            VALUES (@Name, @StartBat, @StopBat, @RestartBat, @ApiPort, @WebPort, @WsPort,
                @LoginUrl, @Directory, @Status, @SortOrder, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """;
        AddParams(cmd, entry);
        entry.Id = Convert.ToInt32((long)cmd.ExecuteScalar()!);
    }

    public void Update(ProgramEntry entry)
    {
        entry.UpdatedAt = DateTime.UtcNow.ToString("o");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Programs SET Name=@Name, StartBat=@StartBat, StopBat=@StopBat,
                RestartBat=@RestartBat, ApiPort=@ApiPort, WebPort=@WebPort,
                WsPort=@WsPort, LoginUrl=@LoginUrl, Directory=@Directory,
                Status=@Status, SortOrder=@SortOrder, UpdatedAt=@UpdatedAt
            WHERE Id = @Id
            """;
        AddParams(cmd, entry);
        cmd.Parameters.AddWithValue("@Id", entry.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Programs WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateStatus(int id, string status)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE Programs SET Status = @Status, UpdatedAt = @UpdatedAt WHERE Id = @Id";
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.ExecuteNonQuery();
    }

    public bool ExistsByName(string name, int? excludeId = null)
    {
        using var cmd = _connection.CreateCommand();
        if (excludeId.HasValue)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Name = @Name AND Id != @Id";
            cmd.Parameters.AddWithValue("@Id", excludeId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Name = @Name";
        }
        cmd.Parameters.AddWithValue("@Name", name);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    public bool ExistsByDirectory(string directory, int? excludeId = null)
    {
        using var cmd = _connection.CreateCommand();
        if (excludeId.HasValue)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Directory = @Dir AND Id != @Id";
            cmd.Parameters.AddWithValue("@Id", excludeId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Programs WHERE Directory = @Dir";
        }
        cmd.Parameters.AddWithValue("@Dir", directory);
        return (long)cmd.ExecuteScalar()! > 0;
    }

    private static ProgramEntry MapEntry(SqliteDataReader reader)
    {
        return new ProgramEntry
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            StartBat = reader.IsDBNull(2) ? "" : reader.GetString(2),
            StopBat = reader.IsDBNull(3) ? "" : reader.GetString(3),
            RestartBat = reader.IsDBNull(4) ? "" : reader.GetString(4),
            ApiPort = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            WebPort = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            WsPort = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            LoginUrl = reader.IsDBNull(8) ? "" : reader.GetString(8),
            Directory = reader.IsDBNull(9) ? "" : reader.GetString(9),
            Status = reader.IsDBNull(10) ? "Stopped" : reader.GetString(10),
            SortOrder = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
            CreatedAt = reader.IsDBNull(12) ? "" : reader.GetString(12),
            UpdatedAt = reader.IsDBNull(13) ? "" : reader.GetString(13),
        };
    }

    private static void AddParams(SqliteCommand cmd, ProgramEntry entry)
    {
        cmd.Parameters.AddWithValue("@Name", entry.Name);
        cmd.Parameters.AddWithValue("@StartBat", (object?)entry.StartBat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StopBat", (object?)entry.StopBat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RestartBat", (object?)entry.RestartBat ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ApiPort", (object?)entry.ApiPort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WebPort", (object?)entry.WebPort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WsPort", (object?)entry.WsPort ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LoginUrl", (object?)entry.LoginUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Directory", (object?)entry.Directory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", entry.Status);
        cmd.Parameters.AddWithValue("@SortOrder", entry.SortOrder);
        cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt);
        cmd.Parameters.AddWithValue("@UpdatedAt", entry.UpdatedAt);
    }

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/AppManager/Services/DatabaseService.cs
git commit -m "feat: add SQLite database service with full CRUD"
```

---

### Task 4: Implement PortChecker Service

**Files:**
- Create: `src/AppManager/Services/PortChecker.cs`

- [ ] **Step 1: Write PortChecker service**

**`src/AppManager/Services/PortChecker.cs`:**
```csharp
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
```

- [ ] **Step 2: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/AppManager/Services/PortChecker.cs
git commit -m "feat: add port checker with conflict detection and suggestions"
```

---

### Task 5: Implement ProcessService

**Files:**
- Create: `src/AppManager/Services/ProcessService.cs`

- [ ] **Step 1: Write ProcessService**

**`src/AppManager/Services/ProcessService.cs`:**
```csharp
using System.Diagnostics;
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
            var proc = Process.Start(startInfo);
            proc?.WaitForExit(10000);
        }

        KillByWindowTitle(entry.WindowTitle);
    }

    public void Restart(ProgramEntry entry)
    {
        Stop(entry);

        Task.Delay(2000).Wait();

        while (IsProgramRunning(entry))
        {
            Task.Delay(500).Wait();
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
```

- [ ] **Step 2: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/AppManager/Services/ProcessService.cs
git commit -m "feat: add process service for start/stop/restart via window title"
```

---

### Task 6: Implement ScannerService

**Files:**
- Create: `src/AppManager/Services/ScannerService.cs`

- [ ] **Step 1: Write ScannerService**

**`src/AppManager/Services/ScannerService.cs`:**
```csharp
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

    public List<ScanResult> ScanDirectory(string rootPath)
    {
        var results = new List<ScanResult>();
        if (!Directory.Exists(rootPath)) return results;

        var appJsonFiles = Directory.GetFiles(rootPath, "app.json", SearchOption.AllDirectories);

        foreach (var jsonPath in appJsonFiles)
        {
            try
            {
                var dir = Path.GetDirectoryName(jsonPath)!;
                var json = File.ReadAllText(jsonPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var entry = new ProgramEntry
                {
                    Directory = dir,
                    Name = GetStringProperty(root, "name") ?? Path.GetFileName(dir),
                    ApiPort = GetIntProperty(root, "apiPort"),
                    WebPort = GetIntProperty(root, "webPort"),
                    WsPort = GetIntProperty(root, "wsPort"),
                    LoginUrl = GetStringProperty(root, "loginUrl") ?? "",
                };

                var startBat = GetStringProperty(root, "startBat");
                if (startBat != null) entry.StartBat = Path.GetFullPath(Path.Combine(dir, startBat));

                var stopBat = GetStringProperty(root, "stopBat");
                if (stopBat != null) entry.StopBat = Path.GetFullPath(Path.Combine(dir, stopBat));

                var restartBat = GetStringProperty(root, "restartBat");
                if (restartBat != null) entry.RestartBat = Path.GetFullPath(Path.Combine(dir, restartBat));

                results.Add(new ScanResult(dir, jsonPath, entry, false));
            }
            catch { }
        }

        return results;
    }

    private static string? GetStringProperty(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int? GetIntProperty(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return null;
    }

    public static ProgramEntry? TryParseAppJson(string jsonPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(jsonPath)!;
            var json = File.ReadAllText(jsonPath);
            var doc = JsonDocument.Parse(json);
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
            return entry;
        }
        catch { return null; }
    }

    private static string? GetStringProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetIntProp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
}
```

- [ ] **Step 2: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/AppManager/Services/ScannerService.cs
git commit -m "feat: add directory scanner for app.json discovery"
```

---

### Task 7: Implement MainViewModel

**Files:**
- Create: `src/AppManager/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Write MainViewModel**

**`src/AppManager/ViewModels/MainViewModel.cs`:**
```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppManager.Models;
using AppManager.Services;
using System.Windows.Threading;

namespace AppManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ProcessService _process;
    private readonly PortChecker _portChecker;
    private readonly ScannerService _scanner;
    private readonly DispatcherTimer _statusTimer;

    public ObservableCollection<ProgramEntry> Programs { get; } = new();

    [ObservableProperty]
    private ProgramEntry? _selectedProgram;

    [ObservableProperty]
    private string _portStatusText = "";

    [ObservableProperty]
    private bool _hasPortConflicts;

    public MainViewModel()
    {
        _db = new DatabaseService();
        _process = new ProcessService();
        _portChecker = new PortChecker();
        _scanner = new ScannerService();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _statusTimer.Tick += OnStatusTimerTick;
        _statusTimer.Start();

        LoadPrograms();
    }

    private void LoadPrograms()
    {
        Programs.Clear();
        foreach (var entry in _db.GetAll())
        {
            Programs.Add(entry);
        }
        RefreshAllStatus();
    }

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        RefreshAllStatus();
    }

    public void RefreshAllStatus()
    {
        foreach (var entry in Programs)
        {
            _process.RefreshStatus(entry);
        }

        if (SelectedProgram != null)
        {
            _process.RefreshStatus(SelectedProgram);
            UpdatePortStatus(SelectedProgram);
        }
    }

    [RelayCommand]
    private void AddProgram()
    {
        var dialog = new ProgramDialog(new ProgramEntry());
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            var entry = dialog.Result!;
            if (_db.ExistsByName(entry.Name))
            {
                MessageBox.Show($"程序 \"{entry.Name}\" 已存在。", "重复", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            entry.CreatedAt = DateTime.UtcNow.ToString("o");
            entry.UpdatedAt = DateTime.UtcNow.ToString("o");
            _db.Insert(entry);
            Programs.Add(entry);
        }
    }

    [RelayCommand]
    private void EditProgram(ProgramEntry? entry)
    {
        if (entry == null) return;
        var dialog = new ProgramDialog(entry);
        dialog.Owner = Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            var updated = dialog.Result!;
            if (_db.ExistsByName(updated.Name, entry.Id))
            {
                MessageBox.Show($"程序 \"{updated.Name}\" 已存在。", "重复", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            updated.Id = entry.Id;
            updated.CreatedAt = entry.CreatedAt;
            updated.Status = entry.Status;
            _db.Update(updated);

            entry.Name = updated.Name;
            entry.StartBat = updated.StartBat;
            entry.StopBat = updated.StopBat;
            entry.RestartBat = updated.RestartBat;
            entry.ApiPort = updated.ApiPort;
            entry.WebPort = updated.WebPort;
            entry.WsPort = updated.WsPort;
            entry.LoginUrl = updated.LoginUrl;
            entry.Directory = updated.Directory;
            entry.SortOrder = updated.SortOrder;
        }
    }

    [RelayCommand]
    private void DeleteProgram(ProgramEntry? entry)
    {
        if (entry == null) return;
        var result = MessageBox.Show(
            $"确定要删除 \"{entry.Name}\" 吗？\n\n这只会删除管理记录，不会删除实际文件。",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            if (entry.IsRunning)
            {
                _process.Stop(entry);
            }
            _db.Delete(entry.Id);
            Programs.Remove(entry);
            if (SelectedProgram == entry)
                SelectedProgram = null;
        }
    }

    [RelayCommand]
    private void StartProgram(ProgramEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.StartBat)) return;

        var conflicts = CheckPortConflicts(entry);
        if (conflicts.Count > 0)
        {
            var msg = "端口冲突检测：\n\n";
            foreach (var c in conflicts)
            {
                var suggestions = _portChecker.SuggestAvailablePorts(c.Port, 3);
                msg += $"端口 {c.Port} 已被占用\n";
                msg += $"  进程: {c.ProcessName} (PID: {c.Pid})\n";
                msg += $"  建议可用端口: {string.Join(", ", suggestions)}\n\n";
            }
            msg += "是否仍然启动？";
            if (MessageBox.Show(msg, "端口冲突", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        try
        {
            _process.Start(entry);
            entry.Status = "Running";
            _db.UpdateStatus(entry.Id, "Running");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void StopProgram(ProgramEntry? entry)
    {
        if (entry == null) return;
        try
        {
            _process.Stop(entry);
            entry.Status = "Stopped";
            _db.UpdateStatus(entry.Id, "Stopped");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RestartProgram(ProgramEntry? entry)
    {
        if (entry == null) return;
        try
        {
            _process.Restart(entry);
            entry.Status = "Running";
            _db.UpdateStatus(entry.Id, "Running");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"重启失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void StartAll()
    {
        foreach (var entry in Programs.Where(p => p.IsStopped))
        {
            StartProgram(entry);
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        foreach (var entry in Programs.Where(p => p.IsRunning))
        {
            StopProgram(entry);
        }
    }

    [RelayCommand]
    private void CopyUrl(ProgramEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.LoginUrl)) return;
        Clipboard.SetText(entry.LoginUrl);
    }

    [RelayCommand]
    private void ScanDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择要扫描的根目录"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var results = _scanner.ScanDirectory(dialog.SelectedPath);
            if (results.Count == 0)
            {
                MessageBox.Show("未发现任何 app.json 文件。", "扫描结果", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var imported = 0;
            foreach (var result in results)
            {
                if (_db.ExistsByDirectory(result.Entry.Directory)) continue;

                result.Entry.CreatedAt = DateTime.UtcNow.ToString("o");
                result.Entry.UpdatedAt = DateTime.UtcNow.ToString("o");
                _db.Insert(result.Entry);
                Programs.Add(result.Entry);
                imported++;
            }

            MessageBox.Show($"从 {results.Count} 个 app.json 中导入了 {imported} 个新程序。\n" +
                $"跳过 {results.Count - imported} 个已存在的程序。", "扫描完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshAllStatus();
    }

    partial void OnSelectedProgramChanged(ProgramEntry? value)
    {
        if (value != null)
        {
            UpdatePortStatus(value);
        }
        else
        {
            PortStatusText = "";
        }
    }

    private void UpdatePortStatus(ProgramEntry entry)
    {
        var conflicts = CheckPortConflicts(entry);
        HasPortConflicts = conflicts.Count > 0;

        if (conflicts.Count == 0 && (entry.ApiPort.HasValue || entry.WebPort.HasValue || entry.WsPort.HasValue))
        {
            PortStatusText = "端口状态: 全部正常";
        }
        else if (conflicts.Count > 0)
        {
            var names = conflicts.Select(c => $"{c.Port}").ToList();
            PortStatusText = $"端口冲突: {string.Join(", ", names)} 已被占用";
        }
        else
        {
            PortStatusText = "";
        }
    }

    private List<PortChecker.PortInfo> CheckPortConflicts(ProgramEntry entry)
    {
        var ports = new List<int>();
        if (entry.ApiPort.HasValue) ports.Add(entry.ApiPort.Value);
        if (entry.WebPort.HasValue) ports.Add(entry.WebPort.Value);
        if (entry.WsPort.HasValue) ports.Add(entry.WsPort.Value);

        return _portChecker.CheckPorts(ports.Cast<int?>().ToArray())
            .Where(p => p.IsOccupied)
            .ToList();
    }

    public int RunningCount => Programs.Count(p => p.IsRunning);
}
```

- [ ] **Step 2: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build will fail because `ProgramDialog` doesn't exist yet. This is expected — it will be created in Task 9.

- [ ] **Step 3: Commit**

```bash
git add src/AppManager/ViewModels/MainViewModel.cs
git commit -m "feat: add MainViewModel with commands and status polling"
```

---

### Task 8: Create ProgramDialog (Add/Edit Window)

**Files:**
- Create: `src/AppManager/Views/ProgramDialog.xaml`
- Create: `src/AppManager/Views/ProgramDialog.xaml.cs`

- [ ] **Step 1: Write ProgramDialog XAML**

**`src/AppManager/Views/ProgramDialog.xaml`:**
```xml
<Window x:Class="AppManager.Views.ProgramDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="程序信息" Height="520" Width="520"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize"
        WindowStyle="ToolWindow">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Text="程序名称" FontWeight="Bold" Margin="0,0,0,4"/>
        <TextBox Grid.Row="1" x:Name="TxtName" Height="28" Margin="0,0,0,10"/>

        <TextBlock Grid.Row="2" Text="项目目录" FontWeight="Bold" Margin="0,0,0,4"/>
        <Grid Grid.Row="3" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtDirectory" Height="28"/>
            <Button Grid.Column="1" Content="..." Width="32" Height="28" Margin="6,0,0,0"
                    Click="BrowseDirectory_Click"/>
        </Grid>

        <TextBlock Grid.Row="4" Text="启动脚本 (start.bat)" FontWeight="Bold" Margin="0,0,0,4"/>
        <Grid Grid.Row="5" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtStartBat" Height="28"/>
            <Button Grid.Column="1" Content="..." Width="32" Height="28" Margin="6,0,0,0"
                    Click="BrowseStart_Click"/>
        </Grid>

        <TextBlock Grid.Row="6" Text="停止脚本 (stop.bat)" FontWeight="Bold" Margin="0,0,0,4"/>
        <Grid Grid.Row="7" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtStopBat" Height="28"/>
            <Button Grid.Column="1" Content="..." Width="32" Height="28" Margin="6,0,0,0"
                    Click="BrowseStop_Click"/>
        </Grid>

        <TextBlock Grid.Row="8" Text="重启脚本 (restart.bat)" FontWeight="Bold" Margin="0,0,0,4"/>
        <Grid Grid.Row="9" Margin="0,0,0,10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox x:Name="TxtRestartBat" Height="28"/>
            <Button Grid.Column="1" Content="..." Width="32" Height="28" Margin="6,0,0,0"
                    Click="BrowseRestart_Click"/>
        </Grid>

        <Grid Grid.Row="10">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <TextBlock Text="API 端口" FontWeight="Bold" Margin="0,0,0,4"/>
            <TextBox Grid.Row="1" x:Name="TxtApiPort" Height="28" Margin="0,0,10,10"/>

            <TextBlock Grid.Column="1" Text="Web 端口" FontWeight="Bold" Margin="10,0,0,4"/>
            <TextBox Grid.Row="1" Grid.Column="1" x:Name="TxtWebPort" Height="28" Margin="10,0,0,10"/>

            <TextBlock Grid.Row="2" Text="WebSocket 端口" FontWeight="Bold" Margin="0,0,0,4"/>
            <TextBox Grid.Row="3" x:Name="TxtWsPort" Height="28" Margin="0,0,10,10"/>

            <TextBlock Grid.Row="2" Grid.Column="1" Text="登录地址" FontWeight="Bold" Margin="10,0,0,4"/>
            <TextBox Grid.Row="3" Grid.Column="1" x:Name="TxtLoginUrl" Height="28" Margin="10,0,0,10"/>
        </Grid>

        <Grid Grid.Row="11" HorizontalAlignment="Right" Margin="0,15,0,0">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <Button Content="确定" Width="80" Height="30" Click="Ok_Click"
                    Background="#0078D4" Foreground="White" BorderThickness="0"/>
            <Button Grid.Column="1" Content="取消" Width="80" Height="30" Margin="10,0,0,0"
                    Click="Cancel_Click"/>
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 2: Write ProgramDialog code-behind**

**`src/AppManager/Views/ProgramDialog.xaml.cs`:**
```csharp
using System.Windows;
using AppManager.Models;
using Microsoft.Win32;

namespace AppManager.Views;

public partial class ProgramDialog : Window
{
    public ProgramEntry? Result { get; private set; }
    private readonly ProgramEntry _original;

    public ProgramDialog(ProgramEntry entry)
    {
        InitializeComponent();
        _original = entry;

        TxtName.Text = entry.Name;
        TxtDirectory.Text = entry.Directory;
        TxtStartBat.Text = entry.StartBat;
        TxtStopBat.Text = entry.StopBat;
        TxtRestartBat.Text = entry.RestartBat;
        TxtApiPort.Text = entry.ApiPort?.ToString() ?? "";
        TxtWebPort.Text = entry.WebPort?.ToString() ?? "";
        TxtWsPort.Text = entry.WsPort?.ToString() ?? "";
        TxtLoginUrl.Text = entry.LoginUrl;

        Title = entry.Id == 0 ? "添加程序" : "编辑程序";
        TxtName.Focus();
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtDirectory.Text = dialog.SelectedPath;
        }
    }

    private void BrowseStart_Click(object sender, RoutedEventArgs e)
    {
        BrowseBat(TxtStartBat);
    }

    private void BrowseStop_Click(object sender, RoutedEventArgs e)
    {
        BrowseBat(TxtStopBat);
    }

    private void BrowseRestart_Click(object sender, RoutedEventArgs e)
    {
        BrowseBat(TxtRestartBat);
    }

    private void BrowseBat(System.Windows.Controls.TextBox target)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "批处理文件|*.bat|所有文件|*.*",
            Title = "选择 bat 脚本"
        };
        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("请输入程序名称。", "验证", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtName.Focus();
            return;
        }

        Result = new ProgramEntry
        {
            Id = _original.Id,
            Name = name,
            Directory = TxtDirectory.Text.Trim(),
            StartBat = TxtStartBat.Text.Trim(),
            StopBat = TxtStopBat.Text.Trim(),
            RestartBat = TxtRestartBat.Text.Trim(),
            ApiPort = ParseNullableInt(TxtApiPort.Text),
            WebPort = ParseNullableInt(TxtWebPort.Text),
            WsPort = ParseNullableInt(TxtWsPort.Text),
            LoginUrl = TxtLoginUrl.Text.Trim(),
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static int? ParseNullableInt(string text)
    {
        if (int.TryParse(text.Trim(), out var val)) return val;
        return null;
    }
}
```

- [ ] **Step 3: Build to verify**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/AppManager/Views/ProgramDialog.xaml src/AppManager/Views/ProgramDialog.xaml.cs
git commit -m "feat: add program dialog for add/edit operations"
```

---

### Task 9: Build MainWindow UI

**Files:**
- Create: `src/AppManager/MainWindow.xaml` (replace default)
- Create: `src/AppManager/MainWindow.xaml.cs` (replace default)

- [ ] **Step 1: Write MainWindow XAML**

**`src/AppManager/MainWindow.xaml`:**
```xml
<Window x:Class="AppManager.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:AppManager.ViewModels"
        xmlns:models="clr-namespace:AppManager.Models"
        Title="程序管理器" Height="600" Width="1000"
        MinHeight="400" MinWidth="700"
        WindowStartupLocation="CenterScreen"
        Closing="Window_Closing">
    <Window.Resources>
        <Style x:Key="ActionButton" TargetType="Button">
            <Setter Property="Height" Value="30"/>
            <Setter Property="MinWidth" Value="70"/>
            <Setter Property="Margin" Value="0,0,8,0"/>
            <Setter Property="Padding" Value="12,0"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Background" Value="#0078D4"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>
        <Style x:Key="IconButton" TargetType="Button">
            <Setter Property="Height" Value="28"/>
            <Setter Property="Width" Value="28"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>
        <Style x:Key="PortOk" TargetType="TextBlock">
            <Setter Property="Foreground" Value="#228B22"/>
            <Setter Property="FontWeight" Value="Bold"/>
        </Style>
        <Style x:Key="PortConflict" TargetType="TextBlock">
            <Setter Property="Foreground" Value="#DC143C"/>
            <Setter Property="FontWeight" Value="Bold"/>
        </Style>
    </Window.Resources>

    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="280"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Left Panel -->
        <Border Grid.Column="0" Background="#F5F5F5">
            <Grid Margin="10">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                    <Button Content="+ 添加" Style="{StaticResource ActionButton}"
                            Command="{Binding AddProgramCommand}"/>
                    <Button Content="扫描" Style="{StaticResource ActionButton}"
                            Background="#5C2D91"
                            Command="{Binding ScanDirectoryCommand}"/>
                </StackPanel>
                <StackPanel Grid.Row="0" Orientation="Horizontal" Margin="0,32,0,10">
                    <Button Content="刷新" Style="{StaticResource ActionButton}"
                            Background="#666"
                            Command="{Binding RefreshCommand}"/>
                    <Button Content="全部启动" Style="{StaticResource ActionButton}"
                            Background="#228B22"
                            Command="{Binding StartAllCommand}"/>
                </StackPanel>

                <ListBox Grid.Row="1"
                         ItemsSource="{Binding Programs}"
                         SelectedItem="{Binding SelectedProgram}"
                         Background="Transparent"
                         BorderThickness="0"
                         ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                    <ListBox.ItemTemplate>
                        <DataTemplate DataType="{x:Type models:ProgramEntry}">
                            <Border Padding="8,6" CornerRadius="4" Margin="0,0,0,2">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{Binding StatusIcon}" FontSize="12"
                                               VerticalAlignment="Center" Margin="0,0,8,0"/>
                                    <StackPanel>
                                        <TextBlock Text="{Binding Name}" FontWeight="SemiBold"
                                                   FontSize="13"/>
                                        <TextBlock Text="{Binding LoginUrl}"
                                                   FontSize="11" Foreground="#666"
                                                   TextTrimming="CharacterEllipsis"
                                                   MaxWidth="210"/>
                                    </StackPanel>
                                </StackPanel>
                            </Border>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </Grid>
        </Border>

        <GridSplitter Grid.Column="1" Width="3" HorizontalAlignment="Stretch"
                      Background="#E0E0E0"/>

        <!-- Right Panel -->
        <Border Grid.Column="2" Background="White">
            <Grid Margin="24" x:Name="DetailPanel"
                  Visibility="{Binding SelectedProgram, Converter={StaticResource NullToCollapsed}}">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <!-- Header with status -->
                <StackPanel Orientation="Horizontal" Margin="0,0,0,16">
                    <TextBlock Text="{Binding SelectedProgram.StatusIcon}" FontSize="20"
                               VerticalAlignment="Center" Margin="0,0,10,0"/>
                    <TextBlock Text="{Binding SelectedProgram.Name}" FontSize="22"
                               FontWeight="Bold" VerticalAlignment="Center"/>
                    <TextBlock Text="{Binding SelectedProgram.Status, StringFormat=' [{0}]'}"
                               FontSize="14" Foreground="#888" VerticalAlignment="Center"
                               Margin="10,0,0,0"/>
                </StackPanel>

                <!-- Port info -->
                <Border Grid.Row="1" Background="#F8F8F8" CornerRadius="6" Padding="16" Margin="0,0,0,12">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>

                        <TextBlock Text="API 端口:" FontWeight="SemiBold" Foreground="#555"
                                   Margin="0,0,16,6"/>
                        <TextBlock Grid.Column="1" Text="{Binding SelectedProgram.ApiPort}"
                                   FontSize="16" FontWeight="Bold" Foreground="#333"/>

                        <TextBlock Grid.Row="1" Text="Web 端口:" FontWeight="SemiBold" Foreground="#555"
                                   Margin="0,6,16,6"/>
                        <TextBlock Grid.Row="1" Grid.Column="1"
                                   Text="{Binding SelectedProgram.WebPort}"
                                   FontSize="16" FontWeight="Bold" Foreground="#333"/>

                        <TextBlock Grid.Row="2" Text="WS 端口:" FontWeight="SemiBold" Foreground="#555"
                                   Margin="0,6,16,6"/>
                        <TextBlock Grid.Row="2" Grid.Column="1"
                                   Text="{Binding SelectedProgram.WsPort}"
                                   FontSize="16" FontWeight="Bold" Foreground="#333"/>

                        <TextBlock Grid.Row="3" Text="登录地址:" FontWeight="SemiBold" Foreground="#555"
                                   Margin="0,6,16,0"/>
                        <StackPanel Grid.Row="3" Grid.Column="1" Orientation="Horizontal" Margin="0,6,0,0">
                            <TextBlock Text="{Binding SelectedProgram.LoginUrl}"
                                       FontSize="14" Foreground="#0078D4"
                                       TextDecorations="Underline" VerticalAlignment="Center"/>
                            <Button Content="复制" Style="{StaticResource IconButton}"
                                    FontSize="11" Width="44" Height="22"
                                    Margin="10,0,0,0"
                                    Foreground="#0078D4"
                                    Command="{Binding CopyUrlCommand}"
                                    CommandParameter="{Binding SelectedProgram}"/>
                        </StackPanel>
                    </Grid>
                </Border>

                <!-- Directory -->
                <Border Grid.Row="2" Background="#F8F8F8" CornerRadius="6" Padding="16" Margin="0,0,0,12">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="项目目录:" FontWeight="SemiBold" Foreground="#555"
                                   Margin="0,0,10,0" VerticalAlignment="Center"/>
                        <TextBlock Text="{Binding SelectedProgram.Directory}"
                                   FontSize="13" Foreground="#666"
                                   TextTrimming="CharacterEllipsis"
                                   VerticalAlignment="Center"/>
                    </StackPanel>
                </Border>

                <!-- Port status -->
                <Border Grid.Row="3" Padding="16,10" Margin="0,0,0,4">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock x:Name="PortStatusLabel" Text="{Binding PortStatusText}"
                                   FontSize="13"/>
                    </StackPanel>
                </Border>

                <!-- Batch files -->
                <Border Grid.Row="4" Padding="16,0" Margin="0,0,0,12">
                    <StackPanel>
                        <TextBlock FontSize="12" Foreground="#999" Margin="0,4,0,2">
                            <Run Text="Start: "/>
                            <Run Text="{Binding SelectedProgram.StartBat}"/>
                        </TextBlock>
                        <TextBlock FontSize="12" Foreground="#999" Margin="0,2,0,2">
                            <Run Text="Stop:  "/>
                            <Run Text="{Binding SelectedProgram.StopBat}"/>
                        </TextBlock>
                        <TextBlock FontSize="12" Foreground="#999">
                            <Run Text="Restart: "/>
                            <Run Text="{Binding SelectedProgram.RestartBat}"/>
                        </TextBlock>
                    </StackPanel>
                </Border>

                <!-- Action buttons -->
                <StackPanel Grid.Row="9" Orientation="Horizontal" Margin="0,12,0,0">
                    <Button Content="启动" Style="{StaticResource ActionButton}"
                            Background="#228B22"
                            Command="{Binding StartProgramCommand}"
                            CommandParameter="{Binding SelectedProgram}"
                            IsEnabled="{Binding SelectedProgram.IsStopped}"/>
                    <Button Content="停止" Style="{StaticResource ActionButton}"
                            Background="#DC143C"
                            Command="{Binding StopProgramCommand}"
                            CommandParameter="{Binding SelectedProgram}"
                            IsEnabled="{Binding SelectedProgram.IsRunning}"/>
                    <Button Content="重启" Style="{StaticResource ActionButton}"
                            Background="#FF8C00"
                            Command="{Binding RestartProgramCommand}"
                            CommandParameter="{Binding SelectedProgram}"/>
                    <Button Content="编辑" Style="{StaticResource ActionButton}"
                            Background="#666"
                            Command="{Binding EditProgramCommand}"
                            CommandParameter="{Binding SelectedProgram}"/>
                    <Button Content="删除" Style="{StaticResource ActionButton}"
                            Background="#999"
                            Command="{Binding DeleteProgramCommand}"
                            CommandParameter="{Binding SelectedProgram}"/>
                </StackPanel>
            </Grid>

            <!-- Empty state -->
            <TextBlock x:Name="EmptyHint"
                       Text="选择左侧程序查看详情，或点击 [+ 添加] 添加新程序"
                       FontSize="16" Foreground="#BBB"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Visibility="{Binding SelectedProgram, Converter={StaticResource NullToVisible}}"/>
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 2: Write MainWindow code-behind**

**`src/AppManager/MainWindow.xaml.cs`:**
```csharp
using System.ComponentModel;
using System.Windows;
using AppManager.ViewModels;

namespace AppManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
```

- [ ] **Step 3: Add value converters for visibility binding**

**`src/AppManager/Converters/NullToVisibilityConverters.cs`:**
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AppManager.Converters;

public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 4: Update App.xaml to register converters**

**`src/AppManager/App.xaml`:**
```xml
<Application x:Class="AppManager.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:AppManager.Converters"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <converters:NullToCollapsedConverter x:Key="NullToCollapsed"/>
        <converters:NullToVisibleConverter x:Key="NullToVisible"/>
    </Application.Resources>
</Application>
```

- [ ] **Step 5: Create Converters directory and build**

```bash
New-Item -ItemType Directory -Path "src\AppManager\Converters" -Force
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/AppManager/MainWindow.xaml src/AppManager/MainWindow.xaml.cs src/AppManager/Converters/ src/AppManager/App.xaml
git commit -m "feat: build main window UI with split layout and full binding"
```

---

### Task 10: Add System Tray Support

**Files:**
- Modify: `src/AppManager/App.xaml.cs`
- Modify: `src/AppManager/MainWindow.xaml.cs`

- [ ] **Step 1: Update App.xaml.cs for tray icon**

Rewrite `src/AppManager/App.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Forms;

namespace AppManager;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CreateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "程序管理器"
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, (s, e) => ShowMainWindow());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("退出", null, (s, e) => ExitApplication());

        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    private void ExitApplication()
    {
        _trayIcon?.Dispose();
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 2: Update MainWindow.xaml.cs to support tray minimize**

Modify `src/AppManager/MainWindow.xaml.cs` — replace the `Window_Closing` method and add `OnStateChanged`:

```csharp
using System.ComponentModel;
using System.Windows;
using AppManager.ViewModels;

namespace AppManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
```

- [ ] **Step 3: Update App.xaml to use proper Startup**

Modify `src/AppManager/App.xaml` — replace `StartupUri` with `Startup`:
```xml
<Application x:Class="AppManager.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:converters="clr-namespace:AppManager.Converters"
             Startup="Application_Startup">
    <Application.Resources>
        <converters:NullToCollapsedConverter x:Key="NullToCollapsed"/>
        <converters:NullToVisibleConverter x:Key="NullToVisible"/>
    </Application.Resources>
</Application>
```

Add `Application_Startup` handler to `App.xaml.cs`:
```csharp
private void Application_Startup(object sender, StartupEventArgs e)
{
    var mainWindow = new MainWindow();
    mainWindow.Show();
}
```

- [ ] **Step 4: Build and fix**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded. Fix any compilation errors.

- [ ] **Step 5: Commit**

```bash
git add src/AppManager/App.xaml src/AppManager/App.xaml.cs src/AppManager/MainWindow.xaml.cs
git commit -m "feat: add system tray support with minimize to tray"
```

---

### Task 11: Final Build and Verify

- [ ] **Step 1: Full build**

```bash
Set-Location -LiteralPath "F:\opencode\appmanager"
dotnet build
```
Expected: Build succeeded with zero errors.

- [ ] **Step 2: Verify all source files exist**

```bash
Get-ChildItem -LiteralPath "src\AppManager" -Recurse -File | Select-Object FullName
```

Expected files:
- `App.xaml`, `App.xaml.cs`
- `MainWindow.xaml`, `MainWindow.xaml.cs`
- `Models/ProgramEntry.cs`
- `Services/DatabaseService.cs`
- `Services/ProcessService.cs`
- `Services/PortChecker.cs`
- `Services/ScannerService.cs`
- `ViewModels/MainViewModel.cs`
- `Views/ProgramDialog.xaml`, `Views/ProgramDialog.xaml.cs`
- `Converters/NullToVisibilityConverters.cs`

- [ ] **Step 3: Commit final**

```bash
git add -A
git commit -m "chore: finalize project structure and verify build"
```
