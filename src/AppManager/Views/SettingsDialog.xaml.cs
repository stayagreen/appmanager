using System.Windows;
using System.Windows.Controls;

namespace AppManager.Views;

public partial class SettingsDialog : Window
{
    private bool _visible;
    private string _apiKey;

    public SettingsDialog(string currentKey)
    {
        InitializeComponent();
        TxtApiKey.Text = currentKey;
        TxtApiKey.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        _apiKey = currentKey;
    }

    private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
    {
        _visible = !_visible;
        TxtApiKey.FontFamily = _visible
            ? new System.Windows.Media.FontFamily("Segoe UI")
            : new System.Windows.Media.FontFamily("Consolas");
        ((System.Windows.Controls.Button)sender).Content = _visible ? "隐藏" : "显示";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _apiKey = TxtApiKey.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public string ApiKey => _apiKey;
}
