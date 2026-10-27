using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Piece_Launcher.ViewModels.Pages;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Piece_Launcher.Views.Pages
{
    public partial class ProfileManagementPageView : UserControl
    {
        private const int MaximumSkinFileBytes = 1024 * 1024;

        public ProfileManagementPageView()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void OnEditSkinClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is not ProfileManagementPageViewModel viewModel)
                return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this)
                    ?? throw new InvalidOperationException("The file picker is unavailable.");
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select a Minecraft skin PNG",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Minecraft skin PNG")
                        {
                            Patterns = new[] { "*.png" }
                        }
                    }
                });

                if (files.Count == 0)
                    return;

                await using var fileStream = await files[0].OpenReadAsync();
                using var buffer = new MemoryStream();
                var readBuffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(readBuffer)) > 0)
                {
                    if (buffer.Length + bytesRead > MaximumSkinFileBytes)
                        throw new InvalidDataException("Minecraft skin PNG files must be 1 MB or smaller.");

                    await buffer.WriteAsync(readBuffer.AsMemory(0, bytesRead));
                }

                viewModel.PreviewSkin(buffer.ToArray());
            }
            catch (Exception ex)
            {
                viewModel.ShowSkinError(ex.Message);
            }
        }
    }
}
