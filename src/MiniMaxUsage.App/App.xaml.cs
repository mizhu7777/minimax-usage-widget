using System.IO;
using System.Windows;
using System.Windows.Threading;
using MiniMaxUsage.App.Services;
using MiniMaxUsage.App.ViewModels;

namespace MiniMaxUsage.App;

public partial class App : Application
{
    // N7 修复:项目根目录在 ProjectLocator.Resolve 成功后保存,LogFatal 用它而不是 5 级 ../
    private static string? _projectRootForLog;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // P2-10 修复:全局未捕获异常兜底,避免神秘闪退
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogFatal("AppDomain", args.ExceptionObject as Exception);
        };

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var location = ProjectLocator.Resolve(e.Args, baseDir);

        if (!location.IsSuccess)
        {
            var error = location.Error ?? "无法定位项目目录。";
            // P3-3 修复:在 MainWindow 尚未创建时,owner 传 null,任务栏不显示
            // 这里无 window,接受任务栏不亮的限制
            MessageBox.Show(error, "MiniMax Usage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var projectRoot = location.ProjectDirectory!;
        _projectRootForLog = projectRoot;
        var viewModel = new MainViewModel(projectRoot, new UsageCacheReader(), new HistoryReader(), () => DateTimeOffset.Now);

        // P1-7 修复:不再在这里同步 Load(),改由 MainWindow 在构造期统一读一次,
        // 避免 history.jsonl 大时冷启动可感知卡顿,以及"读两遍"的浪费
        var window = new MainWindow(viewModel);
        window.Show();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal("Dispatcher", e.Exception);
        var msg = e.Exception.Message + Environment.NewLine + Environment.NewLine
            + "详细信息已写入 widget.log" + Environment.NewLine + Environment.NewLine
            + "是否继续运行？应用状态可能已不稳定，选择「否」退出。";
        // O8 修复:不再无条件吞掉异常 —— 由用户决定继续(可能半坏状态)还是退出,
        // 避免"界面看起来活着但数据/绑定已经坏掉"的静默故障
        var choice = MessageBox.Show(msg, "MiniMax Usage - 未捕获异常", MessageBoxButton.YesNo, MessageBoxImage.Error);
        e.Handled = choice == MessageBoxResult.Yes;
    }

    private static void LogFatal(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            // N7 修复:用 ProjectLocator 找到的项目根目录,而不是 5 级 ../
            // 找不到 projectRoot 时退化到 LocalAppData,绝不写 C 盘根目录
            string logFile;
            if (!string.IsNullOrEmpty(_projectRootForLog))
            {
                logFile = Path.Combine(_projectRootForLog, ".cache", "widget.log");
            }
            else
            {
                logFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MiniMaxUsage", "widget.log");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] [FATAL][{source}] {ex}{Environment.NewLine}";
            File.AppendAllText(logFile, line);
        } catch { }
    }
}