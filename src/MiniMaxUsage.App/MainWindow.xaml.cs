using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniMaxUsage.App.Models;
using MiniMaxUsage.App.ViewModels;

namespace MiniMaxUsage.App;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _refreshTimer.Tick += (_, _) => _viewModel?.Load();
        _refreshTimer.Start();
        Loaded += (_, _) => {
            _viewModel = DataContext as MainViewModel;
            _viewModel?.Load();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null) await _viewModel.RefreshAsync();
    }

    private void Range24h_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedRange = TrendRange.Hours24;
        UpdateRangeButtons(sender);
    }

    private void Range7d_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedRange = TrendRange.Days7;
        UpdateRangeButtons(sender);
    }

    private void Range30d_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedRange = TrendRange.Days30;
        UpdateRangeButtons(sender);
    }

    private void UpdateRangeButtons(object selected)
    {
        var parent = ((Button)selected).Parent as StackPanel;
        if (parent is null) return;
        foreach (var child in parent.Children)
        {
            if (child is Button btn)
            {
                btn.ClearValue(Button.BackgroundProperty);
                btn.ClearValue(Button.ForegroundProperty);
            }
        }
        if (selected is Button sel)
        {
            sel.Background = new SolidColorBrush(Color.FromRgb(234, 242, 255));
            sel.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        }
    }
}