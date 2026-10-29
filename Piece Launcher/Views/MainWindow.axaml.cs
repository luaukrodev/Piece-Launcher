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
            => WindowState = WindowState.Minimized;

        private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => Close();

        private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private bool isMaximized = false;

 
        private void OnMaximizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!isMaximized)
            {
                isMaximized = true;
                WindowState = WindowState.Maximized;
            } else
            {
                isMaximized = false;
                WindowState = WindowState.Normal;
            }
        }
    }
}