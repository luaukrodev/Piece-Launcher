using Avalonia.Markup.Xaml;
using Avalonia.Controls;

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
    }
}
