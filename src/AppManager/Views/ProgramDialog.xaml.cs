using System.Windows;
using AppManager.Models;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

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
            System.Windows.MessageBox.Show("请输入程序名称。", "验证",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
