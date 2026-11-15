using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Piece_Launcher.Models;
using Piece_Launcher.ViewModels.Pages;

namespace Piece_Launcher.Views.Pages
{
    public partial class JavaEditionPageView : UserControl
    {
        public JavaEditionPageView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnProfileItemClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is JavaEditionPageViewModel viewModel &&
                sender is Button { DataContext: ProfileEntry profile })
            {
                viewModel.ChooseProfile(profile);
            }
        }

        private void OnVersionItemClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is JavaEditionPageViewModel viewModel && sender is Button button &&
                button.DataContext is InstalledVersionOption version)
            {
                viewModel.ChooseVersion(version.VersionId);
            }
        }
    }
}
