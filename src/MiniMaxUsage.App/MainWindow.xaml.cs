using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniMaxUsage.App.Models;
using MiniMaxUsage.App.ViewModels;

namespace MiniMaxUsage.App;

public partial class MainWindow : Window
{
    // P1-10 修复:直接接收 VM,不再依赖 DataContext as MainViewModel 在 Loaded 里赋值,
    // 避免 Loaded 之前点击 Range 按钮导致 NRE
    private readonly MainViewModel _viewModel;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // N2 修复:首次 Load() 必须在构造期同步触发,否则窗口打开后 15s 内只显示「正在加载…」
        // DispatcherTimer 15s 才会首跳,体感太慢
        _viewModel.Load();

        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _refreshTimer.Tick += (_, _) => _viewModel.Load();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // P1-11 修复:用 e.ChangedButton 而非 e.LeftButton,语义上区分"按下的瞬间"和"当前状态"
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshAsync();

    // P1-8 修复:Range 按钮的选中态改由 Style + DataTrigger + Tag 自动切换,
    // 这里只需要更新 VM.SelectedRange 即可,不再需要手动 ClearValue 按钮样式

    private void Range24h_Click(object sender, RoutedEventArgs e)
        => _viewModel.SelectedRange = TrendRange.Hours24;

    private void Range7d_Click(object sender, RoutedEventArgs e)
        => _viewModel.SelectedRange = TrendRange.Days7;

    private void Range30d_Click(object sender, RoutedEventArgs e)
        => _viewModel.SelectedRange = TrendRange.Days30;
}