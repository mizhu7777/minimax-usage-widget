using System.Windows;
using MiniMaxUsage.App.Services;
using MiniMaxUsage.App.ViewModels;

namespace MiniMaxUsage.App;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var location = ProjectLocator.Resolve(e.Args, baseDir);

        if (!location.IsSuccess)
        {
            var error = location.Error ?? "无法定位项目目录。";
            MessageBox.Show(error, "MiniMax Usage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var projectRoot = location.ProjectDirectory!;
        var viewModel = new MainViewModel(projectRoot, new UsageCacheReader(), new HistoryReader(), () => DateTimeOffset.Now);
        viewModel.Load();

        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}