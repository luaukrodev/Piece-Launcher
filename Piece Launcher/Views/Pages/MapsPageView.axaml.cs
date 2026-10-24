using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace Piece_Launcher.Views.Pages
{
    public partial class MapsPageView : UserControl
    {
        public MapsPageView()
        {
            InitializeComponent();
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
