using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Piece_Launcher.ViewModels;

namespace Piece_Launcher.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Opened += OnOpened;
            Closed += OnClosed;
        }

        private void OnOpened(object? sender, System.EventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.MinimizeLauncherRequested += OnMinimizeLauncherRequested;
        }

        private void OnClosed(object? sender, System.EventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.MinimizeLauncherRequested -= OnMinimizeLauncherRequested;
        }

        private void OnMinimizeLauncherRequested()
            => MinimizeWindow();

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else
                    BeginMoveDrag(e);
            }
        }

        private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => Close();

        private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => MinimizeWindow();

        private void MinimizeWindow()
        {
            if (DataContext is MainWindowViewModel viewModel
                && viewModel.MinimizeToTray
                && Application.Current is App app
                && app.TryHideInTray(this))
                return;

            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
