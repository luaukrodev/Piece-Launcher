using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Piece_Launcher.Helpers;
using Piece_Launcher.ViewModels.Pages;
using System;
using System.Threading.Tasks;

namespace Piece_Launcher.Views.Pages
{
    public partial class MinecraftModsPageView : UserControl
    {
        public MinecraftModsPageView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnImportPackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not MinecraftModsPageViewModel viewModel)
                return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this)
                    ?? throw new InvalidOperationException("The file picker is unavailable.");
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = LocalizationHelper.Get("Lang_ModsManager_ImportPack"),
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Minecraft modpacks")
                        {
                            Patterns = new[] { "*.mrpack", "*.zip" }
                        }
                    }
                });
                if (files.Count == 0)
                    return;

                await using var stream = await files[0].OpenReadAsync();
                await viewModel.ImportModpackAsync(stream, files[0].Name);
            }
            catch (Exception ex)
            {
                viewModel.ShowModpackImportError(ex.Message);
            }
        }
    }
}
