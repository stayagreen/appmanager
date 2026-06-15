using CommunityToolkit.Mvvm.ComponentModel;

namespace AppManager.Models;

public partial class ProgramEntry : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
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
    [NotifyPropertyChangedFor(nameof(StatusIcon))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsStopped))]
    private string _status = "Stopped";

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    public string StartCommand { get; set; } = string.Empty;
    public string StopMethod { get; set; } = string.Empty;

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
