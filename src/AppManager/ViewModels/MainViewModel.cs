using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppManager.Models;
using AppManager.Services;
using AppManager.Views;

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

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanProgressText = "";

    [ObservableProperty]
    private double _scanProgressValue;

    public MainViewModel()
    {
        _db = new DatabaseService();
        _portChecker = new PortChecker();
        _process = new ProcessService(_portChecker);
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
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            var entry = dialog.Result!;
            if (_db.ExistsByName(entry.Name))
            {
                System.Windows.MessageBox.Show($"程序 \"{entry.Name}\" 已存在。", "重复", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            var updated = dialog.Result!;
            if (_db.ExistsByName(updated.Name, entry.Id))
            {
                System.Windows.MessageBox.Show($"程序 \"{updated.Name}\" 已存在。", "重复", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        var result = System.Windows.MessageBox.Show(
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
            if (System.Windows.MessageBox.Show(msg, "端口冲突", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        try
        {
            entry.LogOutput = "";
            _process.Start(entry);
            entry.Status = "Running";
            _db.Update(entry);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            System.Windows.MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            System.Windows.MessageBox.Show($"重启失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
        System.Windows.Clipboard.SetText(entry.LoginUrl);
    }

    [RelayCommand]
    private async Task ScanDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择要扫描的根目录"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            IsScanning = true;
            ScanProgressValue = 0;

            var results = await _scanner.ScanDirectoryAsync(dialog.SelectedPath, (current, total, name) =>
            {
                ScanProgressText = $"正在扫描: {name} ({current}/{total})";
                ScanProgressValue = (double)current / total * 100;
            });
            if (results.Count == 0)
            {
                IsScanning = false;
                ScanProgressText = "";
                System.Windows.MessageBox.Show("未发现任何项目（未找到 start.bat）。", "扫描结果", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var imported = 0;
            var aiCount = 0;
            foreach (var result in results)
            {
                if (_db.ExistsByDirectory(result.Entry.Directory)) continue;

                result.Entry.CreatedAt = DateTime.UtcNow.ToString("o");
                result.Entry.UpdatedAt = DateTime.UtcNow.ToString("o");
                _db.Insert(result.Entry);
                Programs.Add(result.Entry);
                if (!string.IsNullOrWhiteSpace(result.Entry.StartCommand))
                    aiCount++;
                imported++;
            }

            var aiMsg = aiCount > 0 ? $"\n其中 {aiCount} 个项目已通过 AI 分析。" : "\nAI 未启用（请在设置中配置 API Key）。";
            System.Windows.MessageBox.Show($"从 {results.Count} 个项目中导入了 {imported} 个新程序。" +
                $"跳过 {results.Count - imported} 个已存在的程序。{aiMsg}", "扫描完成",
                MessageBoxButton.OK, MessageBoxImage.Information);

            IsScanning = false;
            ScanProgressText = "";
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshAllStatus();
    }

    [RelayCommand]
    private void SavePorts(ProgramEntry? entry)
    {
        if (entry == null) return;
        _db.Update(entry);
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

    [RelayCommand]
    private void OpenSettings()
    {
        var config = AppConfig.Load();
        var dialog = new Views.SettingsDialog(config.OpenCodeApiKey);
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true)
        {
            config.OpenCodeApiKey = dialog.ApiKey;
            config.Save();
        }
    }
}
