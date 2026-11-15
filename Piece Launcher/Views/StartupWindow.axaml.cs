using Avalonia.Controls;
using Avalonia.Input;
using Piece_Launcher.ViewModels;
using System;
using System.Threading.Tasks;

namespace Piece_Launcher.Views
{
    public partial class StartupWindow : Window
    {
        public StartupWindow()
        {
            InitializeComponent();
            Opened += OnOpened;
            Closing += OnClosing;
        }

        private async void OnOpened(object? sender, EventArgs e)
        {
            if (DataContext is StartupWindowViewModel vm)
            {
                await vm.StartAsync();
            }
        }

        public async Task TransitionToMainWindowAsync(MainWindow mainWindow)
        {
            ArgumentNullException.ThrowIfNull(mainWindow);

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary
                ?? throw new InvalidOperationException("The startup window is not on an available screen.");
            var scaling = screen.Scaling;
            var workingArea = screen.WorkingArea;
            var targetWidth = Math.Min(mainWindow.Width, workingArea.Width / scaling);
            var targetHeight = Math.Min(mainWindow.Height, workingArea.Height / scaling);

            if (targetWidth <= 0 || targetHeight <= 0)
                throw new InvalidOperationException("The main window does not fit in the screen working area.");

            if (DataContext is not StartupWindowViewModel viewModel)
                throw new InvalidOperationException("The startup window has no view model.");

            mainWindow.MinWidth = Math.Min(mainWindow.MinWidth, targetWidth);
            mainWindow.MinHeight = Math.Min(mainWindow.MinHeight, targetHeight);
            mainWindow.Width = targetWidth;
            mainWindow.Height = targetHeight;
            mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;

            var targetPosition = new Avalonia.PixelPoint(
                workingArea.X + (int)Math.Round((workingArea.Width - targetWidth * scaling) / 2),
                workingArea.Y + (int)Math.Round((workingArea.Height - targetHeight * scaling) / 2));
            mainWindow.Position = targetPosition;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = targetWidth;
            Height = targetHeight;
            Position = targetPosition;

            viewModel.TransitionWidth = 520;
            viewModel.TransitionHeight = 320;
            viewModel.TransitionOpacity = 1;
            viewModel.IsTransitioning = true;
            await Task.Delay(16);
            viewModel.TransitionWidth = targetWidth;
            viewModel.TransitionHeight = targetHeight;
            await Task.Delay(460);
        }

        public async Task FadeOutAndCloseAsync()
        {
            if (DataContext is StartupWindowViewModel viewModel)
            {
                viewModel.TransitionOpacity = 0;
                await Task.Delay(260);
            }

            Close();
        }

        public void CancelTransition()
        {
            if (DataContext is StartupWindowViewModel viewModel)
            {
                viewModel.TransitionWidth = 520;
                viewModel.TransitionHeight = 320;
                viewModel.TransitionOpacity = 1;
                viewModel.IsTransitioning = false;
            }
        }

        private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is StartupWindowViewModel vm && vm.IsUpdateConfirmationVisible)
            {
                vm.ContinueWithoutUpdateCommand.Execute(null);
                return;
            }

            Close();
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (DataContext is StartupWindowViewModel vm && !vm.IsCompleted)
                e.Cancel = true;
        }
    }
}
