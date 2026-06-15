using System.ComponentModel;
using System.Windows;
using AppManager.ViewModels;

namespace AppManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_isExiting) return;

        var result = System.Windows.MessageBox.Show(
            "退出将结束所有项目进程。\n\n确定退出？",
            "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _isExiting = true;
            _viewModel.StopAllProcesses();
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            e.Cancel = true;
        }
    }
}
