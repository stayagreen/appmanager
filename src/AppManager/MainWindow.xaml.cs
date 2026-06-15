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
        var result = System.Windows.MessageBox.Show(
            "退出程序管理器将结束所有托管的项目进程。\n\n确定退出？",
            "确认退出", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.KillAllProcesses();
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            e.Cancel = true;
        }
    }
}
