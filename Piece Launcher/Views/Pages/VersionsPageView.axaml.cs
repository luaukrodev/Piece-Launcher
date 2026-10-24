using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace Piece_Launcher.Views.Pages
{
    public partial class VersionsPageView : UserControl
    {
        public VersionsPageView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
